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

    public ShadowMap(
        float[] depth,
        int size,
        Matrix4x4f lightViewProjection,
        int pcfRadius)
    {
        _depth = depth;
        _size = size;
        _lightViewProjection = lightViewProjection;
        _pcfRadius = pcfRadius < 0 ? 0 : pcfRadius;
    }

    /// <summary>
    /// Returns 1 if the point is lit by this light, 0 if it is in shadow.
    /// <paramref name="L"/> is the (normalized) direction toward the light,
    /// used for a slope-scaled depth bias against shadow acne.
    /// </summary>
    public float Sample(Vector3f worldPosition, Vector3f normal, Vector3f L)
    {
        Vector4f clip =
            _lightViewProjection * Vector4f.FromVector3(worldPosition, 1.0f);

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

        // Slope-scaled depth bias, also scaled by the PCF kernel radius: a
        // wider kernel reaches texels farther across the surface, whose depth
        // differs more, so it needs proportionally more bias to avoid the
        // self-shadow acne (which PCF would otherwise smear into a gray veil).
        float ndotl = MathF.Max(0.0f, Vector3f.Dot(normal.Normalized(), L));
        float tanTheta = MathF.Sqrt(MathF.Max(0.0f, 1.0f - ndotl * ndotl))
                         / MathF.Max(ndotl, 0.05f);

        float bias = (0.0006f + 0.0015f * tanTheta) * (1 + _pcfRadius);
        bias = MathF.Min(bias, 0.02f);

        float compare = ndc.Z - bias;

        // PCF: average the depth test over a (2r+1)x(2r+1) neighborhood so
        // shadow edges fade out instead of being a hard step.
        int r = _pcfRadius;

        if (r == 0)
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
