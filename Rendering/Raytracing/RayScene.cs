using System;
using System.Collections.Generic;
using rrr.Core;
using rrr.Scene;
using rrr.VMath;

namespace rrr.Rendering.Raytracing;

/// <summary>
/// One world-space triangle ready for intersection. Positions and normals are
/// baked into world space here (applying <see cref="SceneObject.Transform"/>),
/// exactly mirroring what the rasterizer's <c>ProjectTriangle</c> does, so both
/// engines see the same geometry. UVs/normals are carried for later phases.
/// </summary>
public struct RayTriangle
{
    public Vector3f P0, P1, P2;   // world positions
    public Vector3f N0, N1, N2;   // world normals (face-normal fallback baked in)
    public Vector3f T0, T1, T2;   // world tangents
    public float H0, H1, H2;      // tangent handedness
    public Vector2f UV0, UV1, UV2;
    public ColorRGBAf C0, C1, C2; // per-vertex colors
    public Material? Material;
}

/// <summary>
/// World-space triangle soup with a BVH-accelerated nearest-hit query.
///
/// The BVH is a binary tree of axis-aligned boxes, built top-down by midpoint
/// split on the largest centroid axis (median fallback when a split degenerates)
/// and stored in a flat array for stackless-friendly, allocation-free iterative
/// traversal. <see cref="_order"/> holds triangle indices permuted into leaf
/// order; a leaf node references a contiguous slice of it.
/// </summary>
public sealed class RayScene
{
    private const float Epsilon = 1e-6f;
    private const int LeafSize = 2;
    private const int MaxStack = 64;

    private struct BvhNode
    {
        public Aabb Bounds;

        /// <summary>Leaf: first index in <c>_order</c>. Internal: left child node index (right = +1).</summary>
        public int LeftFirst;

        /// <summary>Triangle count for a leaf; 0 marks an internal node.</summary>
        public int Count;
    }

    private readonly RayTriangle[] _triangles;

    // BVH
    private readonly BvhNode[] _nodes;
    private readonly int _nodeCount;
    private readonly int[] _order;

    public IReadOnlyList<RayTriangle> Triangles => _triangles;

    public int BvhNodeCount => _nodeCount;

    /// <summary>World-space bounds of the whole scene (the BVH root volume).</summary>
    public Aabb Bounds { get; }

    /// <summary>
    /// True if any material is transmissive or non-opaque (Mask/Blend), so
    /// shadow rays must attenuate through transparency instead of using the
    /// cheap any-hit test.
    /// </summary>
    public bool HasTransparentMaterials { get; }

    private RayScene(
        RayTriangle[] triangles, BvhNode[] nodes, int nodeCount, int[] order,
        bool hasTransparent)
    {
        _triangles = triangles;
        _nodes = nodes;
        _nodeCount = nodeCount;
        _order = order;
        Bounds = nodeCount > 0 ? nodes[0].Bounds : Aabb.Empty();
        HasTransparentMaterials = hasTransparent;
    }

