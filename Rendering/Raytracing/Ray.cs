using rrr.Core;
using rrr.Scene;
using rrr.VMath;

namespace rrr.Rendering.Raytracing;

/// <summary>
/// A half-line: all points <c>Origin + t * Direction</c> for <c>t &gt;= 0</c>.
/// <see cref="Direction"/> is expected to be normalized so <c>t</c> is a world
/// distance.
/// </summary>
public readonly struct Ray
{
    public readonly Vector3f Origin;
    public readonly Vector3f Direction;

    public Ray(Vector3f origin, Vector3f direction)
    {
        Origin = origin;
        Direction = direction;
    }

    public Vector3f At(float t) => Origin + Direction * t;
}

/// <summary>
/// Result of the nearest ray/scene intersection. Carries everything later
/// phases need (interpolated normal/uv, material) even though P1 only shades
/// with the geometric facing term.
/// </summary>
public struct RayHit
{
    /// <summary>Distance along the ray to the hit point.</summary>
    public float T;

    /// <summary>World-space hit position.</summary>
    public Vector3f Position;

    /// <summary>Interpolated, world-space shading normal (face normal fallback).</summary>
    public Vector3f Normal;

    /// <summary>Geometric (flat) face normal, world-space.</summary>
    public Vector3f GeometricNormal;

    /// <summary>Interpolated texture coordinates.</summary>
    public Vector2f UV;

    /// <summary>Interpolated tangent (world-space, not yet re-orthonormalized).</summary>
    public Vector3f Tangent;

    /// <summary>Interpolated tangent handedness (sign used by normal mapping).</summary>
    public float Handedness;

    /// <summary>Interpolated per-vertex color.</summary>
    public ColorRGBAf Color;

    /// <summary>Material of the hit surface (may be null).</summary>
    public Material? Material;

    /// <summary>Index of the hit triangle in the <see cref="RayScene"/>.</summary>
    public int TriangleIndex;
}
