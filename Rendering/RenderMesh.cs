using rrr.VMath;

namespace rrr.Rendering;

public class RenderMesh
{
    public List<RenderVertex> Vertices { get; } =
        new();

    public List<int> Indices { get; } =
        new();
}