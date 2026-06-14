using rrr.VMath;

namespace rrr.Scene;

public class Camera
{
    public Vector3f Position { get; set; }

    public Vector3f Target { get; set; } =
        Vector3f.Forward;

    public Vector3f Up { get; set; } =
        Vector3f.Up;

    public float Fov { get; set; } = 60.0f;
    public float Near { get; set; } = 0.1f;
    public float Far { get; set; } = 1000.0f;

    public Matrix4x4f GetViewMatrix()
    {
        return Matrix4x4f.CreateLookAt(
            Position,
            Target,
            Up);
    }

    public Matrix4x4f GetProjectionMatrix(
        float aspectRatio)
    {
        return Matrix4x4f.CreatePerspectiveDegrees(
            Fov,
            aspectRatio,
            Near,
            Far);
    }

    public static Camera CreateComfyCam(
        Vector3f min,
        Vector3f max)
    {
        Vector3f center =
            (min + max) * 0.5f;

        Vector3f size =
            max - min;

        float radius =
            size.Length() * 0.5f;

        const float fov = 60.0f;

        float fovRad =
            fov * MathF.PI / 180.0f;


        float halfExtent =
        MathF.Max(size.X, size.Y) * 0.5f;

        float distance =
            halfExtent /
            MathF.Tan(fovRad * 0.5f);

        distance *= 2f; // margine

        return new Camera
        {
            Position = new Vector3f(
                center.X,
                center.Y,
                center.Z - distance),

            Target = center,

            Up = Vector3f.Up,

            Fov = fov,
            Near = MathF.Max(
                0.01f,
                distance * 0.01f),

            Far = distance + radius * 4.0f
        };
    }
}
