using System.Threading.Tasks;
using rrr.Core;

namespace rrr.Rendering;

/// <summary>
/// Supersampling anti-aliasing (SSAA). The funnel renders the scene into a
/// framebuffer scaled up by an integer factor; this routine box-filters each
/// <c>factor × factor</c> block back down to one output pixel by arithmetic
/// mean. It is engine-agnostic — both the rasterizer and the ray tracer just
/// fill the oversized buffer, so a single routine anti-aliases both identically.
///
/// Note: the average is computed in the stored (currently linear-identity) color
/// space, consistent with the rest of the pipeline; revisit if/when gamma is
/// enabled (the average should then happen in linear space).
/// </summary>
public static class Supersampler
{
    /// <summary>
    /// Box-downsamples <paramref name="source"/> by <paramref name="factor"/>.
    /// The source dimensions must be exact multiples of the factor (the funnel
    /// guarantees this by rendering at <c>width·factor × height·factor</c>).
    /// Returns the source unchanged when factor &lt;= 1.
    /// </summary>
    public static FrameBuffer Downsample(FrameBuffer source, int factor)
    {
        if (factor <= 1)
            return source;

        int width = source.Width / factor;
        int height = source.Height / factor;

        FrameBuffer result = new FrameBuffer(width, height, PixelFormat.RGBA32);

        uint[] src = source.Pixels;
        uint[] dst = result.Pixels;
        int srcStride = source.Stride;
        int dstStride = result.Stride;

        float inv = 1.0f / (factor * factor);

        Parallel.For(0, height, y =>
        {
            int sy0 = y * factor;

            for (int x = 0; x < width; x++)
            {
                int sx0 = x * factor;

                uint r = 0, g = 0, b = 0, a = 0;

                for (int dy = 0; dy < factor; dy++)
                {
                    int row = (sy0 + dy) * srcStride + sx0;

                    for (int dx = 0; dx < factor; dx++)
                    {
                        uint px = src[row + dx];          // AARRGGBB
                        a += (px >> 24) & 0xFF;
                        r += (px >> 16) & 0xFF;
                        g += (px >> 8) & 0xFF;
                        b += px & 0xFF;
                    }
                }

                uint ra = (uint)(a * inv + 0.5f);
                uint rr = (uint)(r * inv + 0.5f);
                uint rg = (uint)(g * inv + 0.5f);
                uint rb = (uint)(b * inv + 0.5f);

                dst[y * dstStride + x] =
                    (ra << 24) | (rr << 16) | (rg << 8) | rb;
            }
        });

        return result;
    }
}
