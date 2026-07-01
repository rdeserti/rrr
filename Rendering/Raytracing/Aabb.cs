using System;
using rrr.VMath;

namespace rrr.Rendering.Raytracing;

/// <summary>
/// Axis-aligned bounding box. Used as the BVH node volume and the per-triangle
/// bound during construction.
/// </summary>
public struct Aabb
{
    public Vector3f Min;
    public Vector3f Max;

    public static Aabb Empty()
    {
        return new Aabb
        {
            Min = new Vector3f(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity),
            Max = new Vector3f(float.NegativeInfinity, float.NegativeInfinity, float.NegativeInfinity)
        };
    }

    public void Grow(Vector3f p)
    {
        Min = Vector3f.Min(Min, p);
        Max = Vector3f.Max(Max, p);
    }

    public void Grow(in Aabb b)
    {
        Min = Vector3f.Min(Min, b.Min);
        Max = Vector3f.Max(Max, b.Max);
    }

    public Vector3f Centroid => (Min + Max) * 0.5f;

    public Vector3f Extent => Max - Min;

    /// <summary>
    /// Slab test. Returns true if the ray enters the box before
    /// <paramref name="tMax"/>. <paramref name="invDir"/> is 1/Direction
    /// precomputed once per ray (a zero component yields ±∞, which the
    /// min/max comparisons handle correctly).
    /// </summary>
    public bool Intersect(in Ray ray, Vector3f invDir, float tMax)
    {
        float t1 = (Min.X - ray.Origin.X) * invDir.X;
        float t2 = (Max.X - ray.Origin.X) * invDir.X;
        float tmin = MathF.Min(t1, t2);
        float tmax = MathF.Max(t1, t2);

        t1 = (Min.Y - ray.Origin.Y) * invDir.Y;
        t2 = (Max.Y - ray.Origin.Y) * invDir.Y;
        tmin = MathF.Max(tmin, MathF.Min(t1, t2));
        tmax = MathF.Min(tmax, MathF.Max(t1, t2));

        t1 = (Min.Z - ray.Origin.Z) * invDir.Z;
        t2 = (Max.Z - ray.Origin.Z) * invDir.Z;
        tmin = MathF.Max(tmin, MathF.Min(t1, t2));
        tmax = MathF.Min(tmax, MathF.Max(t1, t2));

        return tmax >= MathF.Max(tmin, 0.0f) && tmin < tMax;
    }
}
