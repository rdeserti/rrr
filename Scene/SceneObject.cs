using rrr.Rendering;
using rrr.VMath;

namespace rrr.Scene

{
    public class SceneObject
    {
        public string Name { get; set; } = "";

        public Mesh Mesh { get; set; }

        public RenderMesh RenderMesh { get; set; }

        public Material Material { get; set; }

        public Matrix4x4f Transform { get; set; } =
            Matrix4x4f.Identity;

        /// <summary>
        /// Returns the indexed render mesh, building it from
        /// <see cref="Mesh"/> on first access and caching the result.
        /// Call <see cref="InvalidateRenderMesh"/> if the source mesh changes.
        /// </summary>
        public RenderMesh GetRenderMesh()
        {
            RenderMesh ??= RenderMeshBuilder.Build(Mesh);
            return RenderMesh;
        }

        /// <summary>
        /// Drops the cached render mesh so it is rebuilt on next access.
        /// </summary>
        public void InvalidateRenderMesh()
        {
            RenderMesh = null!;
        }
    }

}