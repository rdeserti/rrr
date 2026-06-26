using rrr.Core;

namespace rrr.Scene
{
    /// <summary>How a material's alpha channel is interpreted.</summary>
    public enum AlphaMode
    {
        /// <summary>Alpha ignored; fully opaque.</summary>
        Opaque,

        /// <summary>Alpha-test: fragments below <see cref="Material.AlphaCutoff"/> are discarded.</summary>
        Mask,

        /// <summary>Blended transparency (not yet rendered as such; treated as opaque).</summary>
        Blend
    }

    public class Material
    {
        public string Name { get; set; } = "";

        public ColorRGBAf DiffuseColor { get; set; } =
            ColorRGBAf.White;

        public ColorRGBAf SpecularColor { get; set; } =
            ColorRGBAf.White;

        /// <summary>
        /// Blinn-Phong specular exponent. 0 disables the specular term.
        /// </summary>
        public float Shininess { get; set; } = 0.0f;

        /// <summary>
        /// Self-illumination color, added to the lit result regardless of
        /// lights. Black (default) means no emission.
        /// </summary>
        public ColorRGBAf EmissiveColor { get; set; } =
            ColorRGBAf.Black;

        public Texture2D? DiffuseTexture { get; set; }

        /// <summary>Per-pixel specular color (map_Ks), modulates SpecularColor.</summary>
        public Texture2D? SpecularTexture { get; set; }

        /// <summary>Per-pixel emissive color (map_Ke), modulates EmissiveColor.</summary>
        public Texture2D? EmissiveTexture { get; set; }

        /// <summary>
        /// Tangent-space normal map (default) or, when
        /// <see cref="NormalIsHeightMap"/> is true, a grayscale height map
        /// from which the normal is derived.
        /// </summary>
        public Texture2D? NormalTexture { get; set; }

        public bool NormalIsHeightMap { get; set; } = false;

        /// <summary>
        /// Invert the green channel of a normal map (DirectX-style maps use
        /// the opposite Y sign from OpenGL-style). Ignored for height maps.
        /// </summary>
        public bool InvertNormalGreen { get; set; } = false;

        /// <summary>Strength of the normal/bump perturbation.</summary>
        public float BumpScale { get; set; } = 1.0f;

        /// <summary>
        /// Grayscale ambient-occlusion map (glTF occlusionTexture). Darkens the
        /// ambient + lit terms (not emissive) per pixel. Null = no occlusion.
        /// </summary>
        public Texture2D? OcclusionTexture { get; set; }

        /// <summary>Alpha interpretation (opaque / alpha-test / blend).</summary>
        public AlphaMode AlphaMode { get; set; } = AlphaMode.Opaque;

        /// <summary>Alpha-test threshold for <see cref="AlphaMode.Mask"/>.</summary>
        public float AlphaCutoff { get; set; } = 0.5f;

        /// <summary>Render both faces (disable backface culling for this material).</summary>
        public bool DoubleSided { get; set; } = false;

        /// <summary>Unlit (KHR_materials_unlit): output the base color directly, no lighting.</summary>
        public bool Unlit { get; set; } = false;

        public string? SourceMaterialName { get; set; }
    }
}