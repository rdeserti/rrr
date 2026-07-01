using System;
using System.Threading.Tasks;
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

        // Gamma correction (on by default, two independent switches): input
        // linearization decodes color inputs (textures + flat colors + the
        // background here) sRGB->linear so the framebuffer holds LINEAR color
        // during rendering; output reconstruction encodes the final image
        // linear->sRGB once, after the supersampling downsample (so the box
        // filter averages light in linear space). Either direction off = that
        // half is the identity.
        ColorSpace.SrgbToLinearEnabled = settings.LinearizeInput;
        ColorSpace.LinearToSrgbEnabled = settings.EncodeSrgb;

        Lighting.DebugShadowVisualize = settings.DebugShadow;

        // Supersampling: render into a buffer scaled up by the factor, then box-
        // downsample to the requested size. Engine-agnostic (both the rasterizer
        // and the ray tracer just fill the oversized buffer). 1 = disabled.
        int supersampling = Math.Clamp(settings.Supersampling, 1, 4);

        int renderWidth = width * supersampling;
        int renderHeight = height * supersampling;

        FrameBuffer frameBuffer =
            new FrameBuffer(renderWidth, renderHeight, PixelFormat.RGBA32);

        frameBuffer.Clear(
            PixelPacker.Pack(ColorRGBA32.FromRGBAf(ColorSpace.SrgbToLinear(background))));

        DepthBuffer depthBuffer = new DepthBuffer(renderWidth, renderHeight);

        profiler.Mark("Create buffers");

        RenderPipeline.Render(frameBuffer, depthBuffer, scene, settings);

        profiler.Mark("Render");

        FrameBuffer outputBuffer = Supersampler.Downsample(frameBuffer, supersampling);

        if (supersampling > 1)
            profiler.Mark($"Downsample {supersampling}x");

        // Final encode: linear -> sRGB for the whole image, once.
        if (settings.EncodeSrgb)
        {
            EncodeToSrgb(outputBuffer);
            profiler.Mark("Gamma encode");
        }

        ImageWriter.Save(outputBuffer, outputFile);

        profiler.Mark("Save image");

        Console.WriteLine($"Output: {outputFile}");

        SceneStats.From(scene).Dump();
        profiler.Dump();
    }

    /// <summary>
    /// Encodes a linear framebuffer to sRGB in place (final output step). Runs
    /// only when gamma correction is on. Note: the framebuffer is 8-bit, so the
    /// linear intermediate can band slightly in deep shadows — a float target
    /// would remove that, left as future work.
    /// </summary>
    private static void EncodeToSrgb(FrameBuffer fb)
    {
        uint[] pixels = fb.Pixels;

        Parallel.For(0, fb.Height, y =>
        {
            int row = y * fb.Stride;

            for (int x = 0; x < fb.Width; x++)
            {
                int i = row + x;
                ColorRGBAf linear = ColorRGBAf.FromRGBA32(PixelPacker.Unpack(pixels[i]));
                ColorRGBAf srgb = ColorSpace.LinearToSrgb(linear);
                pixels[i] = PixelPacker.Pack(ColorRGBA32.FromRGBAf(srgb));
            }
        });
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
