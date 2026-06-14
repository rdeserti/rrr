using rrr.VMath;

namespace rrr.Scene;

public static class MeshUtils
{

    public static void GenerateNormals(
        Mesh mesh)
    {
        mesh.Normals.Clear();

        for (int i = 0; i < mesh.Positions.Count; i++)
        {
            mesh.Normals.Add(
                Vector3f.Zero);
        }

        //
        // Accumulo delle face normals
        //
        for (int i = 0; i < mesh.Triangles.Count; i++)
        {
            Triangle triangle =
                mesh.Triangles[i];

            Vector3f p0 =
                mesh.Positions[triangle.P0];

            Vector3f p1 =
                mesh.Positions[triangle.P1];

            Vector3f p2 =
                mesh.Positions[triangle.P2];

            Vector3f edge1 =
                p1 - p0;

            Vector3f edge2 =
                p2 - p0;

            //
            // NON normalizzare qui.
            // La lunghezza contiene l'area
            // del triangolo.
            //
            Vector3f normal =
                Vector3f.Cross(
                    edge1,
                    edge2);

            mesh.Normals[triangle.P0] += normal;
            mesh.Normals[triangle.P1] += normal;
            mesh.Normals[triangle.P2] += normal;
        }

        //
        // Solo adesso normalizziamo
        // il risultato finale.
        //
        for (int i = 0; i < mesh.Normals.Count; i++)
        {
            mesh.Normals[i] =
                mesh.Normals[i]
                    .Normalized();
        }

        //
        // Collega gli indici normali
        // agli stessi indici posizione.
        //
        for (int i = 0; i < mesh.Triangles.Count; i++)
        {
            Triangle triangle =
                mesh.Triangles[i];

            triangle.N0 = triangle.P0;
            triangle.N1 = triangle.P1;
            triangle.N2 = triangle.P2;

            mesh.Triangles[i] =
                triangle;
        }
    }

    public static Vector3f ComputeCenter(
    Mesh mesh)
    {
        var (min, max) =
            ComputeBoundingBox(mesh);

        return (min + max) * 0.5f;
    }

    public static (
    Vector3f Min,
    Vector3f Max)
    ComputeBoundingBox(
        Mesh mesh)
    {
        if (mesh.Positions.Count == 0)
        {
            return (
                Vector3f.Zero,
                Vector3f.Zero);
        }

        Vector3f min =
            mesh.Positions[0];

        Vector3f max =
            mesh.Positions[0];

        foreach (Vector3f p in mesh.Positions)
        {
            min = Vector3f.Min(min, p);
            max = Vector3f.Max(max, p);
        }

        return (min, max);
    }

    public static void Transform(
    Mesh mesh,
    Matrix4x4f transform)
    {
        for (int i = 0; i < mesh.Positions.Count; i++)
        {
            Vector4f p =
                Vector4f.FromVector3(
                    mesh.Positions[i],
                    1);

            mesh.Positions[i] =
                (transform * p).XYZ();
        }

        for (int i = 0; i < mesh.Normals.Count; i++)
        {
            Vector4f n =
                Vector4f.FromVector3(
                    mesh.Normals[i],
                    0);

            mesh.Normals[i] =
                (transform * n)
                    .XYZ()
                    .Normalized();
        }
    }

    public static void FlipFaces(
    Mesh mesh)
    {
        for (int i = 0; i < mesh.Triangles.Count; i++)
        {
            Triangle t =
                mesh.Triangles[i];

            (t.P1, t.P2) =
                (t.P2, t.P1);

            (t.N1, t.N2) =
                (t.N2, t.N1);

            (t.UV1, t.UV2) =
                (t.UV2, t.UV1);

            mesh.Triangles[i] = t;
        }
    }

    public static void FlipNormals(
    Mesh mesh)
    {
        for (int i = 0; i < mesh.Normals.Count; i++)
        {
            mesh.Normals[i] =
                -mesh.Normals[i];
        }
    }
}