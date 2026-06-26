using rrr.App;
using rrr.Scripting;
using System.Reflection;

static string GetVersion()
{
    var version = Assembly.GetExecutingAssembly()
                          .GetName()
                          .Version;
    return version?.ToString() ?? "N/A";
}

Console.WriteLine("RRR 3D Raster & Raytracer Renderer");
Console.WriteLine("Version: " + GetVersion());

if (args.Length == 0)
{
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  rrr.exe model.obj|.stl|.gltf|.glb [output.bmp]");
    Console.WriteLine("  rrr.exe scene.rrr");
    return;
}

string inputFile = args[0];

if (!File.Exists(inputFile))
{
    Console.WriteLine($"Input file not found: {inputFile}");
    return;
}

try
{
    string extension =
        Path.GetExtension(inputFile).ToLowerInvariant();

    if (extension == ".rrr")
    {
        SceneScript.Run(inputFile);
    }
    else
    {
        string outputFile =
            args.Length >= 2
                ? args[1]
                : Path.ChangeExtension(inputFile, ".bmp");

        SceneRenderer.RenderModelFile(inputFile, outputFile);
    }
}
catch (Exception e)
{
    Console.WriteLine($"ERROR: {e.Message}");
}
