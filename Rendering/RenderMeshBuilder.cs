using rrr.Scene;
using rrr.VMath;
using System;

namespace rrr.Rendering;

public static class RenderMeshBuilder
{
    public static RenderMesh Build(
        Mesh mesh)
    {
        RenderMesh renderMesh =
            new RenderMesh();

        Dictionary<VertexKey, int> cache =
            new();

        foreach (Triangle triangle in mesh.Triangles)
        {
            int i0 = GetVertexIndex(
                renderMesh,
                cache,
                mesh,
                triangle.P0,
                triangle.N0,
                triangle.UV0);

            int i1 = GetVertexIndex(
                renderMesh,
                cache,
                mesh,
                triangle.P1,
                triangle.N1,
                triangle.UV1);

            int i2 = GetVertexIndex(
                renderMesh,
                cache,
                mesh,
                triangle.P2,
                triangle.N2,
                triangle.UV2);

            renderMesh.Indices.Add(i0);
            renderMesh.Indices.Add(i1);
            renderMesh.Indices.Add(i2);
        }

        ComputeTangents(renderMesh);

        return renderMesh;
    }

    /// <summary>
    /// Generates a per-vertex tangent frame from positions and UVs (Lengyel's
    /// method): accumulate per-triangle tangent/bitangent, then orthonormalize
    /// the tangent against the normal and derive the handedness sign. Needed
    /// for tangent-space normal/bump mapping.
    /// </summary>
    private static void ComputeTangents(RenderMesh mesh)
    {
        int count = mesh.Vertices.Count;

        Vector3f[] tan = new Vector3f[count];
        Vector3f[] bitan = new Vector3f[count];

        var indices = mesh.Indices;

        for (int i = 0; i + 2 < indices.Count; i += 3)
        {
            int i0 = indices[i];
            int i1 = indices[i + 1];
            int i2 = indices[i + 2];

            RenderVertex v0 = mesh.Vertices[i0];
            RenderVertex v1 = mesh.Vertices[i1];
            RenderVertex v2 = mesh.Vertices[i2];

            Vector3f e1 = v1.Position - v0.Position;
            Vector3f e2 = v2.Position - v0.Position;

            Vector2f duv1 = v1.UV - v0.UV;
            Vector2f duv2 = v2.UV - v0.UV;

            float denom = duv1.X * duv2.Y - duv2.X * duv1.Y;

            if (MathF.Abs(denom) < 1e-12f)
                continue; // degenerate UVs

            float f = 1.0f / denom;

            Vector3f t = (e1 * duv2.Y - e2 * duv1.Y) * f;
            Vector3f b = (e2 * duv1.X - e1 * duv2.X) * f;

            tan[i0] += t; tan[i1] += t; tan[i2] += t;
            bitan[i0] += b; bitan[i1] += b; bitan[i2] += b;
        }

        for (int i = 0; i < count; i++)
        {
            RenderVertex v = mesh.Vertices[i];

            Vector3f n = v.Normal;
            Vector3f t = tan[i];

            // Gram-Schmidt orthonormalization against the normal.
            Vector3f tangent = t - n * Vector3f.Dot(n, t);

            tangent = tangent.LengthSquared() > 1e-12f
                ? tangent.Normalized()
                : AnyPerpendicular(n);

            float handedness =
                Vector3f.Dot(Vector3f.Cross(n, t), bitan[i]) < 0.0f ? -1.0f : 1.0f;

            v.Tangent = tangent;
            v.Handedness = handedness;

            mesh.Vertices[i] = v;
        }
    }

    private static Vector3f AnyPerpendicular(Vector3f n)
    {
        Vector3f axis =
            MathF.Abs(n.X) < 0.9f ? Vector3f.UnitX : Vector3f.UnitY;

        Vector3f p = Vector3f.Cross(n, axis);

        return p.LengthSquared() > 1e-12f ? p.Normalized() : Vector3f.UnitX;
    }

    private static int GetVertexIndex(
        RenderMesh renderMesh,
        Dictionary<VertexKey, int> cache,
        Mesh mesh,
        int positionIndex,
        int normalIndex,
        int uvIndex)
    {
        VertexKey key =
            new VertexKey(
                positionIndex,
                normalIndex,
                uvIndex);

        if (cache.TryGetValue(
            key,
            out int existingIndex))
        {
            return existingIndex;
        }

        RenderVertex vertex =
            new RenderVertex
            {
                Position =
                    mesh.Positions[positionIndex],

                Normal =
                    normalIndex >= 0
                        ? mesh.Normals[normalIndex]
                        : Vector3f.Zero,

                UV =
                    uvIndex >= 0
                        ? mesh.UVs[uvIndex]
                        : Vector2f.Zero
            };

        int index =
            renderMesh.Vertices.Count;

        renderMesh.Vertices.Add(vertex);

        cache.Add(
            key,
            index);

        return index;
    }

    private readonly struct VertexKey :
        IEquatable<VertexKey>
    {
        public readonly int Position;
        public readonly int Normal;
        public readonly int UV;

        public VertexKey(
            int position,
            int normal,
            int uv)
        {
            Position = position;
            Normal = normal;
            UV = uv;
        }

        public bool Equals(
            VertexKey other)
        {
            return
                Position == other.Position &&
                Normal == other.Normal &&
                UV == other.UV;
        }

        public override bool Equals(
            object? obj)
        {
            return obj is VertexKey other &&
                   Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                Position,
                Normal,
                UV);
        }
    }
}