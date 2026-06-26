using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using rrr.Core;
using rrr.Importers.Gltf;
using rrr.Scene;
using rrr.VMath;

namespace rrr.Importers;

/// <summary>
/// Loads a glTF 2.0 model — both <c>.gltf</c> (JSON + external/embedded buffers)
/// and <c>.glb</c> (binary container). Decodes accessors/bufferViews into
/// positions, normals and the first UV set, walks the node hierarchy baking each
/// node's world transform into its vertices, and maps PBR metallic-roughness
/// materials onto the renderer's Blinn-Phong model.
///
/// glTF is right-handed (Y-up, +Z toward the viewer); the renderer is
/// left-handed, so Z is negated and triangle winding flipped on import. UVs use
/// a top-left origin in glTF — the same as our textures — so they are NOT
/// flipped (unlike OBJ).
///
/// Textures load from external files, data-URIs and bufferView-embedded images
/// (PNG, BMP and baseline JPEG); progressive JPEG and unsupported formats
/// degrade to the base color. Material features mapped: baseColor/emissive/
/// normal/occlusion textures, alphaMode (MASK alpha-test) + alphaCutoff,
/// doubleSided, KHR_materials_unlit and KHR_materials_emissive_strength.
///
/// Vertex attributes: POSITION/NORMAL/TEXCOORD_0/indices, plus COLOR_0 (vertex
/// colors) and TANGENT; sparse accessors are decoded.
///
/// Limitations: only triangle primitives; metallic-roughness texture, TEXCOORD_1
/// (one UV set sampled), alpha BLEND (treated opaque), animations, skins, morph
/// targets are ignored.
/// </summary>
public sealed class GltfImporter
{
    private GltfDocument _doc = new();
    private string _baseDirectory = "";
    private byte[]?[] _buffers = Array.Empty<byte[]?>();
    private Material[] _materials = Array.Empty<Material>();
    private Texture2D?[] _imageTextures = Array.Empty<Texture2D?>(); // decoded once, by image index

    public Scene.Scene Load(string fileName)
    {
        rrr.App.Log.Debug($"Loading glTF '{Path.GetFileName(fileName)}'...");

        _baseDirectory =
            Path.GetDirectoryName(Path.GetFullPath(fileName)) ?? "";

        bool isGlb =
            Path.GetExtension(fileName).Equals(".glb", StringComparison.OrdinalIgnoreCase);

        byte[]? glbBinaryChunk = null;
        string json;

        if (isGlb)
            json = ReadGlb(File.ReadAllBytes(fileName), out glbBinaryChunk);
        else
            json = File.ReadAllText(fileName);

        _doc = JsonSerializer.Deserialize<GltfDocument>(json)
               ?? throw new InvalidDataException("Empty or invalid glTF JSON.");

        ResolveBuffers(glbBinaryChunk);

        DecodeImages();

        Scene.Scene scene = new();

        _materials = BuildMaterials(scene);

        foreach (int rootNode in RootNodes())
            WalkNode(rootNode, Mat4.Identity, scene);

        return scene;
    }

    //
    // GLB container
    //

    /// <summary>
    /// Parses a GLB: 12-byte header (magic "glTF", version, total length), then
    /// length-prefixed chunks. Returns the JSON chunk text and the binary chunk
    /// (if present) via <paramref name="binaryChunk"/>.
    /// </summary>
    private static string ReadGlb(byte[] bytes, out byte[]? binaryChunk)
    {
        binaryChunk = null;

        if (bytes.Length < 12 || BitConverter.ToUInt32(bytes, 0) != 0x46546C67u)
            throw new InvalidDataException("Not a GLB file (bad magic).");

        string json = "";
        int offset = 12;

        while (offset + 8 <= bytes.Length)
        {
            uint chunkLength = BitConverter.ToUInt32(bytes, offset);
            uint chunkType = BitConverter.ToUInt32(bytes, offset + 4);
            int dataStart = offset + 8;

            if (dataStart + chunkLength > bytes.Length)
                break;

            switch (chunkType)
            {
                case 0x4E4F534Au: // "JSON"
                    json = Encoding.UTF8.GetString(bytes, dataStart, (int)chunkLength);
                    break;

                case 0x004E4942u: // "BIN\0"
                    binaryChunk = new byte[chunkLength];
                    Array.Copy(bytes, dataStart, binaryChunk, 0, (int)chunkLength);
                    break;
            }

            // Chunks are 4-byte aligned.
            offset = dataStart + (int)chunkLength;
        }

        if (json.Length == 0)
            throw new InvalidDataException("GLB has no JSON chunk.");

        return json;
    }

