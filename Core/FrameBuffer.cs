using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace rrr.Core
{
    public class FrameBuffer
    {
        private readonly uint[] _pixels;

        public int Width { get; }
        public int Height { get; }
        public PixelFormat PixelFormat { get; }
        public int PixelCount => Width * Height;
        public int Stride { get; }

        public uint[] Pixels => _pixels;

        public FrameBuffer(
            int width,
            int height,
            PixelFormat pixelFormat)
        {
            if (width <= 0)
                throw new ArgumentOutOfRangeException(nameof(width));

            if (height <= 0)
                throw new ArgumentOutOfRangeException(nameof(height));

            Width = width;
            Stride = width;
            Height = height;
            PixelFormat = pixelFormat;

            switch (pixelFormat)
            {
                case PixelFormat.RGBA32:
                    _pixels = new uint[width * height];
                    break;

                default:
                    throw new NotSupportedException(
                        $"Pixel format '{pixelFormat}' not supported.");
            }
        }

        public void SetPixel(int x, int y, uint color)
        {
            if (x < 0 || x >= Width)
                return;

            if (y < 0 || y >= Height)
                return;

            _pixels[y * Stride + x] = color;
        }

        internal void SetPixelUnsafe(int x, int y, uint color)
        {
            _pixels[y * Stride + x] = color;
        }

        internal uint GetPixelUnsafe(int x, int y)
        {
            return _pixels[y * Stride + x];
        }

        public void SetPixel(
            int x,
            int y,
            ColorRGBA32 color)
        {
            SetPixel(
                x,
                y,
                PixelPacker.Pack(color));
        }

        public uint GetPixel(int x, int y)
        {
            if (x < 0 || x >= Width)
                throw new ArgumentOutOfRangeException(nameof(x));

            if (y < 0 || y >= Height)
                throw new ArgumentOutOfRangeException(nameof(y));

            return _pixels[y * Stride + x];
        }

        public void Clear(uint color)
        {
            Array.Fill(_pixels, color);
        }

        public void DrawSpan(
            int y,
            int xStart,
            int xEnd,
            uint color)
        {
            if (y < 0 || y >= Height)
                return;

            if (xStart > xEnd)
                (xStart, xEnd) = (xEnd, xStart);

            if (xEnd < 0 || xStart >= Width)
                return;

            xStart = Math.Max(0, xStart);
            xEnd = Math.Min(Width - 1, xEnd);

            int index = y * Stride + xStart;

            for (int x = xStart; x <= xEnd; x++)
            {
                _pixels[index++] = color;
            }
        }

        internal void DrawSpanUnsafe(
            int y,
            int xStart,
            int xEnd,
            uint color)
        {
            int startIndex = y * Stride + xStart;
            int length = xEnd - xStart + 1;

            _pixels.AsSpan(startIndex, length).Fill(color);
        }
    }
}
