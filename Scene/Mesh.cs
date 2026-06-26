using rrr.Core;
using rrr.VMath;

namespace rrr.Scene
{
    public class Mesh
    {
        public List<Vector3f> Positions { get; } =
            new();

        public List<Vector3f> Normals { get; } =
            new();

        public List<Vector2f> UVs { get; } =
            new();

        /// <summary>
        /// Optional per-vertex colors (parallel to <see cref="Positions"/>),
        /// e.g. glTF COLOR_0. Empty = untinted (white). Modulate the albedo.
        /// </summary>
        public List<ColorRGBAf> Colors { get; } =
            new();

        /// <summary>
        /// Optional per-vertex tangents (parallel to <see cref="Positions"/>):
        /// xyz = tangent direction, w = handedness (±1), e.g. glTF TANGENT.
        /// Empty = tangents are computed from UVs by the render-mesh builder.
        /// </summary>
        public List<Vector4f> Tangents { get; } =
            new();

        public List<Triangle> Triangles { get; } =
            new();
    }
}