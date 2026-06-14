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

    public ShadowMap(float[] depth, int size, Matrix4x4f lightViewProjection)
    {
        _depth = depth;
        _size = size;
        _lightViewProjection = lightViewProjection;
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

        int x = (int)(u * _size);
        int y = (int)((1.0f - v) * _size);

        if (x < 0) x = 0; else if (x >= _size) x = _size - 1;
        if (y < 0) y = 0; else if (y >= _size) y = _size - 1;

        float stored = _depth[y * _size + x];

        float ndotl = MathF.Max(0.0f, Vector3f.Dot(normal.Normalized(), L));
        float bias = MathF.Max(0.0008f, 0.004f * (1.0f - ndotl));

        return ndc.Z - bias > stored ? 0.0f : 1.0f;
    }
}
