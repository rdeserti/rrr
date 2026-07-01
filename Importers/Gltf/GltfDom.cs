using System.Text.Json.Serialization;

namespace rrr.Importers.Gltf;

/// <summary>
/// Plain data-transfer objects mirroring the subset of the glTF 2.0 JSON schema
/// the importer consumes. Deserialized with System.Text.Json (part of the .NET
/// runtime — no external package). Unmapped fields are simply ignored.
/// </summary>
internal sealed class GltfDocument
{
    [JsonPropertyName("scene")] public int? Scene { get; set; }
    [JsonPropertyName("scenes")] public List<GltfScene>? Scenes { get; set; }
    [JsonPropertyName("nodes")] public List<GltfNode>? Nodes { get; set; }
    [JsonPropertyName("meshes")] public List<GltfMesh>? Meshes { get; set; }
    [JsonPropertyName("accessors")] public List<GltfAccessor>? Accessors { get; set; }
    [JsonPropertyName("bufferViews")] public List<GltfBufferView>? BufferViews { get; set; }
    [JsonPropertyName("buffers")] public List<GltfBuffer>? Buffers { get; set; }
    [JsonPropertyName("materials")] public List<GltfMaterial>? Materials { get; set; }
    [JsonPropertyName("textures")] public List<GltfTexture>? Textures { get; set; }
    [JsonPropertyName("images")] public List<GltfImage>? Images { get; set; }
    [JsonPropertyName("cameras")] public List<GltfCamera>? Cameras { get; set; }
    [JsonPropertyName("extensions")] public GltfDocumentExtensions? Extensions { get; set; }
}

internal sealed class GltfDocumentExtensions
{
    [JsonPropertyName("KHR_lights_punctual")] public GltfLightsPunctual? Lights { get; set; }
}

internal sealed class GltfLightsPunctual
{
    [JsonPropertyName("lights")] public List<GltfLight>? Lights { get; set; }
}

internal sealed class GltfLight
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = "point"; // directional|point|spot
    [JsonPropertyName("color")] public float[]? Color { get; set; }
    [JsonPropertyName("intensity")] public float? Intensity { get; set; }
    [JsonPropertyName("range")] public float? Range { get; set; }
    [JsonPropertyName("spot")] public GltfLightSpot? Spot { get; set; }
}

internal sealed class GltfLightSpot
{
    [JsonPropertyName("innerConeAngle")] public float? InnerConeAngle { get; set; } // radians
    [JsonPropertyName("outerConeAngle")] public float? OuterConeAngle { get; set; } // radians
}

internal sealed class GltfCamera
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = "perspective";
    [JsonPropertyName("perspective")] public GltfPerspective? Perspective { get; set; }
}

internal sealed class GltfPerspective
{
    [JsonPropertyName("yfov")] public float Yfov { get; set; } = 1.0f;        // radians
    [JsonPropertyName("znear")] public float Znear { get; set; } = 0.1f;
    [JsonPropertyName("zfar")] public float? Zfar { get; set; }
    [JsonPropertyName("aspectRatio")] public float? AspectRatio { get; set; }
}

internal sealed class GltfScene
{
    [JsonPropertyName("nodes")] public List<int>? Nodes { get; set; }
}

internal sealed class GltfNode
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("mesh")] public int? Mesh { get; set; }
    [JsonPropertyName("camera")] public int? Camera { get; set; }
    [JsonPropertyName("children")] public List<int>? Children { get; set; }
    [JsonPropertyName("extensions")] public GltfNodeExtensions? Extensions { get; set; }

    // Either a full matrix (column-major, 16) or TRS components.
    [JsonPropertyName("matrix")] public float[]? Matrix { get; set; }
    [JsonPropertyName("translation")] public float[]? Translation { get; set; }
    [JsonPropertyName("rotation")] public float[]? Rotation { get; set; }   // quaternion x,y,z,w
    [JsonPropertyName("scale")] public float[]? Scale { get; set; }
}

internal sealed class GltfNodeExtensions
{
    [JsonPropertyName("KHR_lights_punctual")] public GltfNodeLightRef? Light { get; set; }
}

internal sealed class GltfNodeLightRef
{
    [JsonPropertyName("light")] public int Light { get; set; }
}

internal sealed class GltfMesh
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("primitives")] public List<GltfPrimitive>? Primitives { get; set; }
}

internal sealed class GltfPrimitive
{
    [JsonPropertyName("attributes")] public Dictionary<string, int>? Attributes { get; set; }
    [JsonPropertyName("indices")] public int? Indices { get; set; }
    [JsonPropertyName("material")] public int? Material { get; set; }
    [JsonPropertyName("mode")] public int? Mode { get; set; }   // 4 = triangles (default)
}

