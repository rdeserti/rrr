using rrr.VMath;
using System;

namespace rrr.Rendering;

/// <summary>
/// Depth map rendered from a light's point of view. Sampling tells whether a
/// world-space point is occluded from that light (in shadow).
///
/// Depth is stored as <b>linear light-space distance</b> (the light-view Z), not
/// the perspective NDC z. Perspective z crams almost all its precision near the
/// light, so at typical occluder/receiver distances a tiny bias leaks light
/// through the umbra (a bright crescent under a sphere). Linear depth has uniform
/// precision, so a small constant bias removes self-shadow acne without leaking.
/// The perspective view-projection is still used only to find the texel (u,v).
/// </summary>
public sealed class ShadowMap
{
    private readonly float[] _depth;            // stored linear light-space Z per texel
    private readonly int _size;
    private readonly Matrix4x4f _lightViewProjection; // for the (u,v) lookup
    private readonly Matrix4x4f _lightView;     // for the linear receiver depth
    private readonly int _pcfRadius;
    private readonly float _worldTexel;
    private readonly float _softness;

    public ShadowMap(
        float[] depth,
        int size,
        Matrix4x4f lightViewProjection,
        Matrix4x4f lightView,
        int pcfRadius,
        float worldTexel,
        float softness)
    {
        _depth = depth;
        _size = size;
        _lightViewProjection = lightViewProjection;
        _lightView = lightView;
        _pcfRadius = pcfRadius < 0 ? 0 : pcfRadius;
        _worldTexel = worldTexel;
        _softness = softness;
    }

    /// <summary>
    /// Returns 1 if the point is lit by this light, 0 if it is in shadow.
    /// <paramref name="L"/> is the (normalized) direction toward the light.
    /// </summary>
    public float Sample(Vector3f worldPosition, Vector3f normal, Vector3f L)
    {
        Vector3f n = normal.Normalized();

        // Normal-offset: push the sample off the surface by ~1 texel, more at
        // grazing angles. Lifts the receiver out of the marginal contact zone so
        // it does not self-alias against the occluder at the contact line.
        float ndotl = MathF.Max(0.0f, Vector3f.Dot(n, L));
        float slope = MathF.Sqrt(MathF.Max(0.0f, 1.0f - ndotl * ndotl));

        Vector3f sampleP = worldPosition + n * (_worldTexel * (1.0f + 2.0f * slope));

        // (u,v) from the perspective projection (matches how occluders were
        // rasterized into the map).
        Vector4f clip =
            _lightViewProjection * Vector4f.FromVector3(sampleP, 1.0f);

        if (clip.W <= 0.0f)
            return 1.0f; // behind the light

        Vector3f ndc = clip.PerspectiveDivide();

        float u = ndc.X * 0.5f + 0.5f;
        float v = ndc.Y * 0.5f + 0.5f;

        if (u < 0.0f || u > 1.0f || v < 0.0f || v > 1.0f ||
            ndc.Z < 0.0f || ndc.Z > 1.0f)
        {
            return 1.0f; // outside the shadow frustum -> treat as lit
        }

        int cx = (int)(u * _size);
        int cy = (int)((1.0f - v) * _size);

        // Receiver depth = linear light-space distance, compared against the
        // stored occluder distance with a small constant bias in the SAME linear
        // units. Because linear depth has uniform precision, this small bias is
        // enough everywhere and does NOT leak through the umbra (the old NDC-z
        // map needed a bias so large at typical distances that it bored a bright
        // hole in contact shadows).
        float receiverZ = (_lightView * Vector4f.FromVector3(sampleP, 1.0f)).Z;
        float bias = _worldTexel * 0.5f;
        float compare = receiverZ - bias;

        if (_softness > 0.0f)
            return SamplePcss(cx, cy, compare);

        return Pcf(cx, cy, compare, _pcfRadius);
    }

    private float SamplePcss(int cx, int cy, float compare)
    {
        // 1. Blocker search: average distance of texels closer to the light.
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

        // 2. Penumbra width via similar triangles (linear distances).
        float penumbra =
            (compare - avgBlocker) / MathF.Max(avgBlocker, 1e-4f) * _softness;

        int kernel = Math.Clamp((int)MathF.Ceiling(penumbra), 1, 12);

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