    //
    // Buffers
    //

    private void ResolveBuffers(byte[]? glbBinaryChunk)
    {
        List<GltfBuffer> buffers = _doc.Buffers ?? new();
        _buffers = new byte[]?[buffers.Count];

        for (int i = 0; i < buffers.Count; i++)
        {
            string? uri = buffers[i].Uri;

            if (uri == null)
            {
                // GLB: the first (only) buffer with no URI is the BIN chunk.
                _buffers[i] = glbBinaryChunk;
            }
            else if (TryDecodeDataUri(uri, out byte[] data))
            {
                _buffers[i] = data;
            }
            else
            {
                string path = Path.Combine(_baseDirectory, Uri.UnescapeDataString(uri));
                _buffers[i] = File.Exists(path) ? File.ReadAllBytes(path) : null;
            }
        }
    }

    private static bool TryDecodeDataUri(string uri, out byte[] data)
    {
        data = Array.Empty<byte>();

        if (!uri.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return false;

        int comma = uri.IndexOf(',');
        if (comma < 0)
            return false;

        string meta = uri.Substring(5, comma - 5);
        string payload = uri.Substring(comma + 1);

        if (meta.Contains("base64", StringComparison.OrdinalIgnoreCase))
            data = Convert.FromBase64String(payload);
        else
            data = Encoding.ASCII.GetBytes(Uri.UnescapeDataString(payload));

        return true;
    }

    //
    // Scene graph
    //

    private IEnumerable<int> RootNodes()
    {
        if (_doc.Scenes != null && _doc.Scenes.Count > 0)
        {
            int index = _doc.Scene ?? 0;
            if (index >= 0 && index < _doc.Scenes.Count)
                return _doc.Scenes[index].Nodes ?? Enumerable.Empty<int>();
        }

        // No scenes declared: treat every node as a root.
        return _doc.Nodes != null
            ? Enumerable.Range(0, _doc.Nodes.Count)
            : Enumerable.Empty<int>();
    }

    private void WalkNode(int nodeIndex, Mat4 parentWorld, Scene.Scene scene)
    {
        if (_doc.Nodes == null || nodeIndex < 0 || nodeIndex >= _doc.Nodes.Count)
            return;

        GltfNode node = _doc.Nodes[nodeIndex];
        Mat4 world = parentWorld * LocalTransform(node);

        if (node.Mesh != null)
            EmitMesh(node.Mesh.Value, node.Name, world, scene);

        if (node.Camera != null)
            EmitCamera(node.Camera.Value, node.Name, world, scene);

        if (node.Extensions?.Light != null)
            EmitLight(node.Extensions.Light.Light, node.Name, world, scene);

        if (node.Children != null)
            foreach (int child in node.Children)
                WalkNode(child, world, scene);
    }

    //
    // Cameras & lights (positioned by the node's world transform).
    // glTF cameras/lights look down local -Z; +Y is up. Z is negated for our
    // left-handed space, consistent with the mesh vertices.
    //

    private void EmitCamera(int cameraIndex, string? nodeName, Mat4 world, Scene.Scene scene)
    {
        if (_doc.Cameras == null || cameraIndex < 0 || cameraIndex >= _doc.Cameras.Count)
            return;

        GltfCamera gltf = _doc.Cameras[cameraIndex];

        if (gltf.Type != "perspective" || gltf.Perspective == null)
            return; // orthographic cameras are not supported by the renderer

        Vector3f position = ToLeftHanded(world.TransformPoint(Vector3f.Zero));
        Vector3f forward = ToLeftHanded(world.TransformDirection(new Vector3f(0, 0, -1))).Normalized();
        Vector3f up = ToLeftHanded(world.TransformDirection(new Vector3f(0, 1, 0))).Normalized();

        GltfPerspective p = gltf.Perspective;

        Camera camera = new Camera
        {
            Name = nodeName ?? gltf.Name ?? $"gltf_camera{cameraIndex}",
            Position = position,
            Target = position + forward,
            Up = up,
            Fov = p.Yfov * 180.0f / MathF.PI,   // glTF yfov is vertical, in radians
            Near = p.Znear,
            Far = p.Zfar ?? 1000.0f
        };

        scene.Cameras.Add(camera);
        scene.Camera ??= camera; // first camera becomes the active one
    }

    private void EmitLight(int lightIndex, string? nodeName, Mat4 world, Scene.Scene scene)
    {
        List<GltfLight>? lights = _doc.Extensions?.Lights?.Lights;
        if (lights == null || lightIndex < 0 || lightIndex >= lights.Count)
            return;

        GltfLight gltf = lights[lightIndex];

        ColorRGBAf color = gltf.Color is { Length: >= 3 } c
            ? new ColorRGBAf(c[0], c[1], c[2])
            : ColorRGBAf.White;

        // glTF intensity is physical (candela for point/spot, lux for
        // directional). This renderer is non-physical, so clamp to a sane range;
        // the value can be overridden in a .rrr script if needed.
        float intensity = MathF.Min(gltf.Intensity ?? 1.0f, 10.0f);

        Vector3f position = ToLeftHanded(world.TransformPoint(Vector3f.Zero));
        Vector3f direction = ToLeftHanded(world.TransformDirection(new Vector3f(0, 0, -1))).Normalized();

        Light light;

        switch (gltf.Type)
        {
            case "directional":
                light = new DirectionalLight
                {
                    Direction = direction,
                    Color = color,
                    Intensity = intensity
                };
                break;

            case "spot":
                float outer = (gltf.Spot?.OuterConeAngle ?? (MathF.PI / 4)) * 180.0f / MathF.PI;
                float inner = (gltf.Spot?.InnerConeAngle ?? 0f) * 180.0f / MathF.PI;
                light = new SpotLight
                {
                    Position = position,
                    Direction = direction,
                    Color = color,
                    Intensity = intensity,
                    ConeAngleDegrees = outer,
                    InnerAngleDegrees = inner
                };
                break;

            case "point":
            default:
                light = new PointLight(position, color, intensity);
                break;
        }

        scene.Lights.Add(light);
    }

    private static Vector3f ToLeftHanded(Vector3f v) => new Vector3f(v.X, v.Y, -v.Z);

    private static Mat4 LocalTransform(GltfNode node)
    {
        if (node.Matrix is { Length: 16 })
            return Mat4.FromColumnMajor(node.Matrix);

        Vector3f t = node.Translation is { Length: 3 }
            ? new Vector3f(node.Translation[0], node.Translation[1], node.Translation[2])
            : Vector3f.Zero;

        (float x, float y, float z, float w) r = node.Rotation is { Length: 4 }
            ? (node.Rotation[0], node.Rotation[1], node.Rotation[2], node.Rotation[3])
            : (0, 0, 0, 1);

        Vector3f s = node.Scale is { Length: 3 }
            ? new Vector3f(node.Scale[0], node.Scale[1], node.Scale[2])
            : Vector3f.One;

        return Mat4.FromTRS(t, r, s);
    }

    private void EmitMesh(int meshIndex, string? nodeName, Mat4 world, Scene.Scene scene)
    {
        if (_doc.Meshes == null || meshIndex < 0 || meshIndex >= _doc.Meshes.Count)
            return;

        GltfMesh gltfMesh = _doc.Meshes[meshIndex];
        if (gltfMesh.Primitives == null)
            return;

        string baseName = nodeName ?? gltfMesh.Name ?? $"mesh{meshIndex}";

        for (int p = 0; p < gltfMesh.Primitives.Count; p++)
        {
            GltfPrimitive prim = gltfMesh.Primitives[p];

            // Only triangles (mode 4, or unspecified which defaults to 4).
            if (prim.Mode is not (null or 4))
                continue;

            if (prim.Attributes == null ||
                !prim.Attributes.TryGetValue("POSITION", out int positionAccessor))
                continue;

            Mesh mesh = BuildPrimitiveMesh(prim, positionAccessor, world);
            if (mesh.Positions.Count == 0)
                continue;

            Material material =
                prim.Material is int m && m >= 0 && m < _materials.Length
                    ? _materials[m]
                    : DefaultMaterial();

            if (!scene.Materials.Contains(material))
                scene.Materials.Add(material);

            string name =
                gltfMesh.Primitives.Count > 1 ? $"{baseName}_{p}" : baseName;

            scene.Objects.Add(
                new SceneObject { Name = name, Mesh = mesh, Material = material });
        }
    }

    private Mesh BuildPrimitiveMesh(GltfPrimitive prim, int positionAccessor, Mat4 world)
    {
        Mesh mesh = new Mesh();

        Vector3f[] positions = ReadVec3(positionAccessor);

        // Bake the node world transform, then convert RH (glTF) to our LH space
        // by negating Z. Winding is flipped below to compensate.
        for (int i = 0; i < positions.Length; i++)
        {
            Vector3f w = world.TransformPoint(positions[i]);
            mesh.Positions.Add(new Vector3f(w.X, w.Y, -w.Z));
        }

        bool hasNormals =
            prim.Attributes!.TryGetValue("NORMAL", out int normalAccessor);

        if (hasNormals)
        {
            Vector3f[] normals = ReadVec3(normalAccessor);
            for (int i = 0; i < normals.Length; i++)
            {
                Vector3f n = world.TransformDirection(normals[i]);
                mesh.Normals.Add(new Vector3f(n.X, n.Y, -n.Z).Normalized());
            }
        }

        bool hasUv =
            prim.Attributes.TryGetValue("TEXCOORD_0", out int uvAccessor);

        if (hasUv)
        {
            Vector2f[] uvs = ReadVec2(uvAccessor);
            // No v-flip: glTF UV origin is top-left, matching our frame buffer.
            mesh.UVs.AddRange(uvs);
        }

        // Per-vertex colors (COLOR_0): VEC3 or VEC4, float or normalized int.
        if (prim.Attributes.TryGetValue("COLOR_0", out int colorAccessor))
            mesh.Colors.AddRange(ReadColors(colorAccessor));

        // Supplied tangents (TANGENT): VEC4 (xyz + handedness w). Convert to our
        // left-handed space: negate Z, and negate the handedness (single-axis
        // mirror flips it). Used directly instead of recomputing from UVs.
        if (prim.Attributes.TryGetValue("TANGENT", out int tangentAccessor))
        {
            Vector4f[] tangents = ReadVec4(tangentAccessor);
            for (int i = 0; i < tangents.Length; i++)
            {
                Vector3f dir =
                    world.TransformDirection(new Vector3f(tangents[i].X, tangents[i].Y, tangents[i].Z));
                mesh.Tangents.Add(
                    new Vector4f(dir.X, dir.Y, -dir.Z, -tangents[i].W));
            }
        }

        int[] indices = prim.Indices is int ia
            ? ReadIndices(ia)
            : SequentialIndices(positions.Length);

        for (int i = 0; i + 2 < indices.Length; i += 3)
        {
            int a = indices[i];
            int b = indices[i + 1];
            int c = indices[i + 2];

            // Swap b,c: negating Z reversed the winding.
            mesh.Triangles.Add(
                new Triangle(
                    a, hasUv ? a : -1, hasNormals ? a : -1,
                    c, hasUv ? c : -1, hasNormals ? c : -1,
                    b, hasUv ? b : -1, hasNormals ? b : -1));
        }

        return mesh;
    }

    private static int[] SequentialIndices(int count)
    {
        int[] indices = new int[count];
        for (int i = 0; i < count; i++)
            indices[i] = i;
        return indices;
    }

    //
    // Accessor decoding
    //

    private const int CompByte = 5120;
    private const int CompUByte = 5121;
    private const int CompShort = 5122;
    private const int CompUShort = 5123;
    private const int CompUInt = 5125;
    private const int CompFloat = 5126;

    private Vector3f[] ReadVec3(int accessorIndex)
    {
        float[] flat = ReadFloats(accessorIndex, 3);
        Vector3f[] result = new Vector3f[flat.Length / 3];

        for (int i = 0; i < result.Length; i++)
            result[i] = new Vector3f(flat[i * 3], flat[i * 3 + 1], flat[i * 3 + 2]);

        return result;
    }

    private Vector2f[] ReadVec2(int accessorIndex)
    {
        float[] flat = ReadFloats(accessorIndex, 2);
        Vector2f[] result = new Vector2f[flat.Length / 2];

        for (int i = 0; i < result.Length; i++)
            result[i] = new Vector2f(flat[i * 2], flat[i * 2 + 1]);

        return result;
    }

    private Vector4f[] ReadVec4(int accessorIndex)
    {
        float[] flat = ReadFloats(accessorIndex, 4);
        Vector4f[] result = new Vector4f[flat.Length / 4];

        for (int i = 0; i < result.Length; i++)
            result[i] = new Vector4f(
                flat[i * 4], flat[i * 4 + 1], flat[i * 4 + 2], flat[i * 4 + 3]);

        return result;
    }

    /// <summary>Reads a COLOR_0 accessor (VEC3 → opaque, or VEC4).</summary>
    private ColorRGBAf[] ReadColors(int accessorIndex)
    {
        int comps = ComponentCount(_doc.Accessors![accessorIndex].Type); // 3 or 4
        float[] flat = ReadFloats(accessorIndex, comps);

        ColorRGBAf[] result = new ColorRGBAf[flat.Length / comps];

        for (int i = 0; i < result.Length; i++)
            result[i] = new ColorRGBAf(
                flat[i * comps],
                flat[i * comps + 1],
                flat[i * comps + 2],
                comps >= 4 ? flat[i * comps + 3] : 1.0f);

        return result;
    }

    /// <summary>
    /// Reads a float-valued accessor (positions/normals/UVs), honoring the
    /// component type, normalization and bufferView stride.
    /// </summary>
    private float[] ReadFloats(int accessorIndex, int components)
    {
        GltfAccessor accessor = _doc.Accessors![accessorIndex];

        int actual = ComponentCount(accessor.Type);
        if (actual != components)
            throw new InvalidDataException(
                $"Accessor type '{accessor.Type}' has {actual} components, expected {components}.");

        float[] result = new float[accessor.Count * components];

        // Dense base values (a sparse accessor may omit the bufferView, in which
        // case the base is all zeros and only the sparse overrides apply).
        if (accessor.BufferView != null)
        {
            GltfBufferView view = _doc.BufferViews![accessor.BufferView.Value];
            byte[] buffer = _buffers[view.Buffer]
                ?? throw new InvalidDataException($"Buffer {view.Buffer} could not be resolved.");

            int compSize = ComponentSize(accessor.ComponentType);
            int elementSize = compSize * components;
            int stride = view.ByteStride ?? elementSize;
            int start = view.ByteOffset + accessor.ByteOffset;

            for (int i = 0; i < accessor.Count; i++)
            {
                int elementOffset = start + i * stride;

                for (int c = 0; c < components; c++)
                {
                    int o = elementOffset + c * compSize;
                    result[i * components + c] =
                        ReadComponentAsFloat(buffer, o, accessor.ComponentType, accessor.Normalized);
                }
            }
        }

        // Sparse overrides: replace selected elements with their stored values.
        if (accessor.Sparse != null)
            ApplySparseFloats(accessor, components, result);

        return result;
    }

    /// <summary>
    /// Applies a sparse accessor's overrides onto an already-read dense buffer:
    /// reads the override indices and values and writes them into place.
    /// </summary>
    private void ApplySparseFloats(GltfAccessor accessor, int components, float[] result)
    {
        GltfSparse sparse = accessor.Sparse!;
        GltfSparseIndices si = sparse.Indices!;
        GltfSparseValues sv = sparse.Values!;

        GltfBufferView iView = _doc.BufferViews![si.BufferView];
        byte[] iBuf = _buffers[iView.Buffer]
            ?? throw new InvalidDataException("Sparse indices buffer unresolved.");
        int iStart = iView.ByteOffset + si.ByteOffset;
        int iCompSize = ComponentSize(si.ComponentType);

        GltfBufferView vView = _doc.BufferViews![sv.BufferView];
        byte[] vBuf = _buffers[vView.Buffer]
            ?? throw new InvalidDataException("Sparse values buffer unresolved.");
        int vStart = vView.ByteOffset + sv.ByteOffset;
        int vCompSize = ComponentSize(accessor.ComponentType);

        for (int k = 0; k < sparse.Count; k++)
        {
            int io = iStart + k * iCompSize;
            int target = si.ComponentType switch
            {
                CompUByte => iBuf[io],
                CompUShort => BitConverter.ToUInt16(iBuf, io),
                CompUInt => (int)BitConverter.ToUInt32(iBuf, io),
                _ => throw new InvalidDataException("Bad sparse index component type.")
            };

            for (int c = 0; c < components; c++)
            {
                int vo = vStart + (k * components + c) * vCompSize;
                result[target * components + c] =
                    ReadComponentAsFloat(vBuf, vo, accessor.ComponentType, accessor.Normalized);
            }
        }
    }

    private int[] ReadIndices(int accessorIndex)
    {
        GltfAccessor accessor = _doc.Accessors![accessorIndex];

        if (accessor.BufferView == null)
            return Array.Empty<int>();

        GltfBufferView view = _doc.BufferViews![accessor.BufferView.Value];
        byte[] buffer = _buffers[view.Buffer]
            ?? throw new InvalidDataException($"Buffer {view.Buffer} could not be resolved.");

        int compSize = ComponentSize(accessor.ComponentType);
        int stride = view.ByteStride ?? compSize;
        int start = view.ByteOffset + accessor.ByteOffset;

        int[] result = new int[accessor.Count];

        for (int i = 0; i < accessor.Count; i++)
        {
            int o = start + i * stride;
            result[i] = accessor.ComponentType switch
            {
                CompUByte => buffer[o],
                CompUShort => BitConverter.ToUInt16(buffer, o),
                CompUInt => (int)BitConverter.ToUInt32(buffer, o),
                _ => throw new InvalidDataException(
                    $"Unsupported index component type {accessor.ComponentType}.")
            };
        }

        return result;
    }

    private static float ReadComponentAsFloat(byte[] buffer, int offset, int componentType, bool normalized)
    {
        switch (componentType)
        {
            case CompFloat:
                return BitConverter.ToSingle(buffer, offset);

            case CompUByte:
                {
                    byte v = buffer[offset];
                    return normalized ? v / 255.0f : v;
                }

            case CompByte:
                {
                    sbyte v = unchecked((sbyte)buffer[offset]);
                    return normalized ? MathF.Max(v / 127.0f, -1.0f) : v;
                }

            case CompUShort:
                {
                    ushort v = BitConverter.ToUInt16(buffer, offset);
                    return normalized ? v / 65535.0f : v;
                }

            case CompShort:
                {
                    short v = BitConverter.ToInt16(buffer, offset);
                    return normalized ? MathF.Max(v / 32767.0f, -1.0f) : v;
                }

            case CompUInt:
                return BitConverter.ToUInt32(buffer, offset);

            default:
                throw new InvalidDataException($"Unsupported component type {componentType}.");
        }
    }

    private static int ComponentSize(int componentType) => componentType switch
    {
        CompByte or CompUByte => 1,
        CompShort or CompUShort => 2,
        CompUInt or CompFloat => 4,
        _ => throw new InvalidDataException($"Unknown component type {componentType}.")
    };

    private static int ComponentCount(string type) => type switch
    {
        "SCALAR" => 1,
        "VEC2" => 2,
        "VEC3" => 3,
        "VEC4" => 4,
        "MAT2" => 4,
        "MAT3" => 9,
        "MAT4" => 16,
        _ => throw new InvalidDataException($"Unknown accessor type '{type}'.")
    };

    //
    // Materials (PBR metallic-roughness -> Blinn-Phong)
    //

    private Material[] BuildMaterials(Scene.Scene scene)
    {
        List<GltfMaterial> mats = _doc.Materials ?? new();
        Material[] result = new Material[mats.Count];

        for (int i = 0; i < mats.Count; i++)
        {
            GltfMaterial src = mats[i];

            Material material = new Material
            {
                Name = src.Name ?? $"material{i}"
            };

            GltfPbr? pbr = src.Pbr;

            if (pbr?.BaseColorFactor is { Length: >= 3 } bc)
                material.DiffuseColor = new ColorRGBAf(
                    bc[0], bc[1], bc[2], bc.Length >= 4 ? bc[3] : 1.0f);

            // Roughness -> Blinn-Phong shininess; metals keep a bright spec.
            float roughness = pbr?.RoughnessFactor ?? 1.0f;
            material.SpecularColor = ColorRGBAf.White;
            material.Shininess = MathF.Max(0.0f, (1.0f - roughness)) * 128.0f;

            // Emissive factor, scaled by KHR_materials_emissive_strength.
            float emissiveStrength =
                src.Extensions?.EmissiveStrength?.EmissiveStrength ?? 1.0f;

            if (src.EmissiveFactor is { Length: >= 3 } e)
                material.EmissiveColor = new ColorRGBAf(
                    e[0] * emissiveStrength,
                    e[1] * emissiveStrength,
                    e[2] * emissiveStrength);

            material.DiffuseTexture = TryLoadTexture(pbr?.BaseColorTexture);
            material.EmissiveTexture = TryLoadTexture(src.EmissiveTexture);
            material.OcclusionTexture = TryLoadTexture(src.OcclusionTexture);

            Texture2D? normal = TryLoadTexture(src.NormalTexture);
            if (normal != null)
            {
                material.NormalTexture = normal;
                material.NormalIsHeightMap = false;
            }

            if (material.EmissiveTexture != null &&
                material.EmissiveColor.R == 0 &&
                material.EmissiveColor.G == 0 &&
                material.EmissiveColor.B == 0)
            {
                material.EmissiveColor = ColorRGBAf.White;
            }

            // Alpha mode / cutoff, double-sided, unlit.
            material.AlphaMode = (src.AlphaMode?.ToUpperInvariant()) switch
            {
                "MASK" => Scene.AlphaMode.Mask,
                "BLEND" => Scene.AlphaMode.Blend,
                _ => Scene.AlphaMode.Opaque
            };
            material.AlphaCutoff = src.AlphaCutoff ?? 0.5f;
            material.DoubleSided = src.DoubleSided;
            material.Unlit = src.Extensions?.Unlit != null;

            scene.Materials.Add(material);
            result[i] = material;
        }

        return result;
    }

    /// <summary>
    /// Decodes every unique image once, in parallel. The from-scratch PNG/JPEG
    /// decoders are CPU-bound and independent, so fanning out across cores (and
    /// never decoding the same image twice) is the bulk of the load-time win for
    /// texture-heavy models. Results are cached by image index.
    /// </summary>
    private void DecodeImages()
    {
        if (_doc.Images == null || _doc.Images.Count == 0)
            return;

        int count = _doc.Images.Count;
        _imageTextures = new Texture2D?[count];

        rrr.App.Log.Debug($"Decoding {count} texture image(s)...");

        Parallel.For(0, count, i =>
        {
            _imageTextures[i] = DecodeImage(i);
        });
    }

    private Texture2D? DecodeImage(int imageIndex)
    {
        GltfImage image = _doc.Images![imageIndex];

        // Image stored in a bufferView (typical for GLB): decode from bytes.
        if (image.BufferView != null)
        {
            byte[] bytes = GetBufferViewBytes(image.BufferView.Value);
            return Texture2D.FromBytes(bytes, $"image#{imageIndex}");
        }

        if (!string.IsNullOrEmpty(image.Uri))
        {
            // data-URI image: decode the embedded (base64) bytes.
            if (TryDecodeDataUri(image.Uri, out byte[] data))
                return Texture2D.FromBytes(data, $"image#{imageIndex}");

            // External file (PNG/BMP/JPEG).
            string path = Path.Combine(_baseDirectory, Uri.UnescapeDataString(image.Uri));
            return Texture2D.TryLoad(path);
        }

        return null;
    }

    private Texture2D? TryLoadTexture(GltfTextureRef? reference)
    {
        if (reference == null || _doc.Textures == null)
            return null;

        if (reference.Index < 0 || reference.Index >= _doc.Textures.Count)
            return null;

        int? source = _doc.Textures[reference.Index].Source;
        if (source == null ||
            source.Value < 0 || source.Value >= _imageTextures.Length)
            return null;

        // Already decoded (once) by DecodeImages.
        return _imageTextures[source.Value];
    }

    /// <summary>Returns the raw bytes of a bufferView (e.g. an embedded image).</summary>
    private byte[] GetBufferViewBytes(int bufferViewIndex)
    {
        GltfBufferView view = _doc.BufferViews![bufferViewIndex];
        byte[] buffer = _buffers[view.Buffer]
            ?? throw new InvalidDataException($"Buffer {view.Buffer} could not be resolved.");

        byte[] slice = new byte[view.ByteLength];
        Array.Copy(buffer, view.ByteOffset, slice, 0, view.ByteLength);
        return slice;
    }

    private static Material DefaultMaterial() => new Material
    {
        Name = "gltf_default",
        DiffuseColor = new ColorRGBAf(0.8f, 0.8f, 0.8f)
    };
}
