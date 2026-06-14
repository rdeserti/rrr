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

        public List<Triangle> Triangles { get; } =
            new();
    }
}