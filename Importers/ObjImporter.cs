using System.Globalization;
using rrr.Core;
using rrr.Scene;
using rrr.VMath;

namespace rrr.Importers;

public sealed class ObjImporter
{
    private class ObjGroup
    {
        public string Name = "";

        public string? MaterialName;

        public List<Triangle> Triangles =
            new();
    }

    public Scene.Scene Load(
        string fileName)
    {
        Scene.Scene scene = new();

        List<Vector3f> positions = new();
        List<Vector3f> normals = new();
        List<Vector2f> uvs = new();

        List<ObjGroup> groups = new();

        List<string> materialLibraries = new();

        ObjGroup currentGroup =
            new ObjGroup
            {
                Name =
                    Path.GetFileNameWithoutExtension(
                        fileName)
            };

        groups.Add(currentGroup);

        foreach (string rawLine in File.ReadLines(fileName))
        {
            string line = rawLine.Trim();

            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (line.StartsWith("#"))
                continue;

            string[] parts =
                line.Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries);

            switch (parts[0])
            {
                case "v":
                    positions.Add(
                        new Vector3f(
                            ParseFloat(parts[1]),
                            ParseFloat(parts[2]),
                            ParseFloat(parts[3])));
                    break;

                case "vn":
                    normals.Add(
                        new Vector3f(
                            ParseFloat(parts[1]),
                            ParseFloat(parts[2]),
                            ParseFloat(parts[3])));
                    break;

                case "vt":
                    // OBJ texture coordinates use a bottom-left origin
                    // (v = 0 at the bottom). Our textures use a top-left
                    // origin (v = 0 at the top, matching frame-buffer rows),
                    // so flip v here to keep standard assets upright.
                    uvs.Add(
                        new Vector2f(
                            ParseFloat(parts[1]),
                            1.0f - ParseFloat(parts[2])));
                    break;

                case "o":
                case "g":
                    {
                        string name =
                            parts.Length > 1
                                ? string.Join(
                                    " ",
                                    parts.Skip(1))
                                : "Object";

                        currentGroup =
                            new ObjGroup
                            {
                                Name = name
                            };

                        groups.Add(currentGroup);

                        break;
                    }

                case "usemtl":
                    {
                        if (parts.Length > 1)
                        {
                            currentGroup.MaterialName =
                                parts[1];
                        }

                        break;
                    }

                case "mtllib":
                    {
                        // Each token is a material library file name,
                        // resolved later relative to the .obj directory.
                        for (int i = 1; i < parts.Length; i++)
                            materialLibraries.Add(parts[i]);

                        break;
                    }

                case "f":
                    {
                        List<(int p, int uv, int n)> face =
                            new();

                        for (int i = 1; i < parts.Length; i++)
                        {
                            face.Add(
                                ParseFaceVertex(
                                    parts[i],
                                    positions.Count,
                                    uvs.Count,
                                    normals.Count));
                        }

                        //
                        // Fan triangulation
                        //
                        for (int i = 1; i < face.Count - 1; i++)
                        {
                            var v0 = face[0];
                            var v1 = face[i];
                            var v2 = face[i + 1];

                            currentGroup.Triangles.Add(
                                new Triangle(
                                    v0.p, v0.uv, v0.n,
                                    v1.p, v1.uv, v1.n,
                                    v2.p, v2.uv, v2.n));
                        }

                        break;
                    }
            }
        }

        Dictionary<string, Material> loadedMaterials =
            LoadMaterials(fileName, materialLibraries);

        Random random = new Random(12345);

        foreach (ObjGroup group in groups)
        {
            if (group.Triangles.Count == 0)
                continue;

            Material material;

            if (group.MaterialName != null &&
                loadedMaterials.TryGetValue(
                    group.MaterialName, out Material? found))
            {
                material = found;
            }
            else
            {
                // No .mtl entry: keep the previous behaviour of a stable
                // random pastel so the object is still visible.
                material =
                    new Material
                    {
                        Name =
                            group.MaterialName ??
                            $"Material_{group.Name}",

                        SourceMaterialName =
                            group.MaterialName,

                        DiffuseColor =
                            RandomPastel(random)
                    };
            }

            if (!scene.Materials.Contains(material))
                scene.Materials.Add(material);

            Mesh mesh = new Mesh();

            //
            // Copia globale.
            // Gli indici OBJ rimangono validi.
            //
            mesh.Positions.AddRange(positions);
            mesh.Normals.AddRange(normals);
            mesh.UVs.AddRange(uvs);

            mesh.Triangles.AddRange(
                group.Triangles);

            scene.Objects.Add(
                new SceneObject
                {
                    Name = group.Name,
                    Mesh = mesh,
                    Material = material
                });
        }

        return scene;
    }

    /// <summary>
    /// Loads and merges all material libraries referenced by the .obj,
    /// resolving their paths relative to the .obj directory. If no mtllib
    /// is declared, falls back to a same-named .mtl next to the .obj.
    /// </summary>
    private static Dictionary<string, Material> LoadMaterials(
        string objFileName,
        List<string> materialLibraries)
    {
        string objDirectory =
            Path.GetDirectoryName(Path.GetFullPath(objFileName)) ?? "";

        List<string> libraries = materialLibraries;

        if (libraries.Count == 0)
        {
            // Fallback: <objname>.mtl beside the .obj.
            libraries = new List<string>
            {
                Path.GetFileNameWithoutExtension(objFileName) + ".mtl"
            };
        }

        MtlImporter mtlImporter = new MtlImporter();

        Dictionary<string, Material> materials =
            new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

        foreach (string library in libraries)
        {
            string path =
                Path.IsPathRooted(library)
                    ? library
                    : Path.Combine(objDirectory, library);

            foreach (var entry in mtlImporter.Load(path))
            {
                // First definition wins on name collisions across files.
                materials.TryAdd(entry.Key, entry.Value);
            }
        }

        return materials;
    }

    private static (
        int p,
        int uv,
        int n)
        ParseFaceVertex(
            string token,
            int positionCount,
            int uvCount,
            int normalCount)
    {
        string[] fields =
            token.Split('/');

        int p =
            ResolveIndex(
                fields[0],
                positionCount);

        int uv = -1;
        int n = -1;

        if (fields.Length > 1 &&
            !string.IsNullOrWhiteSpace(
                fields[1]))
        {
            uv =
                ResolveIndex(
                    fields[1],
                    uvCount);
        }

        if (fields.Length > 2 &&
            !string.IsNullOrWhiteSpace(
                fields[2]))
        {
            n =
                ResolveIndex(
                    fields[2],
                    normalCount);
        }

        return (p, uv, n);
    }

    private static int ResolveIndex(
        string value,
        int count)
    {
        int index =
            int.Parse(
                value,
                CultureInfo.InvariantCulture);

        if (index > 0)
            return index - 1;

        return count + index;
    }

    private static float ParseFloat(
        string value)
    {
        return float.Parse(
            value,
            CultureInfo.InvariantCulture);
    }

    private static ColorRGBAf RandomPastel(
        Random random)
    {
        return new ColorRGBAf(
            0.4f + random.NextSingle() * 0.6f,
            0.4f + random.NextSingle() * 0.6f,
            0.4f + random.NextSingle() * 0.6f);
    }
}