    public static RayScene Build(Scene.Scene scene)
    {
        List<RayTriangle> triangles = new List<RayTriangle>();

        foreach (SceneObject sceneObject in scene.Objects)
        {
            Matrix4x4f world = sceneObject.Transform;
            RenderMesh mesh = sceneObject.GetRenderMesh();
            Material? material = sceneObject.Material;

            for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
            {
                RenderVertex v0 = mesh.Vertices[mesh.Indices[i + 0]];
                RenderVertex v1 = mesh.Vertices[mesh.Indices[i + 1]];
                RenderVertex v2 = mesh.Vertices[mesh.Indices[i + 2]];

                Vector3f p0 = TransformPoint(world, v0.Position);
                Vector3f p1 = TransformPoint(world, v1.Position);
                Vector3f p2 = TransformPoint(world, v2.Position);

                // Geometric face normal, used when a vertex normal is missing
                // or degenerate (same fallback rule as the rasterizer).
                Vector3f faceNormal =
                    Vector3f.Cross(p1 - p0, p2 - p0).Normalized();

                triangles.Add(new RayTriangle
                {
                    P0 = p0, P1 = p1, P2 = p2,
                    N0 = VertexNormal(world, v0.Normal, faceNormal),
                    N1 = VertexNormal(world, v1.Normal, faceNormal),
                    N2 = VertexNormal(world, v2.Normal, faceNormal),
                    T0 = TransformDirection(world, v0.Tangent),
                    T1 = TransformDirection(world, v1.Tangent),
                    T2 = TransformDirection(world, v2.Tangent),
                    H0 = v0.Handedness, H1 = v1.Handedness, H2 = v2.Handedness,
                    UV0 = v0.UV, UV1 = v1.UV, UV2 = v2.UV,
                    C0 = v0.Color, C1 = v1.Color, C2 = v2.Color,
                    Material = material
                });
            }
        }

        RayTriangle[] tris = triangles.ToArray();

        bool hasTransparent = false;
        foreach (RayTriangle t in tris)
        {
            Material? m = t.Material;
            if (m != null && (m.Transmission > 0.0f || m.AlphaMode != AlphaMode.Opaque))
            {
                hasTransparent = true;
                break;
            }
        }

        var (nodes, nodeCount, order) = BuildBvh(tris);
        return new RayScene(tris, nodes, nodeCount, order, hasTransparent);
    }

    //
    // BVH construction
    //

    private static (BvhNode[] nodes, int nodeCount, int[] order) BuildBvh(RayTriangle[] tris)
    {
        int n = tris.Length;

        int[] order = new int[n];
        Vector3f[] centroids = new Vector3f[n];
        Aabb[] bounds = new Aabb[n];

        for (int i = 0; i < n; i++)
        {
            order[i] = i;

            Aabb b = Aabb.Empty();
            b.Grow(tris[i].P0);
            b.Grow(tris[i].P1);
            b.Grow(tris[i].P2);
            bounds[i] = b;
            centroids[i] = b.Centroid;
        }

        // A binary tree over n leaves has at most 2n-1 nodes (1 when n == 0).
        BvhNode[] nodes = new BvhNode[Math.Max(1, 2 * n)];
        int nodeCount = 0;

        if (n > 0)
        {
            nodeCount = 1; // reserve the root at index 0
            Subdivide(0, 0, n, nodes, ref nodeCount, order, centroids, bounds);
        }

        return (nodes, nodeCount, order);
    }

    private static void Subdivide(
        int nodeIdx, int start, int count,
        BvhNode[] nodes, ref int nodeCount,
        int[] order, Vector3f[] centroids, Aabb[] bounds)
    {
        // Node bounds = union of the triangle bounds in this range.
        Aabb nodeBounds = Aabb.Empty();
        Aabb centroidBounds = Aabb.Empty();

        for (int k = start; k < start + count; k++)
        {
            nodeBounds.Grow(bounds[order[k]]);
            centroidBounds.Grow(centroids[order[k]]);
        }

        nodes[nodeIdx].Bounds = nodeBounds;

        if (count <= LeafSize)
        {
            nodes[nodeIdx].LeftFirst = start;
            nodes[nodeIdx].Count = count;
            return;
        }

        // Split on the largest centroid axis.
        Vector3f extent = centroidBounds.Extent;
        int axis = 0;
        if (extent.Y > extent.X) axis = 1;
        if (extent.Z > (axis == 0 ? extent.X : extent.Y)) axis = 2;

        float axisExtent = axis == 0 ? extent.X : (axis == 1 ? extent.Y : extent.Z);
        if (axisExtent < 1e-12f)
        {
            // All centroids coincide: make a leaf (can't split meaningfully).
            nodes[nodeIdx].LeftFirst = start;
            nodes[nodeIdx].Count = count;
            return;
        }

        float splitMin = Axis(centroidBounds.Min, axis);
        float splitPos = splitMin + axisExtent * 0.5f;

        // Partition order[start..start+count) by centroid[axis] < splitPos.
        int mid = Partition(order, centroids, start, count, axis, splitPos);

        // Degenerate split (everything on one side): fall back to a median split.
        if (mid == start || mid == start + count)
        {
            Array.Sort(order, start, count,
                Comparer<int>.Create((a, b) => Axis(centroids[a], axis).CompareTo(Axis(centroids[b], axis))));
            mid = start + count / 2;
        }

        int left = nodeCount++;
        int right = nodeCount++;

        nodes[nodeIdx].LeftFirst = left;
        nodes[nodeIdx].Count = 0; // internal

        Subdivide(left, start, mid - start, nodes, ref nodeCount, order, centroids, bounds);
        Subdivide(right, mid, start + count - mid, nodes, ref nodeCount, order, centroids, bounds);
    }

