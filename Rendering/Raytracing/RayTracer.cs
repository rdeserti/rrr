using rrr.Core;

namespace rrr.Rendering.Raytracing;

/// <summary>
/// Ray-tracing rendering backend. Consumes the same <see cref="Scene.Scene"/>
/// (camera, lights, materials, textures) as the rasterizer and writes the same
/// <see cref="FrameBuffer"/>, so it slots into the existing render funnel
/// (profiler, stats and image output stay shared).
///
/// P0 (current): plumbing only. The framebuffer arrives already cleared to the
/// background color by <c>SceneRenderer.Render</c>, so the stub leaves it as-is
/// and returns — proving the dispatch path works without touching the raster.
/// Subsequent phases (primary rays, BVH, lighting/material parity, shadow rays,
/// reflections) are tracked in <c>docs/RAYTRACER_ROADMAP.md</c>.
/// </summary>
public static class RayTracer
{
    public static void Render(
        FrameBuffer framebuffer,
        Scene.Scene scene,
        RenderSettings settings)
    {
        rrr.App.Log.Debug(
            $"Raytrace engine selected (P0 stub): {framebuffer.Width}x{framebuffer.Height}, " +
            $"{scene.Objects.Count} objects, {scene.Lights.Count} lights, " +
            $"maxBounces={settings.MaxBounces}, rayShadows={settings.RayShadows}.");

        // P0: the framebuffer is already background-filled; nothing to draw yet.
    }
}
