using rrr.VMath;
using System;

namespace rrr.Rendering;

/// <summary>
/// Depth map rendered from a light's point of view. Sampling tells whether a
/// world-space point is occluded from that light (in shadow).
/// </summary>
public sealed class ShadowMap
{
    private readonly float[] _depth;
    private readonly int _size;
    private readonly Matrix4x4f _lightViewProjection;
    private readonly int _pcfRadius;
    private readonly float _worldTexel;
    private readonly float _softness;

    public ShadowMap(
        float[] depth,
        int size,
        Matrix4x4f lightViewProjection,
        int pcfRadius,
        float worldTexel,
        float softness)
    {
        _depth = depth;
        _size = size;
        _lightViewProjection = lightViewProjection;
        _pcfRadius = pcfRadius < 0 ? 0 : pcfRadius;
        _worldTexel = worldTexel;
        _softness = softness;
    }

    /// <summary>
    /// Returns 1 if the point is lit by this light, 0 if it is in shadow.
    /// <paramref name="L"/> is the (normalized) direction toward the light,
    /// used for a slope-scaled depth bias against shadow acne.
    /// </summary>
    public float Sample(Vector3f worldPosition, Vector3f normal, Vector3f L)
    {
        Vector3f n = normal.Normalized();

        // Normal-offset bias: push the sample point off the surface along the
        // normal (in world units, a few texels' worth, more at grazing angles
        // and wider PCF kernels). This removes acne without peter-panning and
        // is scene-scale aware, so the depth bias below can stay small.
        float ndotl = MathF.Max(0.0f, Vector3f.Dot(n, L));
        float slope = MathF.Sqrt(MathF.Max(0.0f, 1.0f - ndotl * ndotl));
        float offset = _worldTexel * (1.0f + _pcfRadius) * (1.0f + 2.0f * slope);

        Vector3f sampleP = worldPosition + n * offset;

        Vector4f clip =
            _lightViewProjection * Vector4f.FromVector3(sampleP, 1.0f);

        if (clip.W <= 0.0f)
            return 1.0f; // behind the light

        Vector3f ndc = clip.PerspectiveDivide();

        float u = ndc.X * 0.5f + 0.5f;
        float v = ndc.Y * 0.5f + 0.5f;

        // Outside the shadow map's frustum -> treat as lit.
        if (u < 0.0f || u > 1.0f || v < 0.0f || v > 1.0f ||
            ndc.Z < 0.0f || ndc.Z > 1.0f)
        {
            return 1.0f;
        }

        int cx = (int)(u * _size);
        int cy = (int)((1.0f - v) * _size);

        // Small constant depth bias; the heavy lifting against acne is done by
        // the normal-offset above plus front-face culling in the shadow pass.
        float compare = ndc.Z - 0.0004f;

        // PCSS: estimate the penumbra from the average blocker distance, then
        // do PCF with a kernel sized to that penumbra (contact hardening).
        if (_softness > 0.0f)
            return SamplePcss(cx, cy, compare);

        // Fixed-kernel PCF.
        return Pcf(cx, cy, compare, _pcfRadius);
    }

    private float SamplePcss(int cx, int cy, float compare)
    {
        // 1. Blocker search: average depth of texels closer to the light.
        int searchR = Math.Clamp((int)MathF.Ceiling(_softness), 1, 8);

        float sum = 0.0f;
        int count = 0;

        for (int dy = -searchR; dy <= searchR; dy++)
        {
            for (int dx = -searchR; dx <= searchR; dx++)
            {
                float d = DepthAt(cx + dx, cy + dy);
                if (d < compare)
                {
                    sum += d;
                    count++;
                }
            }
        }

        if (count == 0)
            return 1.0f; // no blockers -> fully lit

        float avgBlocker = sum / count;

        // 2. Penumbra width via similar triangles (exact for orthographic
        // depth, a good approximation for perspective).
        float penumbra =
            (compare - avgBlocker) / MathF.Max(avgBlocker, 1e-4f) * _softness;

        int kernel = Math.Clamp((int)MathF.Ceiling(penumbra), 1, 12);

        // 3. PCF with the penumbra-sized kernel.
        return Pcf(cx, cy, compare, kernel);
    }

    private float Pcf(int cx, int cy, float compare, int r)
    {
        if (r <= 0)
            return compare > DepthAt(cx, cy) ? 0.0f : 1.0f;

        float lit = 0.0f;
        int samples = 0;

        for (int dy = -r; dy <= r; dy++)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                lit += compare > DepthAt(cx + dx, cy + dy) ? 0.0f : 1.0f;
                samples++;
            }
        }

        return lit / samples;
    }

    private float DepthAt(int x, int y)
    {
        if (x < 0) x = 0; else if (x >= _size) x = _size - 1;
        if (y < 0) y = 0; else if (y >= _size) y = _size - 1;

        return _depth[y * _size + x];
    }
}
