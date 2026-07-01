using System;
using rrr.VMath;

namespace rrr.Rendering.Raytracing;

/// <summary>
/// Generates primary rays for a <see cref="Scene.Camera"/>, matching the
/// rasterizer's left-handed camera exactly:
///   - basis identical to <c>Matrix4x4f.CreateLookAt</c> (z = forward toward
///     the target, x = cross(up, z), y = cross(z, x));
///   - vertical FOV with <c>xScale = yScale / aspect</c>, as in
///     <c>CreatePerspective</c>;
///   - top-left pixel origin (y flipped), as in the raster viewport transform.
/// A pixel's ray therefore points at the same world direction the rasterizer
/// would project that pixel onto, so silhouettes line up.
/// </summary>
public sealed class RayCamera
{
    private readonly Vector3f _origin;
    private readonly Vector3f _forward;
    private readonly Vector3f _right;
    private readonly Vector3f _up;
    private readonly float _tanHalfFov;
    private readonly float _aspect;
    private readonly int _width;
    private readonly int _height;

    private RayCamera(
        Vector3f origin, Vector3f forward, Vector3f right, Vector3f up,
        float tanHalfFov, float aspect, int width, int height)
    {
        _origin = origin;
        _forward = forward;
        _right = right;
        _up = up;
        _tanHalfFov = tanHalfFov;
        _aspect = aspect;
        _width = width;
        _height = height;
    }

    public static RayCamera Create(Scene.Camera camera, int width, int height)
    {
        Vector3f forward = (camera.Target - camera.Position).Normalized();
        Vector3f right = Vector3f.Cross(camera.Up, forward).Normalized();
        Vector3f up = Vector3f.Cross(forward, right);

        float fovRad = camera.Fov * MathF.PI / 180.0f;
        float tanHalfFov = MathF.Tan(fovRad * 0.5f);
        float aspect = (float)width / height;

        return new RayCamera(
            camera.Position, forward, right, up,
            tanHalfFov, aspect, width, height);
    }

    /// <summary>
    /// Ray through the center of pixel (px, py). Pass fractional sub-pixel
    /// offsets in [0,1) via <paramref name="jitterX"/>/<paramref name="jitterY"/>
    /// for supersampling (default 0.5 = pixel center).
    /// </summary>
    public Ray GetRay(int px, int py, float jitterX = 0.5f, float jitterY = 0.5f)
    {
        // Pixel center -> NDC in [-1, 1], y flipped (top-left origin).
        float ndcX = 2.0f * (px + jitterX) / _width - 1.0f;
        float ndcY = 1.0f - 2.0f * (py + jitterY) / _height;

        Vector3f direction =
            (_forward
             + _right * (ndcX * _aspect * _tanHalfFov)
             + _up * (ndcY * _tanHalfFov)).Normalized();

        return new Ray(_origin, direction);
    }
}
