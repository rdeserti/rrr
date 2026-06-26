using rrr.Core;
using rrr.Imaging;
using rrr.Importers;
using rrr.Rendering;
using rrr.Scene;
using rrr.VMath;

namespace rrr.App;

/// <summary>
/// Single entry point for turning a scene into an image file. Both the .obj
/// quick path and the .rrr script funnel through here, so profiling and scene
/// statistics are reported consistently.
/// </summary>
public static class SceneRenderer
{
    public static void Render(
        Scene.Scene scene,
        RenderSettings settings,
        string outputFile,
        int width,
        int height,
        ColorRGBAf background,
        double loadMilliseconds = 0.0)
    {
        Profiler profiler = new Profiler();
        profiler.Start(loadMilliseconds, loadMilliseconds > 0.0 ? "Load model" : null);

        FrameBuffer frameBuffer =
            new FrameBuffer(width, height, PixelFormat.RGBA32);

        frameBuffer.Clear(
            PixelPacker.Pack(ColorRGBA32.FromRGBAf(background)));

        DepthBuffer depthBuffer = new DepthBuffer(width, height);

        profiler.Mark("Create buffers");

        RenderPipeline.Render(frameBuffer, depthBuffer, scene, settings);

        profiler.Mark("Render");

        ImageWriter.Save(frameBuffer, outputFile);

        profiler.Mark("Save image");

        Console.WriteLine($"Output: {outputFile}");

        SceneStats.From(scene).Dump();
        profiler.Dump();
    }

    /// <summary>
    /// Loads an OBJ, frames it with a comfy camera and (if it has no lights)
    /// a comfy light, then renders it with flat shading.
    /// </summary>
    public static void RenderObjFile(string objPath, string outputFile)
        => RenderModelFile(objPath, outputFile);

    /// <summary>
    /// Loads any supported model (OBJ/STL/glTF/GLB) via
    /// <see cref="ModelImporter"/>, frames it with a comfy camera and (if it
    /// has no lights) a comfy light, then renders it with flat shading.
    /// </summary>
    public static void RenderModelFile(string modelPath, string outputFile)
    {
        System.Diagnostics.Stopwatch sw =
            System.Diagnostics.Stopwatch.StartNew();

        Scene.Scene scene = ModelImporter.Load(modelPath);

        sw.Stop();
        double loadMs = sw.Elapsed.TotalMilliseconds;

        var (min, max) = scene.GetBoundingBox();

        // Respect a camera/lights imported from the model (e.g. glTF); only
        // fall back to an auto-framed comfy camera / light when none exist.
        if (scene.Camera == null)
            scene.Camera = Camera.CreateComfyCam(min, max);

        if (scene.Lights.Count == 0)
            scene.Lights.Add(PointLight.CreateComfyLight(min, max));

        Render(
            scene,
            RenderSettings.Flat(),
            outputFile,
            1920,
            1080,
            ColorRGBAf.Black,
            loadMs);
    }
}
