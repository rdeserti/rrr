using rrr.Core;
using rrr.Scene;
using rrr.VMath;
using rrr.App;

namespace rrr.Rendering;

/// <summary>
/// Renders a scene's depth from a light's point of view into a
/// <see cref="ShadowMap"/>. Directional lights use an orthographic frustum
/// covering the scene; point lights use a perspective frustum aimed at the
/// scene center (a single map, not an omnidirectional cube map).
/// </summary>
public static class ShadowMapRenderer
{
    public static ShadowMap Render(
        Scene.Scene scene,
        Matrix4x4f lightViewProjection,
        int size)
    {

        Log.Debug("ShadowMap");
        // Reuse the rasterizer: it writes interpolated NDC z into the depth
        // buffer, which is exactly what a shadow map stores. The color buffer
        // is discarded.
        FrameBuffer color = new FrameBuffer(size, size, PixelFormat.RGBA32);
        DepthBuffer depth = new DepthBuffer(size, size);

        foreach (SceneObject obj in scene.Objects)
        {
            Matrix4x4f mvp = lightViewProjection * obj.Transform;

            RenderMesh mesh = obj.GetRenderMesh();
            var indices = mesh.Indices;
            var vertices = mesh.Vertices;

            for (int i = 0; i + 2 < indices.Count; i += 3)
            {
                if (Project(mvp, vertices[indices[i]].Position, size, out int x0, out int y0, out float z0) &&
                    Project(mvp, vertices[indices[i + 1]].Position, size, out int x1, out int y1, out float z1) &&
                    Project(mvp, vertices[indices[i + 2]].Position, size, out int x2, out int y2, out float z2))
                {
                    // No backface culling: both sides occlude.
                    Rasterizer.FillTriangle(
                        color, depth,
                        x0, y0, z0,
                        x1, y1, z1,
                        x2, y2, z2,
                        0u,
                        0, size - 1);
                }
            }
        }

        return new ShadowMap(depth.Depth, size, lightViewProjection);
    }

    private static bool Project(
        Matrix4x4f mvp, Vector3f position, int size,
        out int x, out int y, out float z)
    {
        x = 0; y = 0; z = 0;

        Vector4f p = mvp * Vector4f.FromVector3(position, 1.0f);

        if (p.W <= 0.0f)
            return false;

        Vector3f ndc = p.PerspectiveDivide();

        x = (int)((ndc.X * 0.5f + 0.5f) * size);
        y = (int)((1.0f - (ndc.Y * 0.5f + 0.5f)) * size);
        z = ndc.Z;

        return true;
    }

    /// <summary>
    /// Builds the view-projection matrix for a light, framed on the scene
    /// bounding box.
    /// </summary>
    public static Matrix4x4f BuildLightMatrix(Light light, Vector3f min, Vector3f max)
    {
        Vector3f center = (min + max) * 0.5f;

        float radius = (max - min).Length() * 0.5f;
        if (radius < 1e-4f)
            radius = 1.0f;

        if (light is DirectionalLight directional)
        {
            Vector3f dir = directional.Direction.Normalized();
            if (dir.LengthSquared() < 1e-8f)
                dir = new Vector3f(0, -1, 0);

            Vector3f eye = center - dir * (radius * 2.0f);
            Vector3f up = MathF.Abs(dir.Y) > 0.99f ? Vector3f.UnitZ : Vector3f.UnitY;

            Matrix4x4f view = Matrix4x4f.CreateLookAt(eye, center, up);

            float extent = radius * 2.2f;
            Matrix4x4f proj = Matrix4x4f.CreateOrthographic(
                extent, extent, 0.05f, radius * 4.0f + 0.1f);

            return proj * view;
        }

        if (light is PointLight point)
        {
            Vector3f eye = point.Position;

            Vector3f toCenter = center - eye;
            float dist = toCenter.Length();
            if (dist < 1e-4f)
                dist = 1.0f;

            Vector3f viewDir = toCenter / dist;
            Vector3f up = MathF.Abs(viewDir.Y) > 0.99f ? Vector3f.UnitZ : Vector3f.UnitY;

            Matrix4x4f view = Matrix4x4f.CreateLookAt(eye, center, up);

            // FOV wide enough to cover the bounding sphere, with margin.
            float fov = 2.0f * MathF.Atan2(radius, dist) * 1.4f;
            fov = MathF.Min(2.8f, MathF.Max(0.1f, fov));

            // Tight depth range around the scene. Keep far/near bounded so the
            // perspective depth buffer retains precision (otherwise distant
            // occluders and receivers collapse to the same z and cast no
            // shadow). near falls back to a fraction of far if the light is
            // inside/near the bounding sphere.
            float far = dist + radius * 1.5f;
            float near = dist - radius;

            float minNear = far / 500.0f;
            if (near < minNear)
                near = minNear;

            Matrix4x4f proj = Matrix4x4f.CreatePerspective(fov, 1.0f, near, far);

            return proj * view;
        }

        return Matrix4x4f.Identity;
    }
}
