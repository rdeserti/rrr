using rrr.VMath;
using System;

namespace rrr.Scene;

/// <summary>
/// Procedural mesh generators for simple primitives. All meshes are centered
/// on the origin, carry per-vertex normals and UVs, and have their triangles
/// oriented outward (CCW seen from outside) so backface culling keeps them.
/// </summary>
public static class Primitives
{
    private sealed class Builder
    {
        public readonly Mesh Mesh = new Mesh();

        public int Vertex(Vector3f position, Vector3f normal, Vector2f uv)
        {
            Mesh.Positions.Add(position);
            Mesh.Normals.Add(normal);
            Mesh.UVs.Add(uv);
            return Mesh.Positions.Count - 1;
        }

        public void Tri(int a, int b, int c)
        {
            Mesh.Triangles.Add(new Triangle(a, a, a, b, b, b, c, c, c));
        }

        /// <summary>
        /// Adds a triangle, flipping the winding if its geometric normal does
        /// not point away from the origin (valid for origin-centered solids).
        /// </summary>
        public void TriOutward(int a, int b, int c)
        {
            Vector3f pa = Mesh.Positions[a];
            Vector3f pb = Mesh.Positions[b];
            Vector3f pc = Mesh.Positions[c];

            Vector3f faceNormal = Vector3f.Cross(pb - pa, pc - pa);
            Vector3f centroid = (pa + pb + pc) / 3.0f;

            if (Vector3f.Dot(faceNormal, centroid) < 0.0f)
                (b, c) = (c, b);

            Tri(a, b, c);
        }
    }

    public static Mesh Plane(float sizeX, float sizeZ)
    {
        Builder b = new Builder();

        float hx = sizeX * 0.5f;
        float hz = sizeZ * 0.5f;

        Vector3f up = new Vector3f(0, 1, 0);

        int v0 = b.Vertex(new Vector3f(-hx, 0, -hz), up, new Vector2f(0, 0));
        int v1 = b.Vertex(new Vector3f(hx, 0, -hz), up, new Vector2f(1, 0));
        int v2 = b.Vertex(new Vector3f(hx, 0, hz), up, new Vector2f(1, 1));
        int v3 = b.Vertex(new Vector3f(-hx, 0, hz), up, new Vector2f(0, 1));

        // Wound CCW seen from +Y (above).
        b.Tri(v0, v2, v1);
        b.Tri(v0, v3, v2);

        return b.Mesh;
    }

    public static Mesh Cube(float sizeX, float sizeY, float sizeZ)
    {
        Builder b = new Builder();

        float hx = sizeX * 0.5f;
        float hy = sizeY * 0.5f;
        float hz = sizeZ * 0.5f;

        // Each face: 4 corners + outward normal, UV 0..1.
        AddQuad(b,
            new Vector3f(-hx, -hy, hz), new Vector3f(hx, -hy, hz),
            new Vector3f(hx, hy, hz), new Vector3f(-hx, hy, hz),
            new Vector3f(0, 0, 1));   // +Z

        AddQuad(b,
            new Vector3f(hx, -hy, -hz), new Vector3f(-hx, -hy, -hz),
            new Vector3f(-hx, hy, -hz), new Vector3f(hx, hy, -hz),
            new Vector3f(0, 0, -1));  // -Z

        AddQuad(b,
            new Vector3f(hx, -hy, hz), new Vector3f(hx, -hy, -hz),
            new Vector3f(hx, hy, -hz), new Vector3f(hx, hy, hz),
            new Vector3f(1, 0, 0));   // +X

        AddQuad(b,
            new Vector3f(-hx, -hy, -hz), new Vector3f(-hx, -hy, hz),
            new Vector3f(-hx, hy, hz), new Vector3f(-hx, hy, -hz),
            new Vector3f(-1, 0, 0));  // -X

        AddQuad(b,
            new Vector3f(-hx, hy, hz), new Vector3f(hx, hy, hz),
            new Vector3f(hx, hy, -hz), new Vector3f(-hx, hy, -hz),
            new Vector3f(0, 1, 0));   // +Y

        AddQuad(b,
            new Vector3f(-hx, -hy, -hz), new Vector3f(hx, -hy, -hz),
            new Vector3f(hx, -hy, hz), new Vector3f(-hx, -hy, hz),
            new Vector3f(0, -1, 0));  // -Y

        return b.Mesh;
    }

