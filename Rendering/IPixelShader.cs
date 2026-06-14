using rrr.Core;

namespace rrr.Rendering;

/// <summary>
/// Per-pixel color source for the rasterizer. Implemented as a
/// <c>struct</c> and passed through a generic constraint so the JIT
/// monomorphizes <see cref="Rasterizer.FillTriangle{TShader}"/>, inlines
/// <see cref="Shade"/> into the inner loop, and avoids any per-triangle
/// heap allocation (unlike a delegate).
/// </summary>
public interface IPixelShader
{
    /// <summary>
    /// Returns the color at a pixel given its barycentric weights.
    /// </summary>
    ColorRGBAf Shade(float b0, float b1, float b2);
}
