namespace rrr.Importers;

/// <summary>
/// Single entry point for loading a 3D model into a <see cref="Scene.Scene"/>,
/// dispatching to the right importer by file extension. Used by both the CLI
/// quick path and the script <c>load</c> command.
/// </summary>
public static class ModelImporter
{
    public static Scene.Scene Load(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();

        switch (extension)
        {
            case ".obj":
                return new ObjImporter().Load(path);

            case ".stl":
                return new StlImporter().Load(path);

            case ".gltf":
            case ".glb":
                return new GltfImporter().Load(path);

            default:
                throw new NotSupportedException(
                    $"Unsupported model format '{extension}'. " +
                    "Supported: .obj, .stl, .gltf, .glb");
        }
    }
}