internal sealed class GltfAccessor
{
    [JsonPropertyName("bufferView")] public int? BufferView { get; set; }
    [JsonPropertyName("byteOffset")] public int ByteOffset { get; set; }
    [JsonPropertyName("componentType")] public int ComponentType { get; set; }
    [JsonPropertyName("normalized")] public bool Normalized { get; set; }
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = "SCALAR";
    [JsonPropertyName("sparse")] public GltfSparse? Sparse { get; set; }
}

internal sealed class GltfSparse
{
    [JsonPropertyName("count")] public int Count { get; set; }
    [JsonPropertyName("indices")] public GltfSparseIndices? Indices { get; set; }
    [JsonPropertyName("values")] public GltfSparseValues? Values { get; set; }
}

internal sealed class GltfSparseIndices
{
    [JsonPropertyName("bufferView")] public int BufferView { get; set; }
    [JsonPropertyName("byteOffset")] public int ByteOffset { get; set; }
    [JsonPropertyName("componentType")] public int ComponentType { get; set; }
}

internal sealed class GltfSparseValues
{
    [JsonPropertyName("bufferView")] public int BufferView { get; set; }
    [JsonPropertyName("byteOffset")] public int ByteOffset { get; set; }
}

internal sealed class GltfBufferView
{
    [JsonPropertyName("buffer")] public int Buffer { get; set; }
    [JsonPropertyName("byteOffset")] public int ByteOffset { get; set; }
    [JsonPropertyName("byteLength")] public int ByteLength { get; set; }
    [JsonPropertyName("byteStride")] public int? ByteStride { get; set; }
}

internal sealed class GltfBuffer
{
    [JsonPropertyName("uri")] public string? Uri { get; set; }
    [JsonPropertyName("byteLength")] public int ByteLength { get; set; }
}

internal sealed class GltfMaterial
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("pbrMetallicRoughness")] public GltfPbr? Pbr { get; set; }
    [JsonPropertyName("normalTexture")] public GltfTextureRef? NormalTexture { get; set; }
    [JsonPropertyName("occlusionTexture")] public GltfTextureRef? OcclusionTexture { get; set; }
    [JsonPropertyName("emissiveFactor")] public float[]? EmissiveFactor { get; set; }
    [JsonPropertyName("emissiveTexture")] public GltfTextureRef? EmissiveTexture { get; set; }
    [JsonPropertyName("alphaMode")] public string? AlphaMode { get; set; }     // OPAQUE|MASK|BLEND
    [JsonPropertyName("alphaCutoff")] public float? AlphaCutoff { get; set; }
    [JsonPropertyName("doubleSided")] public bool DoubleSided { get; set; }
    [JsonPropertyName("extensions")] public GltfMaterialExtensions? Extensions { get; set; }
}

internal sealed class GltfMaterialExtensions
{
    [JsonPropertyName("KHR_materials_unlit")] public object? Unlit { get; set; } // presence = unlit
    [JsonPropertyName("KHR_materials_emissive_strength")] public GltfEmissiveStrength? EmissiveStrength { get; set; }
    [JsonPropertyName("KHR_materials_transmission")] public GltfTransmission? Transmission { get; set; }
    [JsonPropertyName("KHR_materials_ior")] public GltfIor? Ior { get; set; }
}

internal sealed class GltfEmissiveStrength
{
    [JsonPropertyName("emissiveStrength")] public float EmissiveStrength { get; set; } = 1.0f;
}

internal sealed class GltfTransmission
{
    [JsonPropertyName("transmissionFactor")] public float TransmissionFactor { get; set; } = 0.0f;
}

internal sealed class GltfIor
{
    [JsonPropertyName("ior")] public float Ior { get; set; } = 1.5f;
}

internal sealed class GltfPbr
{
    [JsonPropertyName("baseColorFactor")] public float[]? BaseColorFactor { get; set; }
    [JsonPropertyName("baseColorTexture")] public GltfTextureRef? BaseColorTexture { get; set; }
    [JsonPropertyName("metallicFactor")] public float? MetallicFactor { get; set; }
    [JsonPropertyName("roughnessFactor")] public float? RoughnessFactor { get; set; }
}

internal sealed class GltfTextureRef
{
    [JsonPropertyName("index")] public int Index { get; set; }
    [JsonPropertyName("texCoord")] public int TexCoord { get; set; }
}

internal sealed class GltfTexture
{
    [JsonPropertyName("source")] public int? Source { get; set; }
    [JsonPropertyName("sampler")] public int? Sampler { get; set; }
}

internal sealed class GltfImage
{
    [JsonPropertyName("uri")] public string? Uri { get; set; }
    [JsonPropertyName("bufferView")] public int? BufferView { get; set; }
    [JsonPropertyName("mimeType")] public string? MimeType { get; set; }
}
