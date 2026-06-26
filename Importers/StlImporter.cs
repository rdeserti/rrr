using System.Globalization;
using System.Text;
using rrr.Core;
using rrr.Scene;
using rrr.VMath;

namespace rrr.Importers;

/// <summary>
/// Loads a stereolithography (.stl) model — both binary and ASCII variants,
/// auto-detected. STL stores only triangles (no UVs, no materials, no shared
/// indices), so the result is a single <see cref="SceneObject"/> with a plain
/// grey material.
///
/// Coincident vertices are welded (STL repeats identical vertex bytes for
/// shared corners, so exact matching merges them). By default normals are left
/// unset (-1) and the renderer falls back to the per-triangle geometric normal,
/// reproducing STL's faceted look; pass <c>smoothNormals</c> to derive smooth
/// per-vertex normals instead.
///
/// Coordinates are imported verbatim. CAD STLs are commonly Z-up, so a model
/// may need a <c>rotate world axis=x angle=-90</c> to stand upright here.
/// </summary>
public sealed class StlImporter
{
    public Scene.Scene Load(string fileName)
        => Load(fileName, smoothNormals: false);

    public Scene.Scene Load(string fileName, bool smoothNormals)
    {
        byte[] bytes = File.ReadAllBytes(fileName);

        Mesh mesh =
            IsBinary(bytes)
                ? ParseBinary(bytes)
                : ParseAscii(bytes);

        if (smoothNormals && mesh.Positions.Count > 0)
            MeshUtils.GenerateNormals(mesh);

        Scene.Scene scene = new();

        Material material = new Material
        {
            Name = Path.GetFileNameWithoutExtension(fileName),
            DiffuseColor = new ColorRGBAf(0.75f, 0.75f, 0.78f)
        };

        scene.Materials.Add(material);

        scene.Objects.Add(
            new SceneObject
            {
                Name = Path.GetFileNameWithoutExtension(fileName),
                Mesh = mesh,
                Material = material
            });

        return scene;
    }

    /// <summary>
    /// Decides binary vs ASCII. A binary STL is exactly
    /// <c>84 + 50 * triangleCount</c> bytes; that size check is the only
    /// reliable test (the leading bytes can read as "solid" in either form).
    /// Falls back to scanning for the ASCII "facet" keyword.
    /// </summary>
    private static bool IsBinary(byte[] bytes)
    {
        if (bytes.Length < 84)
            return false;

        uint triangleCount = BitConverter.ToUInt32(bytes, 80);

        long expected = 84L + 50L * triangleCount;

        if (expected == bytes.Length)
            return true;

        // Not an exact binary size: treat as ASCII only if it actually looks
        // like one, otherwise trust the (possibly padded) binary layout.
        int probe = Math.Min(bytes.Length, 512);
        string head = Encoding.ASCII.GetString(bytes, 0, probe);

        return !head.Contains("facet", StringComparison.OrdinalIgnoreCase);
    }

    private static Mesh ParseBinary(byte[] bytes)
    {
        Mesh mesh = new Mesh();
        VertexWelder welder = new VertexWelder(mesh);

        uint triangleCount = BitConverter.ToUInt32(bytes, 80);

        int offset = 84;

        for (uint t = 0; t < triangleCount; t++)
        {
            if (offset + 50 > bytes.Length)
                break;

            // Skip the per-facet normal (offset..offset+12); we recompute as
            // needed. Read the three vertices.
            int vOffset = offset + 12;

            int a = welder.Add(ReadVector(bytes, vOffset));
            int b = welder.Add(ReadVector(bytes, vOffset + 12));
            int c = welder.Add(ReadVector(bytes, vOffset + 24));

            AddTriangle(mesh, a, b, c);

            offset += 50; // 12 normal + 36 verts + 2 attribute bytes
        }

        return mesh;
    }

    private static Mesh ParseAscii(byte[] bytes)
    {
        Mesh mesh = new Mesh();
        VertexWelder welder = new VertexWelder(mesh);

        string text = Encoding.ASCII.GetString(bytes);

        // Buffer the up-to-three vertices of the current facet.
        Span<int> facet = stackalloc int[3];
        int facetCount = 0;

        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine.Trim();

            if (line.Length == 0)
                continue;

            string[] parts =
                line.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
                continue;

            if (parts[0].Equals("vertex", StringComparison.OrdinalIgnoreCase) &&
                parts.Length >= 4)
            {
                Vector3f p = new Vector3f(
                    ParseFloat(parts[1]),
                    ParseFloat(parts[2]),
                    ParseFloat(parts[3]));

                if (facetCount < 3)
                    facet[facetCount++] = welder.Add(p);
            }
            else if (parts[0].Equals("endloop", StringComparison.OrdinalIgnoreCase))
            {
                if (facetCount == 3)
                    AddTriangle(mesh, facet[0], facet[1], facet[2]);

                facetCount = 0;
            }
        }

        return mesh;
    }

    private static void AddTriangle(Mesh mesh, int a, int b, int c)
    {
        // Position indices only; UV (-1) and normal (-1) left unset so the
        // rasterizer uses the geometric face normal unless smoothing is asked.
        mesh.Triangles.Add(
            new Triangle(
                a, -1, -1,
                b, -1, -1,
                c, -1, -1));
    }

    private static Vector3f ReadVector(byte[] bytes, int offset)
    {
        return new Vector3f(
            BitConverter.ToSingle(bytes, offset),
            BitConverter.ToSingle(bytes, offset + 4),
            BitConverter.ToSingle(bytes, offset + 8));
    }

    private static float ParseFloat(string value)
    {
        return float.Parse(value, CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// Merges coincident vertices by exact coordinate match, so shared corners
    /// (which STL stores as byte-identical floats) collapse to one index.
    /// </summary>
    private sealed class VertexWelder
    {
        private readonly Mesh _mesh;
        private readonly Dictionary<(float, float, float), int> _map = new();

        public VertexWelder(Mesh mesh) => _mesh = mesh;

        public int Add(Vector3f p)
        {
            var key = (p.X, p.Y, p.Z);

            if (_map.TryGetValue(key, out int index))
                return index;

            index = _mesh.Positions.Count;
            _mesh.Positions.Add(p);
            _map[key] = index;
            return index;
        }
    }
}
