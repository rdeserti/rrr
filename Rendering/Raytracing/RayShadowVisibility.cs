using System;
using System.Collections.Generic;
using rrr.Scene;
using rrr.VMath;

namespace rrr.Rendering.Raytracing;

/// <summary>
/// Ray-traced visibility: a light is occluded when a shadow ray from the hit
/// point toward the light hits any geometry before reaching it. Plugs into the
/// shared <see cref="Lighting.ShadeCore{TVis}"/> BRDF, so shadows are the only
/// difference from the rasterizer's shadow-map path.
///
/// Per-light gating honors <see cref="Light.CastsShadows"/> and the global
/// <c>RenderSettings.RayShadows</c>. A normal-offset bias pushes the ray origin
/// off the surface to avoid self-shadow acne.
///
/// Soft shadows: when <c>RenderSettings.ShadowSoftness</c> &gt; 0 the light is
/// treated as an area source — several shadow rays are cast toward jittered
/// points on a disk around the light and the unoccluded fraction is the
/// penumbra. The disk sample pattern (a Fibonacci/sunflower set) is rotated by
/// a per-point hash so neighboring pixels decorrelate (noise instead of banding).
/// Softness 0 (default) keeps a single hard ray — identical to P4.
/// </summary>
public readonly struct RayShadowVisibility : Lighting.IVisibility
{
    private readonly RayScene _scene;
    private readonly IReadOnlyList<Light> _lights;
    private readonly float _bias;
    private readonly bool _enabled;
    private readonly float _softRadius;
    private readonly Vector2f[]? _diskSamples;   // unit-disk offsets, null when hard
    private readonly bool _hasTransparent;

    // Max transparent surfaces a shadow ray may pass through before stopping.
    private const int MaxShadowSteps = 64;

    public RayShadowVisibility(
        RayScene scene, IReadOnlyList<Light> lights, float bias, bool enabled,
        float softRadius, int softSamples)
    {
        _scene = scene;
        _lights = lights;
        _bias = bias;
        _enabled = enabled;
        _softRadius = softRadius;
        _hasTransparent = scene.HasTransparentMaterials;

        _diskSamples =
            (softRadius > 0.0f && softSamples > 1)
                ? BuildDiskSamples(softSamples)
                : null;
    }

    public float Visibility(int lightIndex, Vector3f worldPosition, Vector3f n, Vector3f L)
    {
        if (!_enabled)
            return 1.0f;

        Light light = _lights[lightIndex];

        if (!light.CastsShadows)
            return 1.0f;

        // Surface faces away from the light: not lit regardless of occlusion,
        // so skip the shadow ray(s) (the diffuse/specular terms are already zero).
        if (Vector3f.Dot(n, L) <= 0.0f)
            return 0.0f;

        Vector3f origin = worldPosition + n * _bias;

        bool directional = light is DirectionalLight;
        Vector3f lightPosition = light switch
        {
            PointLight p => p.Position,
            SpotLight s => s.Position,
            _ => Vector3f.Zero
        };

        if (!directional && light is not PointLight && light is not SpotLight)
            return 1.0f; // unknown light type

        // Hard shadow: a single ray toward the light center.
        if (_diskSamples == null)
        {
            float maxDist = directional
                ? float.MaxValue
                : (lightPosition - worldPosition).Length();

            return Transmittance(origin, L, maxDist);
        }

        // Soft shadow: average occlusion over a disk of samples around the light.
        // Build a basis perpendicular to the light direction L.
        Vector3f t = Vector3f.Cross(
            MathF.Abs(L.Y) < 0.99f ? Vector3f.Up : Vector3f.Right, L).Normalized();
        Vector3f b = Vector3f.Cross(L, t);

        // Per-point rotation of the sample pattern (hash noise) to decorrelate.
        float angle = HashAngle(worldPosition);
        float ca = MathF.Cos(angle);
        float sa = MathF.Sin(angle);

        float lit = 0.0f;

        for (int i = 0; i < _diskSamples.Length; i++)
        {
            Vector2f d = _diskSamples[i];
            float dx = d.X * ca - d.Y * sa;
            float dy = d.X * sa + d.Y * ca;

            Vector3f offset = (t * dx + b * dy) * _softRadius;

            Vector3f dir;
            float maxDist;

            if (directional)
            {
                // Jitter the direction within a small cone (angular area light).
                dir = (L + offset).Normalized();
                maxDist = float.MaxValue;
            }
            else
            {
                Vector3f target = lightPosition + offset;
                Vector3f toTarget = target - origin;
                maxDist = toTarget.Length();
                dir = toTarget / maxDist;
            }

            lit += Transmittance(origin, dir, maxDist);
        }

        return lit / _diskSamples.Length;
    }

    /// <summary>
    /// Light transmittance along a shadow ray in [0,1]: 1 = unobstructed, 0 =
    /// fully blocked. Opaque scenes use the cheap any-hit test; otherwise the ray
    /// walks through transparent surfaces, multiplying their transmittance
    /// (glass passes <c>Transmission</c>, alpha-blend passes <c>1 - alpha</c>,
    /// masked cutouts pass fully below the cutoff) until an opaque hit or the
    /// light is reached.
    /// </summary>
    private float Transmittance(Vector3f origin, Vector3f dir, float maxDist)
    {
        float tMax = maxDist >= float.MaxValue ? float.MaxValue : maxDist - _bias * 2.0f;
        if (tMax <= 0.0f)
            return 1.0f; // light is within the bias shell: treat as unoccluded

        if (!_hasTransparent)
        {
            Ray ray = new Ray(origin, dir);
            return _scene.IntersectAny(in ray, tMax) ? 0.0f : 1.0f;
        }

        float transmit = 1.0f;
        float traveled = 0.0f;
        Vector3f o = origin;

        for (int step = 0; step < MaxShadowSteps; step++)
        {
            float segMax = tMax - traveled;
            if (segMax <= 1e-4f)
                break; // reached the light unobstructed

            Ray seg = new Ray(o, dir);
            if (!_scene.Intersect(in seg, segMax, out RayHit hit))
                break; // nothing more between here and the light

            float surface = SurfaceTransmit(in hit);
            if (surface <= 0.0f)
                return 0.0f; // opaque occluder

            transmit *= surface;
            if (transmit <= 0.01f)
                return 0.0f;

            traveled += hit.T + _bias;
            o = hit.Position + dir * _bias;
        }

        return transmit;
    }

    /// <summary>
    /// How much light a single surface lets through (0 = opaque). Mirrors the
    /// material rules used for camera rays: masked cutout (binary), glass
    /// (<c>Transmission</c>), alpha-blend (<c>1 - alpha</c>).
    /// </summary>
    private static float SurfaceTransmit(in RayHit hit)
    {
        Material? m = hit.Material;
        if (m == null)
            return 0.0f;

        if (m.AlphaMode == AlphaMode.Mask)
            return SampleAlpha(m, in hit) < m.AlphaCutoff ? 1.0f : 0.0f;

        if (m.Transmission > 0.0f)
            return m.Transmission;

        if (m.AlphaMode == AlphaMode.Blend)
            return 1.0f - SampleAlpha(m, in hit);

        return 0.0f;
    }

    private static float SampleAlpha(Material m, in RayHit hit)
    {
        float a = m.DiffuseColor.A * hit.Color.A;
        if (m.DiffuseTexture != null)
            a *= m.DiffuseTexture.Sample(hit.UV.X, hit.UV.Y).A;
        return a;
    }

    /// <summary>
    /// Fibonacci/sunflower distribution of <paramref name="count"/> points on
    /// the unit disk (even coverage, good for low sample counts).
    /// </summary>
    private static Vector2f[] BuildDiskSamples(int count)
    {
        Vector2f[] samples = new Vector2f[count];

        const float goldenAngle = 2.39996323f; // π(3 - √5)

        for (int i = 0; i < count; i++)
        {
            float r = MathF.Sqrt((i + 0.5f) / count);
            float theta = i * goldenAngle;
            samples[i] = new Vector2f(r * MathF.Cos(theta), r * MathF.Sin(theta));
        }

        return samples;
    }

    /// <summary>Deterministic [0, 2π) angle hashed from a world position.</summary>
    private static float HashAngle(Vector3f p)
    {
        float h = MathF.Sin(
            p.X * 12.9898f + p.Y * 78.233f + p.Z * 37.719f) * 43758.5453f;
        float frac = h - MathF.Floor(h);
        return frac * (2.0f * MathF.PI);
    }
}
