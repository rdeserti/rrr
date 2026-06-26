using rrr.Core;
using rrr.Imaging;
using System;

namespace rrr.Scene
{
    public enum TextureWrap
    {
        Repeat,
        Clamp
    }

    public enum TextureFilter
    {
        Nearest,
        Bilinear
    }

    /// <summary>
    /// A 2D texture backed by a <see cref="FrameBuffer"/> (RGBA32).
    ///
    /// UV convention: (0,0) is the TOP-LEFT texel and v grows downward,
    /// matching the way the frame buffer stores rows (row 0 = top). OBJ
    /// assets authored with a bottom-left origin may need their v flipped
    /// at import time.
    /// </summary>
    public class Texture2D
    {
        public FrameBuffer Image { get; }

        public int Width => Image.Width;
        public int Height => Image.Height;

        public TextureWrap WrapU { get; set; } = TextureWrap.Repeat;
        public TextureWrap WrapV { get; set; } = TextureWrap.Repeat;
        public TextureFilter Filter { get; set; } = TextureFilter.Bilinear;

        public Texture2D(FrameBuffer image)
        {
            Image = image
                ?? throw new ArgumentNullException(nameof(image));
        }

        /// <summary>
        /// Loads a texture from file, returning null (and logging) on any
        /// failure, so a broken/unsupported texture degrades to a solid
        /// color instead of aborting the render.
        /// </summary>
        public static Texture2D? TryLoad(string fileName)
        {
            try
            {
                FrameBuffer image =
                    ImageReader.Load(fileName);

                return new Texture2D(image);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"WARNING: could not load texture '{fileName}': " +
                    $"{ex.Message}. Using solid color instead.");

                return null;
            }
        }

        /// <summary>
        /// Loads a texture from an in-memory image (format auto-detected),
        /// returning null (and logging) on any failure — the same graceful
        /// degradation as <see cref="TryLoad"/>. Used for container-embedded
        /// textures (e.g. glTF bufferView / data-URI images).
        /// </summary>
        public static Texture2D? FromBytes(byte[] data, string? label = null)
        {
            try
            {
                FrameBuffer image = ImageReader.Load(data);
                return new Texture2D(image);
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"WARNING: could not decode embedded texture" +
                    (label != null ? $" '{label}'" : "") +
                    $": {ex.Message}. Using solid color instead.");

                return null;
            }
        }

        /// <summary>
        /// Heuristic: true if (almost) every sampled texel has R == G == B,
        /// i.e. the image is grayscale. Used to tell a height/bump map apart
        /// from a tangent-space normal map (which is colored, not gray).
        /// </summary>
        public bool IsLikelyGrayscale(float tolerance = 0.04f)
        {
            int stepX = Math.Max(1, Width / 64);
            int stepY = Math.Max(1, Height / 64);

            int total = 0;
            int gray = 0;

            for (int y = 0; y < Height; y += stepY)
            {
                for (int x = 0; x < Width; x += stepX)
                {
                    ColorRGBAf c = ColorRGBAf.FromRGBA32(
                        PixelPacker.Unpack(Image.GetPixel(x, y)));

                    total++;

                    if (MathF.Abs(c.R - c.G) <= tolerance &&
                        MathF.Abs(c.G - c.B) <= tolerance)
                    {
                        gray++;
                    }
                }
            }

            return total > 0 && gray >= total * 0.95f;
        }

        /// <summary>
        /// Samples the texture at (u, v) using the configured filter.
        /// </summary>
        public ColorRGBAf Sample(float u, float v)
        {
            return Filter == TextureFilter.Nearest
                ? SampleNearest(u, v)
                : SampleBilinear(u, v);
        }

        public ColorRGBAf SampleNearest(float u, float v)
        {
            float su = WrapCoord(u, WrapU);
            float sv = WrapCoord(v, WrapV);

            int x = Math.Min((int)(su * Width), Width - 1);
            int y = Math.Min((int)(sv * Height), Height - 1);

            return Texel(x, y);
        }

        public ColorRGBAf SampleBilinear(float u, float v)
        {
            // Texel-center sampling: shift by half a texel.
            float su = WrapCoord(u, WrapU) * Width - 0.5f;
            float sv = WrapCoord(v, WrapV) * Height - 0.5f;

            int x0 = (int)MathF.Floor(su);
            int y0 = (int)MathF.Floor(sv);

            float fx = su - x0;
            float fy = sv - y0;

            int x1 = x0 + 1;
            int y1 = y0 + 1;

            x0 = WrapIndex(x0, Width, WrapU);
            x1 = WrapIndex(x1, Width, WrapU);
            y0 = WrapIndex(y0, Height, WrapV);
            y1 = WrapIndex(y1, Height, WrapV);

            ColorRGBAf c00 = Texel(x0, y0);
            ColorRGBAf c10 = Texel(x1, y0);
            ColorRGBAf c01 = Texel(x0, y1);
            ColorRGBAf c11 = Texel(x1, y1);

            ColorRGBAf top = Lerp(c00, c10, fx);
            ColorRGBAf bottom = Lerp(c01, c11, fx);

            return Lerp(top, bottom, fy);
        }

        private ColorRGBAf Texel(int x, int y)
        {
            return ColorRGBAf.FromRGBA32(
                PixelPacker.Unpack(
                    Image.GetPixel(x, y)));
        }

        private static float WrapCoord(float c, TextureWrap wrap)
        {
            if (wrap == TextureWrap.Repeat)
                return c - MathF.Floor(c);   // -> [0, 1)

            return Math.Clamp(c, 0.0f, 1.0f);
        }

        private static int WrapIndex(int i, int size, TextureWrap wrap)
        {
            if (wrap == TextureWrap.Repeat)
                return ((i % size) + size) % size;

            return Math.Clamp(i, 0, size - 1);
        }

        private static ColorRGBAf Lerp(ColorRGBAf a, ColorRGBAf b, float t)
        {
            return new ColorRGBAf(
                a.R + (b.R - a.R) * t,
                a.G + (b.G - a.G) * t,
                a.B + (b.B - a.B) * t,
                a.A + (b.A - a.A) * t);
        }
    }
}
