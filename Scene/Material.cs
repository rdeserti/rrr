using rrr.Core;

namespace rrr.Scene
{
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

        public string? SourceMaterialName { get; set; }
    }
}