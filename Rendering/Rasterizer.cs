using rrr.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace rrr.Rendering
{
    internal class Rasterizer
    {
        public static void DrawLine(
            FrameBuffer frameBuffer,
            int x0,
            int y0,
            int x1,
            int y1,
            ColorRGBAf color)
        {
            DrawLine(
                frameBuffer, x0, y0, x1, y1,
                PixelPacker.Pack(
                    ColorRGBA32.FromRGBAf(color)));
        }

        public static void DrawLineUnsafe(
            FrameBuffer frameBuffer,
            int x0,
            int y0,
            int x1,
            int y1,
            ColorRGBAf color)
        {
            DrawLineUnsafe(
                frameBuffer,
                x0, y0, x1, y1,
                PixelPacker.Pack(
                    ColorRGBA32.FromRGBAf(color)));
        }

        public static void DrawLine(
            FrameBuffer frameBuffer,
            int x0, int y0, int x1, int y1,
            uint color)
        {
            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);

            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;

            int err = dx - dy;

            while (true)
            {
                if ((uint)x0 < (uint)frameBuffer.Width &&
                    (uint)y0 < (uint)frameBuffer.Height)
                {
                    frameBuffer.SetPixelUnsafe(
                        x0,
                        y0,
                        color);
                }

                if (x0 == x1 && y0 == y1)
                    break;

                int e2 = err * 2;

                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }

                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        /// <summary>
        /// Draws a line without bounds checking.
        /// Both endpoints must define a line entirely contained
        /// inside the framebuffer.
        /// Undefined behaviour otherwise.
        /// </summary>
        public static void DrawLineUnsafe(
            FrameBuffer frameBuffer,
            int x0, int y0, int x1, int y1,
            uint color)
        {
            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);

            int sx = x0 < x1 ? 1 : -1;
            int sy = y0 < y1 ? 1 : -1;

            int err = dx - dy;

            while (true)
            {
                frameBuffer.SetPixelUnsafe(
                    x0,
                    y0,
                    color);

                if (x0 == x1 && y0 == y1)
                    break;

                int e2 = err * 2;

                if (e2 > -dy)
                {
                    err -= dy;
                    x0 += sx;
                }

                if (e2 < dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        private static int Edge(int ax, int ay, int bx, int by, int px, int py)
        {
            return
                (px - ax) * (by - ay) -
                (py - ay) * (bx - ax);
        }

        // Integer ceil/floor for division by a POSITIVE divisor.
        // C# integer division truncates toward zero, so we correct it.

        private static int CeilDiv(int a, int b)
        {
            return a / b + (a % b > 0 ? 1 : 0);
        }

        private static int FloorDiv(int a, int b)
        {
            return a / b - (a % b < 0 ? 1 : 0);
        }

        /// <summary>
        /// Narrows the scanline interval [kStart, kEnd] (in pixels from minX)
        /// to the half-plane of a single edge. The edge value at column k is
        /// <c>wRow + k * stepX</c>; a pixel is inside this edge when that value,
        /// times <paramref name="orient"/> (+1 CCW, -1 CW), is &gt;= 0 — exactly
        /// the per-pixel test, so coverage is identical.
        /// Returns false if the edge excludes the whole row.
        /// </summary>
        private static bool ClampEdge(
            int wRow,
            int stepX,
            int orient,
            ref int kStart,
            ref int kEnd)
        {
            int value0 = wRow * orient;
            int s = stepX * orient;

            if (s == 0)
            {
                // Constant along the row: either fully inside or fully out.
                return value0 >= 0;
            }

            if (s > 0)
            {
                // value0 + k*s >= 0  ->  k >= ceil(-value0 / s)
                int kmin = CeilDiv(-value0, s);

                if (kmin > kStart)
                    kStart = kmin;
            }
            else
            {
                // value0 - k*|s| >= 0  ->  k <= floor(value0 / |s|)
                int kmax = FloorDiv(value0, -s);

                if (kmax < kEnd)
                    kEnd = kmax;
            }

            return true;
        }

        /// <summary>
        /// Computes the covered pixel span [kStart, kEnd] (offsets from minX)
        /// on a scanline, given the three edge values at minX and their per-x
        /// steps. Returns false if the row is not covered at all.
        /// </summary>
        private static bool ComputeSpan(
            int w0Row, int w1Row, int w2Row,
            int stepX0, int stepX1, int stepX2,
            int orient, int range,
            out int kStart, out int kEnd)
        {
            kStart = 0;
            kEnd = range;

            if (!ClampEdge(w0Row, stepX0, orient, ref kStart, ref kEnd))
                return false;

            if (!ClampEdge(w1Row, stepX1, orient, ref kStart, ref kEnd))
                return false;

            if (!ClampEdge(w2Row, stepX2, orient, ref kStart, ref kEnd))
                return false;

            return kStart <= kEnd;
        }

        public static void DrawTriangle(
            FrameBuffer frameBuffer,
            int x0, int y0, int x1, int y1, int x2, int y2,
            uint color)
        {
            DrawLine(frameBuffer, x0, y0, x1, y1, color);
            DrawLine(frameBuffer, x1, y1, x2, y2, color);
            DrawLine(frameBuffer, x2, y2, x0, y0, color);
        }

        public static void DrawTriangle(
            FrameBuffer frameBuffer,
            int x0, int y0, int x1, int y1, int x2, int y2,
            ColorRGBAf color)
        {
            DrawTriangle(frameBuffer, x0, y0, x1, y1, x2, y2,
                PixelPacker.Pack(
                    ColorRGBA32.FromRGBAf(color)));
        }

        public static void FillTriangle(
            FrameBuffer frameBuffer,
            DepthBuffer depthBuffer,

            int x0, int y0, float z0,
            int x1, int y1, float z1,
            int x2, int y2, float z2,

            uint color,
            int bandMinY,
            int bandMaxY)
        {
            int area =
                Edge(
                    x0, y0,
                    x1, y1,
                    x2, y2);

            if (area == 0)
                return;

            int minX =
                Math.Max(
                    0,
                    Math.Min(
                        x0,
                        Math.Min(x1, x2)));

            int maxX =
                Math.Min(
                    frameBuffer.Width - 1,
                    Math.Max(
                        x0,
                        Math.Max(x1, x2)));

            int minY =
                Math.Max(
                    0,
                    Math.Min(
                        y0,
                        Math.Min(y1, y2)));

            int maxY =
                Math.Min(
                    frameBuffer.Height - 1,
                    Math.Max(
                        y0,
                        Math.Max(y1, y2)));

            // Restrict to the caller's horizontal band (for parallel
            // rasterization). Rows outside [bandMinY, bandMaxY] belong to
            // other threads, so this triangle simply contributes nothing here.
            if (minY < bandMinY)
                minY = bandMinY;

            if (maxY > bandMaxY)
                maxY = bandMaxY;

            if (minY > maxY)
                return;

            bool ccw = area > 0;

            float invArea =
                1.0f / area;

            //
            // Incremental edge functions.
            //
            // Each w is linear in (x, y), so instead of recomputing Edge()
            // per pixel we evaluate it once at (minX, minY) and step it by
            // constant integer deltas: +stepX moving one pixel right,
            // +stepY moving one pixel down. All-integer, so the produced
            // values are identical to the per-pixel Edge() calls.
            //

            int stepX0 = y2 - y1;
            int stepX1 = y0 - y2;
            int stepX2 = y1 - y0;

            int stepY0 = x1 - x2;
            int stepY1 = x2 - x0;
            int stepY2 = x0 - x1;

            int w0Row = Edge(x1, y1, x2, y2, minX, minY);
            int w1Row = Edge(x2, y2, x0, y0, minX, minY);
            int w2Row = Edge(x0, y0, x1, y1, minX, minY);

            int orient = ccw ? 1 : -1;
            int range = maxX - minX;

            for (int y = minY; y <= maxY; y++)
            {
                if (ComputeSpan(
                        w0Row, w1Row, w2Row,
                        stepX0, stepX1, stepX2,
                        orient, range,
                        out int kStart, out int kEnd))
                {
                    int w0 = w0Row + kStart * stepX0;
                    int w1 = w1Row + kStart * stepX1;
                    int w2 = w2Row + kStart * stepX2;

                    int xStart = minX + kStart;
                    int xEnd = minX + kEnd;

                    for (int x = xStart; x <= xEnd; x++)
                    {
                        float b0 = w0 * invArea;
                        float b1 = w1 * invArea;
                        float b2 = w2 * invArea;

                        float z =
                          b0 * z0 +
                          b1 * z1 +
                          b2 * z2;

                        if (depthBuffer.TestAndWriteUnsafe(
                                x,
                                y,
                                z))
                        {
                            frameBuffer.SetPixelUnsafe(
                                x,
                                y,
                                color);
                        }

                        w0 += stepX0;
                        w1 += stepX1;
                        w2 += stepX2;
                    }
                }

                w0Row += stepY0;
                w1Row += stepY1;
                w2Row += stepY2;
            }
        }

        /// <summary>
        /// Fills a triangle calling <paramref name="shader"/> for every
        /// covered pixel, passing the barycentric weights (b0, b1, b2)
        /// of that pixel. The returned color is packed and written.
        ///
        /// This is the entry point for interpolated shading:
        ///   - Gouraud: interpolate the three precomputed vertex colors
        ///   - Phong:   interpolate world position / normal, then light
        ///   - (tomorrow) texture: interpolate UVs and sample
        ///
        /// <typeparamref name="TShader"/> is a <c>struct</c> so the JIT
        /// produces a dedicated, allocation-free, inlinable specialization
        /// per shader type instead of a boxed/virtual delegate call.
        /// </summary>
        public static void FillTriangle<TShader>(
            FrameBuffer frameBuffer,
            DepthBuffer depthBuffer,

            int x0, int y0, float z0,
            int x1, int y1, float z1,
            int x2, int y2, float z2,

            float iw0, float iw1, float iw2,

            TShader shader,
            int bandMinY,
            int bandMaxY,
            bool blend = false)
            where TShader : struct, IPixelShader
        {
            int area =
                Edge(
                    x0, y0,
                    x1, y1,
                    x2, y2);

            if (area == 0)
                return;

            int minX =
                Math.Max(
                    0,
                    Math.Min(
                        x0,
                        Math.Min(x1, x2)));

            int maxX =
                Math.Min(
                    frameBuffer.Width - 1,
                    Math.Max(
                        x0,
                        Math.Max(x1, x2)));

            int minY =
                Math.Max(
                    0,
                    Math.Min(
                        y0,
                        Math.Min(y1, y2)));

            int maxY =
                Math.Min(
                    frameBuffer.Height - 1,
                    Math.Max(
                        y0,
                        Math.Max(y1, y2)));

            // Restrict to the caller's horizontal band (for parallel
            // rasterization). Rows outside [bandMinY, bandMaxY] belong to
            // other threads, so this triangle simply contributes nothing here.
            if (minY < bandMinY)
                minY = bandMinY;

            if (maxY > bandMaxY)
                maxY = bandMaxY;

            if (minY > maxY)
                return;

            bool ccw = area > 0;

            float invArea =
                1.0f / area;

            //
            // Incremental edge functions (see the uint overload for details).
            //

            int stepX0 = y2 - y1;
            int stepX1 = y0 - y2;
            int stepX2 = y1 - y0;

            int stepY0 = x1 - x2;
            int stepY1 = x2 - x0;
            int stepY2 = x0 - x1;

            int w0Row = Edge(x1, y1, x2, y2, minX, minY);
            int w1Row = Edge(x2, y2, x0, y0, minX, minY);
            int w2Row = Edge(x0, y0, x1, y1, minX, minY);

            int orient = ccw ? 1 : -1;
            int range = maxX - minX;

            for (int y = minY; y <= maxY; y++)
            {
                if (ComputeSpan(
                        w0Row, w1Row, w2Row,
                        stepX0, stepX1, stepX2,
                        orient, range,
                        out int kStart, out int kEnd))
                {
                    int w0 = w0Row + kStart * stepX0;
                    int w1 = w1Row + kStart * stepX1;
                    int w2 = w2Row + kStart * stepX2;

                    int xStart = minX + kStart;
                    int xEnd = minX + kEnd;

                    for (int x = xStart; x <= xEnd; x++)
                    {
                        float b0 = w0 * invArea;
                        float b1 = w1 * invArea;
                        float b2 = w2 * invArea;

                        float z =
                          b0 * z0 +
                          b1 * z1 +
                          b2 * z2;

                        // Depth test first (peek). The depth write is deferred
                        // until after shading so a shader can discard the
                        // fragment (alpha-test) without leaving a depth hole.
                        // For opaque shaders this is equivalent to the old
                        // test-and-write: the same pixels are shaded.
                        if (depthBuffer.TestUnsafe(x, y, z))
                        {
                            // Perspective-correct barycentric weights for
                            // attribute interpolation: weight each vertex by
                            // 1/w, then renormalize. (Depth z stays affine —
                            // it is already linear in screen space.)
                            float l0 = b0 * iw0;
                            float l1 = b1 * iw1;
                            float l2 = b2 * iw2;

                            float invSum = 1.0f / (l0 + l1 + l2);

                            ColorRGBAf color =
                                shader.Shade(
                                    l0 * invSum,
                                    l1 * invSum,
                                    l2 * invSum);

                            // A negative alpha is the shader's "discard" signal
                            // (alpha-tested fragment below the cutoff).
                            if (color.A >= 0.0f)
                            {
                                if (blend)
                                {
                                    // Transparent pass: src-over-dst into the
                                    // existing pixel; depth is NOT written so
                                    // later (nearer, back-to-front) transparent
                                    // fragments still blend on top.
                                    float a = color.A;
                                    if (a > 0.0f)
                                    {
                                        ColorRGBAf dst =
                                            ColorRGBAf.FromRGBA32(
                                                PixelPacker.Unpack(
                                                    frameBuffer.GetPixelUnsafe(x, y)));

                                        ColorRGBAf outc = new ColorRGBAf(
                                            color.R * a + dst.R * (1.0f - a),
                                            color.G * a + dst.G * (1.0f - a),
                                            color.B * a + dst.B * (1.0f - a),
                                            1.0f);

                                        frameBuffer.SetPixelUnsafe(
                                            x, y,
                                            PixelPacker.Pack(ColorRGBA32.FromRGBAf(outc)));
                                    }
                                }
                                else
                                {
                                    depthBuffer.WriteUnsafe(x, y, z);

                                    frameBuffer.SetPixelUnsafe(
                                        x,
                                        y,
                                        PixelPacker.Pack(
                                            ColorRGBA32.FromRGBAf(color)));
                                }
                            }
                        }

                        w0 += stepX0;
                        w1 += stepX1;
                        w2 += stepX2;
                    }
                }

                w0Row += stepY0;
                w1Row += stepY1;
                w2Row += stepY2;
            }
        }
    }
}