    /// <summary>In-place Hoare-style partition of the order slice; returns the split index.</summary>
    private static int Partition(
        int[] order, Vector3f[] centroids, int start, int count, int axis, float splitPos)
    {
        int i = start;
        int j = start + count - 1;

        while (i <= j)
        {
            if (Axis(centroids[order[i]], axis) < splitPos)
            {
                i++;
            }
            else
            {
                (order[i], order[j]) = (order[j], order[i]);
                j--;
            }
        }

        return i;
    }

    private static float Axis(Vector3f v, int axis)
        => axis == 0 ? v.X : (axis == 1 ? v.Y : v.Z);

    //
    // Queries
    //

    /// <summary>
    /// Nearest intersection in (Epsilon, tMax). Returns false if the ray misses
    /// every triangle. Two-sided (no backface culling) so primary rays hit the
    /// near surface of any mesh regardless of winding.
    /// </summary>
    public bool Intersect(in Ray ray, float tMax, out RayHit hit)
    {
        hit = default;

        if (_nodeCount == 0)
            return false;

        Vector3f invDir = new Vector3f(
            1.0f / ray.Direction.X,
            1.0f / ray.Direction.Y,
            1.0f / ray.Direction.Z);

        float closest = tMax;
        int bestTri = -1;
        float bestU = 0, bestV = 0;

        Span<int> stack = stackalloc int[MaxStack];
        int sp = 0;
        stack[sp++] = 0;

        while (sp > 0)
        {
            int nodeIdx = stack[--sp];
            ref readonly BvhNode node = ref _nodes[nodeIdx];

            if (!node.Bounds.Intersect(in ray, invDir, closest))
                continue;

            if (node.Count > 0)
            {
                // Leaf: test its triangles.
                for (int k = node.LeftFirst; k < node.LeftFirst + node.Count; k++)
                {
                    int triIdx = _order[k];
                    if (IntersectTriangle(in ray, in _triangles[triIdx], closest,
                            out float t, out float u, out float v))
                    {
                        closest = t;
                        bestTri = triIdx;
                        bestU = u;
                        bestV = v;
                    }
                }
            }
            else
            {
                // Internal: visit both children (guard against stack overflow on
                // pathological trees — capped depth is plenty for log2 splits).
                if (sp + 2 <= MaxStack)
                {
                    stack[sp++] = node.LeftFirst;
                    stack[sp++] = node.LeftFirst + 1;
                }
            }
        }

        if (bestTri < 0)
            return false;

        float w = 1.0f - bestU - bestV;
        ref readonly RayTriangle tri = ref _triangles[bestTri];

        hit.T = closest;
        hit.Position = ray.At(closest);
        hit.Normal = (tri.N0 * w + tri.N1 * bestU + tri.N2 * bestV).Normalized();
        hit.GeometricNormal =
            Vector3f.Cross(tri.P1 - tri.P0, tri.P2 - tri.P0).Normalized();
        hit.Tangent = tri.T0 * w + tri.T1 * bestU + tri.T2 * bestV;
        hit.Handedness = tri.H0 * w + tri.H1 * bestU + tri.H2 * bestV;
        hit.UV = tri.UV0 * w + tri.UV1 * bestU + tri.UV2 * bestV;
        hit.Color = new ColorRGBAf(
            tri.C0.R * w + tri.C1.R * bestU + tri.C2.R * bestV,
            tri.C0.G * w + tri.C1.G * bestU + tri.C2.G * bestV,
            tri.C0.B * w + tri.C1.B * bestU + tri.C2.B * bestV,
            tri.C0.A * w + tri.C1.A * bestU + tri.C2.A * bestV);
        hit.Material = tri.Material;
        hit.TriangleIndex = bestTri;

        return true;
    }