    public static Mesh Sphere(float radius, int segments, int rings)
    {
        segments = Math.Max(3, segments);
        rings = Math.Max(2, rings);

        Builder b = new Builder();

        // ring index i (0 = top pole, rings = bottom pole)
        for (int i = 0; i <= rings; i++)
        {
            float theta = MathF.PI * i / rings;
            float y = MathF.Cos(theta);
            float ringRadius = MathF.Sin(theta);

            for (int j = 0; j <= segments; j++)
            {
                float phi = 2.0f * MathF.PI * j / segments;

                Vector3f normal = new Vector3f(
                    ringRadius * MathF.Cos(phi),
                    y,
                    ringRadius * MathF.Sin(phi));

                Vector2f uv = new Vector2f(
                    (float)j / segments,
                    (float)i / rings);

                b.Vertex(normal * radius, normal, uv);
            }
        }

        int stride = segments + 1;

        for (int i = 0; i < rings; i++)
        {
            for (int j = 0; j < segments; j++)
            {
                int a = i * stride + j;
                int bb = a + 1;
                int c = a + stride;
                int d = c + 1;

                b.TriOutward(a, c, bb);
                b.TriOutward(bb, c, d);
            }
        }

        return b.Mesh;
    }

    public static Mesh Cylinder(float radius, float height, int segments)
    {
        segments = Math.Max(3, segments);

        Builder b = new Builder();

        float hy = height * 0.5f;

        // Side
        for (int j = 0; j < segments; j++)
        {
            float a0 = 2.0f * MathF.PI * j / segments;
            float a1 = 2.0f * MathF.PI * (j + 1) / segments;

            Vector3f n0 = new Vector3f(MathF.Cos(a0), 0, MathF.Sin(a0));
            Vector3f n1 = new Vector3f(MathF.Cos(a1), 0, MathF.Sin(a1));

            float u0 = (float)j / segments;
            float u1 = (float)(j + 1) / segments;

            int t0 = b.Vertex(new Vector3f(n0.X * radius, hy, n0.Z * radius), n0, new Vector2f(u0, 0));
            int t1 = b.Vertex(new Vector3f(n1.X * radius, hy, n1.Z * radius), n1, new Vector2f(u1, 0));
            int b0 = b.Vertex(new Vector3f(n0.X * radius, -hy, n0.Z * radius), n0, new Vector2f(u0, 1));
            int b1 = b.Vertex(new Vector3f(n1.X * radius, -hy, n1.Z * radius), n1, new Vector2f(u1, 1));

            b.TriOutward(t0, b0, t1);
            b.TriOutward(t1, b0, b1);
        }

        AddCap(b, radius, hy, segments, new Vector3f(0, 1, 0));    // top
        AddCap(b, radius, -hy, segments, new Vector3f(0, -1, 0));  // bottom

        return b.Mesh;
    }

    public static Mesh Cone(float radius, float height, int segments)
    {
        segments = Math.Max(3, segments);

        Builder b = new Builder();

        float hy = height * 0.5f;
        Vector3f apex = new Vector3f(0, hy, 0);

        for (int j = 0; j < segments; j++)
        {
            float a0 = 2.0f * MathF.PI * j / segments;
            float a1 = 2.0f * MathF.PI * (j + 1) / segments;

            Vector3f p0 = new Vector3f(MathF.Cos(a0) * radius, -hy, MathF.Sin(a0) * radius);
            Vector3f p1 = new Vector3f(MathF.Cos(a1) * radius, -hy, MathF.Sin(a1) * radius);

            // Flat face normal for this side facet.
            Vector3f faceNormal =
                Vector3f.Cross(p0 - apex, p1 - apex).Normalized();

            int ia = b.Vertex(apex, faceNormal, new Vector2f(((float)j + 0.5f) / segments, 0));
            int i0 = b.Vertex(p0, faceNormal, new Vector2f((float)j / segments, 1));
            int i1 = b.Vertex(p1, faceNormal, new Vector2f((float)(j + 1) / segments, 1));

            b.TriOutward(ia, i0, i1);
        }

        AddCap(b, radius, -hy, segments, new Vector3f(0, -1, 0));  // base

        return b.Mesh;
    }

