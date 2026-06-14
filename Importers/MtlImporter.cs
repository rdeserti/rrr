using System.Globalization;
using rrr.App;
using rrr.Core;
using rrr.Scene;

namespace rrr.Importers;

/// <summary>
/// Loads Wavefront .mtl material libraries into <see cref="Material"/>
/// instances, keyed by material name. Unknown directives are ignored, and
/// textures that fail to load degrade to a solid color (via
/// <see cref="Texture2D.TryLoad"/>) instead of throwing.
/// </summary>
public sealed class MtlImporter
{
    public Dictionary<string, Material> Load(string fileName)
    {
        Dictionary<string, Material> materials =
            new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);

        if (!File.Exists(fileName))
            return materials;

        string baseDirectory =
            Path.GetDirectoryName(Path.GetFullPath(fileName)) ?? "";

        Log.Debug("Material loading from " + fileName);
        Material? current = null;

        foreach (string rawLine in File.ReadLines(fileName))
        {
            string line = rawLine.Trim();

            if (line.Length == 0 || line.StartsWith("#"))
                continue;

            string[] parts =
                line.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries);

            switch (parts[0].ToLowerInvariant())
            {
                case "newmtl":
                    {
                        string name =
                            parts.Length > 1 ? parts[1] : "";

                        current = new Material
                        {
                            Name = name,
                            SourceMaterialName = name
                        };

                        materials[name] = current;
                        break;
                    }

                case "kd":
                    if (current != null && parts.Length >= 4)
                    {
                        current.DiffuseColor = new ColorRGBAf(
                            ParseFloat(parts[1]),
                            ParseFloat(parts[2]),
                            ParseFloat(parts[3]),
                            current.DiffuseColor.A);
                    }
                    break;

                case "ks":
                    if (current != null && parts.Length >= 4)
                    {
                        current.SpecularColor = new ColorRGBAf(
                            ParseFloat(parts[1]),
                            ParseFloat(parts[2]),
                            ParseFloat(parts[3]));
                    }
                    break;

                case "ns":
                    if (current != null && parts.Length >= 2)
                        current.Shininess = ParseFloat(parts[1]);
                    break;

                case "ke":
                    if (current != null && parts.Length >= 4)
                    {
                        current.EmissiveColor = new ColorRGBAf(
                            ParseFloat(parts[1]),
                            ParseFloat(parts[2]),
                            ParseFloat(parts[3]));
                    }
                    break;

                case "d":
                    // Dissolve: 1.0 = opaque, 0.0 = transparent.
                    if (current != null && parts.Length >= 2)
                        current.DiffuseColor =
                            WithAlpha(current.DiffuseColor, ParseFloat(parts[1]));
                    break;

                case "tr":
                    // Transparency: inverse of dissolve.
                    if (current != null && parts.Length >= 2)
                        current.DiffuseColor =
                            WithAlpha(current.DiffuseColor, 1.0f - ParseFloat(parts[1]));
                    break;

                case "map_kd":
                    if (current != null && parts.Length >= 2)
                    {
                        current.DiffuseTexture =
                            Texture2D.TryLoad(ResolveTexturePath(baseDirectory, parts));
                    }
                    break;

                case "map_ks":
                    if (current != null && parts.Length >= 2)
                    {
                        current.SpecularTexture =
                            Texture2D.TryLoad(ResolveTexturePath(baseDirectory, parts));
                    }
                    break;

                case "map_ke":
                    if (current != null && parts.Length >= 2)
                    {
                        current.EmissiveTexture =
                            Texture2D.TryLoad(ResolveTexturePath(baseDirectory, parts));

                        // A map with no Ke would be multiplied by black;
                        // default the emissive color to white in that case.
                        if (current.EmissiveTexture != null &&
                            current.EmissiveColor.R == 0 &&
                            current.EmissiveColor.G == 0 &&
                            current.EmissiveColor.B == 0)
                        {
                            current.EmissiveColor = ColorRGBAf.White;
                        }
                    }
                    break;

                // Bump directives are ambiguous: a colored image is a
                // tangent-space normal map, a grayscale one is a height map.
                // Auto-detect so real-world assets (often grayscale "bump")
                // work without re-authoring.
                case "norm":
                case "map_bump":
                case "bump":
                    if (current != null && parts.Length >= 2)
                    {
                        current.NormalTexture =
                            Texture2D.TryLoad(ResolveTexturePath(baseDirectory, parts));
                        current.NormalIsHeightMap =
                            current.NormalTexture?.IsLikelyGrayscale() ?? false;
                    }
                    break;

                // Grayscale height/displacement map.
                case "map_height":
                case "disp":
                    if (current != null && parts.Length >= 2)
                    {
                        current.NormalTexture =
                            Texture2D.TryLoad(ResolveTexturePath(baseDirectory, parts));
                        current.NormalIsHeightMap = true;
                    }
                    break;
            }
        }

        return materials;
    }

    /// <summary>
    /// The texture file name is the last token (this skips any leading
    /// map options such as "-o 1 1 0"). Backslashes are normalized and the
    /// path is resolved relative to the .mtl directory when not absolute.
    /// </summary>
    private static string ResolveTexturePath(string baseDirectory, string[] parts)
    {
        string fileName =
            parts[parts.Length - 1].Replace('\\', '/');

        if (Path.IsPathRooted(fileName))
            return fileName;

        return Path.Combine(baseDirectory, fileName);
    }

    private static ColorRGBAf WithAlpha(ColorRGBAf color, float alpha)
    {
        return new ColorRGBAf(color.R, color.G, color.B, alpha);
    }

    private static float ParseFloat(string value)
    {
        return float.Parse(value, CultureInfo.InvariantCulture);
    }
}
