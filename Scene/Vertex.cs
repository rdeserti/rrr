using rrr.VMath;

namespace rrr.Scene
{

    public struct Vertex
    {
        public Vector3f Position;

        public Vector3f Normal;

        public Vector2f UV;

        public Vertex(Vector3f position, Vector3f normal, Vector2f texcoord)
        {
            this.Position = position;
            Normal = normal;
        }
    }
}