using global::rrr.Core;
using global::rrr.VMath;
using rrr.Core;
using rrr.Scene;
using rrr.VMath;

namespace rrr.Samples;

public static class SphereScene
{
    public static Scene.Scene Create(
        int segments)
    {
        Scene.Scene scene = new Scene.Scene();

        scene.Camera = Camera.CreateComfyCam(
            new Vector3f(-1, -1, -1),
            new Vector3f(+1, +1, +1));

        Mesh sphere =
            CreateSphereMesh(segments);

        SceneObject sphereObject =
            new SceneObject
            {
                Mesh = sphere,

                Material = new Material
                {
                    DiffuseColor =
                        ColorRGBAf.White
                },

                Transform =
                    Matrix4x4f.Identity
            };

        scene.Objects.Add(
            sphereObject);

        return scene;
    }

    private static Mesh CreateSphereMesh(
        int segments)
    {
        if (segments < 3)
        {
            throw new ArgumentOutOfRangeException(
                nameof(segments));
        }

        Mesh mesh =
            new Mesh();

        //
        // Positions
        //

        for (int lat = 0; lat <= segments; lat++)
        {
            float v =
                (float)lat / segments;

            float phi =
                v * MathF.PI;

            float y =
                MathF.Cos(phi);

            float ringRadius =
                MathF.Sin(phi);

            for (int lon = 0; lon <= segments; lon++)
            {
                float u =
                    (float)lon / segments;

                float theta =
                    u * MathF.PI * 2.0f;

                float x =
                    ringRadius *
                    MathF.Cos(theta);

                float z =
                    ringRadius *
                    MathF.Sin(theta);

                mesh.Positions.Add(
                    new Vector3f(
                        x,
                        y,
                        z));
            }
        }

        //
        // Triangles
        //

        int rowSize =
            segments + 1;

        for (int lat = 0; lat < segments; lat++)
        {
            for (int lon = 0; lon < segments; lon++)
            {
                int a =
                    lat * rowSize + lon;

                int b =
                    a + 1;

                int c =
                    a + rowSize;

                int d =
                    c + 1;

                mesh.Triangles.Add(
                    new Triangle(
                        a,
                        c,
                        b));

                mesh.Triangles.Add(
                    new Triangle(
                        b,
                        c,
                        d));
            }
        }

        return mesh;
    }
}