    public static Mesh Pyramid(float baseX, float baseZ, float height)
    {
        Builder b = new Builder();

        float hx = baseX * 0.5f;
        float hz = baseZ * 0.5f;
        float hy = height * 0.5f;

        Vector3f apex = new Vector3f(0, hy, 0);

        Vector3f c0 = new Vector3f(-hx, -hy, -hz);
        Vector3f c1 = new Vector3f(hx, -hy, -hz);
        Vector3f c2 = new Vector3f(hx, -hy, hz);
        Vector3f c3 = new Vector3f(-hx, -hy, hz);

        AddTriFace(b, apex, c0, c1);
        AddTriFace(b, apex, c1, c2);
        AddTriFace(b, apex, c2, c3);
        AddTriFace(b, apex, c3, c0);

        // Base (-Y)
        Vector3f down = new Vector3f(0, -1, 0);
        int b0 = b.Vertex(c0, down, new Vector2f(0, 0));
        int b1 = b.Vertex(c1, down, new Vector2f(1, 0));
        int b2 = b.Vertex(c2, down, new Vector2f(1, 1));
        int b3 = b.Vertex(c3, down, new Vector2f(0, 1));

        b.TriOutward(b0, b1, b2);
        b.TriOutward(b0, b2, b3);

        return b.Mesh;
    }

    //
    // Helpers
    //

    private static void AddQuad(
        Builder b,
        Vector3f p0, Vector3f p1, Vector3f p2, Vector3f p3,
        Vector3f normal)
    {
        int i0 = b.Vertex(p0, normal, new Vector2f(0, 0));
        int i1 = b.Vertex(p1, normal, new Vector2f(1, 0));
        int i2 = b.Vertex(p2, normal, new Vector2f(1, 1));
        int i3 = b.Vertex(p3, normal, new Vector2f(0, 1));

        b.TriOutward(i0, i1, i2);
        b.TriOutward(i0, i2, i3);
    }

    private static void AddTriFace(
        Builder b, Vector3f a, Vector3f p0, Vector3f p1)
    {
        Vector3f normal = Vector3f.Cross(p0 - a, p1 - a).Normalized();

        int ia = b.Vertex(a, normal, new Vector2f(0.5f, 0));
        int i0 = b.Vertex(p0, normal, new Vector2f(0, 1));
        int i1 = b.Vertex(p1, normal, new Vector2f(1, 1));

        b.TriOutward(ia, i0, i1);
    }

    private static void AddCap(
        Builder b, float radius, float y, int segments, Vector3f normal)
    {
        int center = b.Vertex(new Vector3f(0, y, 0), normal, new Vector2f(0.5f, 0.5f));

        for (int j = 0; j < segments; j++)
        {
            float a0 = 2.0f * MathF.PI * j / segments;
            float a1 = 2.0f * MathF.PI * (j + 1) / segments;

            int i0 = b.Vertex(
                new Vector3f(MathF.Cos(a0) * radius, y, MathF.Sin(a0) * radius),
                normal,
                new Vector2f(0.5f + 0.5f * MathF.Cos(a0), 0.5f + 0.5f * MathF.Sin(a0)));

            int i1 = b.Vertex(
                new Vector3f(MathF.Cos(a1) * radius, y, MathF.Sin(a1) * radius),
                normal,
                new Vector2f(0.5f + 0.5f * MathF.Cos(a1), 0.5f + 0.5f * MathF.Sin(a1)));

            b.TriOutward(center, i0, i1);
        }
    }
}
