using rrr.App;
using rrr.VMath;

namespace rrr.Scene;

public class Scene
{
    /// <summary>The active camera used for rendering.</summary>
    public Camera Camera { get; set; }

    /// <summary>All cameras defined for the scene (for selection / listing).</summary>
    public List<Camera> Cameras { get; } =
        new();

    public List<SceneObject> Objects { get; } =
        new();
    public List<Light> Lights { get; } =
        new();
    public List<Material> Materials { get; } =
        new();

    public (
      Vector3f Min,
      Vector3f Max)
      GetBoundingBox()
    {
        bool first = true;

        Vector3f min = Vector3f.Zero;
        Vector3f max = Vector3f.Zero;

        foreach (SceneObject obj in Objects)
        {
            foreach (Vector3f position in obj.Mesh.Positions)
            {
                if (first)
                {
                    min = position;
                    max = position;
                    first = false;
                }
                else
                {
                    min = Vector3f.Min(
                        min,
                        position);

                    max = Vector3f.Max(
                        max,
                        position);
                }
            }
        }

        if (first)
        {
            return (
                Vector3f.Zero,
                Vector3f.Zero);
        }
        Log.Debug("BoundingBox: " + min + " " + max);

        return (min, max);
    }
}