    /// <summary>
    /// Any-hit test for shadow rays (P4): true as soon as a triangle is hit
    /// within (Epsilon, tMax). Same BVH traversal, no nearest bookkeeping.
    /// </summary>
    public bool IntersectAny(in Ray ray, float tMax)
    {
        if (_nodeCount == 0)
            return false;

        Vector3f invDir = new Vector3f(
            1.0f / ray.Direction.X,
            1.0f / ray.Direction.Y,
            1.0f / ray.Direction.Z);

        Span<int> stack = stackalloc int[MaxStack];
        int sp = 0;
        stack[sp++] = 0;

        while (sp > 0)
        {
            int nodeIdx = stack[--sp];
            ref readonly BvhNode node = ref _nodes[nodeIdx];

            if (!node.Bounds.Intersect(in ray, invDir, tMax))
                continue;

            if (node.Count > 0)
            {
                for (int k = node.LeftFirst; k < node.LeftFirst + node.Count; k++)
                {
                    if (IntersectTriangle(in ray, in _triangles[_order[k]], tMax,
                            out _, out _, out _))
                        return true;
                }
            }
            else if (sp + 2 <= MaxStack)
            {
                stack[sp++] = node.LeftFirst;
                stack[sp++] = node.LeftFirst + 1;
            }
        }

        return false;
    }

    /// <summary>
    /// Möller–Trumbore. Returns the barycentrics (u, v) of the hit; the third
    /// weight is 1 - u - v.
    /// </summary>
    private static bool IntersectTriangle(
        in Ray ray, in RayTriangle tri, float tMax,
        out float t, out float u, out float v)
    {
        t = 0; u = 0; v = 0;

        Vector3f edge1 = tri.P1 - tri.P0;
        Vector3f edge2 = tri.P2 - tri.P0;

        Vector3f pvec = Vector3f.Cross(ray.Direction, edge2);
        float det = Vector3f.Dot(edge1, pvec);

        // Near-parallel ray (two-sided: |det| test, no winding cull).
        if (det > -Epsilon && det < Epsilon)
            return false;

        float invDet = 1.0f / det;

        Vector3f tvec = ray.Origin - tri.P0;
        u = Vector3f.Dot(tvec, pvec) * invDet;
        if (u < 0.0f || u > 1.0f)
            return false;

        Vector3f qvec = Vector3f.Cross(tvec, edge1);
        v = Vector3f.Dot(ray.Direction, qvec) * invDet;
        if (v < 0.0f || u + v > 1.0f)
            return false;

        t = Vector3f.Dot(edge2, qvec) * invDet;
        return t > Epsilon && t < tMax;
    }

    private static Vector3f TransformPoint(Matrix4x4f m, Vector3f p)
        => (m * Vector4f.FromVector3(p, 1.0f)).XYZ();

    private static Vector3f TransformDirection(Matrix4x4f m, Vector3f d)
        => new Vector3f(
            m.M11 * d.X + m.M12 * d.Y + m.M13 * d.Z,
            m.M21 * d.X + m.M22 * d.Y + m.M23 * d.Z,
            m.M31 * d.X + m.M32 * d.Y + m.M33 * d.Z).Normalized();

    private static Vector3f VertexNormal(Matrix4x4f m, Vector3f normal, Vector3f faceNormal)
    {
        if (normal.LengthSquared() < 1e-12f)
            return faceNormal;

        // Upper-left 3x3 (rotation + uniform scale), same as the rasterizer.
        return new Vector3f(
            m.M11 * normal.X + m.M12 * normal.Y + m.M13 * normal.Z,
            m.M21 * normal.X + m.M22 * normal.Y + m.M23 * normal.Z,
            m.M31 * normal.X + m.M32 * normal.Y + m.M33 * normal.Z).Normalized();
    }
}
