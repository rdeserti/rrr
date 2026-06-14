using rrr.Core;
using System;
using System.IO;

namespace rrr.Imaging.Readers;

/// <summary>
/// Minimal BMP decoder. Supports uncompressed (BI_RGB) 24-bit BGR and
/// 32-bit BGRA images with a BITMAPINFOHEADER (40-byte) or larger header.
/// Stored bottom-up or top-down. Anything else throws.
/// </summary>
internal class BmpImageReader : IImageReader
{
    private const ushort Signature = 0x4D42; // 'BM', little-endian
    private const uint BiRgb = 0;            // uncompressed

    public FrameBuffer Load(Stream stream)
    {
        // Copy into a seekable buffer so we can honor the pixel-data offset
        // regardless of the source stream.
        using MemoryStream memory = new MemoryStream();
        stream.CopyTo(memory);
        memory.Position = 0;

        using BinaryReader reader = new BinaryReader(memory);

        //
        // BITMAPFILEHEADER (14 bytes)
        //

        ushort signature = reader.ReadUInt16();

        if (signature != Signature)
            throw new InvalidDataException("Not a BMP file (bad signature).");

        reader.ReadUInt32();             // file size (ignored)
        reader.ReadUInt16();             // reserved1
        reader.ReadUInt16();             // reserved2
        uint pixelDataOffset = reader.ReadUInt32();

        //
        // BITMAPINFOHEADER (at least 40 bytes)
        //

        uint headerSize = reader.ReadUInt32();

        if (headerSize < 40)
            throw new NotSupportedException(
                $"Unsupported BMP header size {headerSize} (need >= 40).");

        int width = reader.ReadInt32();
        int rawHeight = reader.ReadInt32();
        reader.ReadUInt16();             // planes
        ushort bitsPerPixel = reader.ReadUInt16();
        uint compression = reader.ReadUInt32();
        // Remaining info-header fields are not needed.

        if (compression != BiRgb)
            throw new NotSupportedException(
                $"Unsupported BMP compression {compression} (only BI_RGB).");

        if (bitsPerPixel != 24 && bitsPerPixel != 32)
            throw new NotSupportedException(
                $"Unsupported BMP bit depth {bitsPerPixel} (only 24 / 32).");

        if (width <= 0)
            throw new InvalidDataException($"Invalid BMP width {width}.");

        if (rawHeight == 0)
            throw new InvalidDataException("Invalid BMP height 0.");

        bool topDown = rawHeight < 0;
        int height = Math.Abs(rawHeight);

        int bytesPerPixel = bitsPerPixel / 8;

        // Rows are padded to a multiple of 4 bytes.
        int rowSize = ((width * bytesPerPixel + 3) / 4) * 4;

        FrameBuffer frameBuffer =
            new FrameBuffer(width, height, PixelFormat.RGBA32);

        memory.Position = pixelDataOffset;

        byte[] row = new byte[rowSize];

        for (int r = 0; r < height; r++)
        {
            ReadExactly(memory, row, rowSize);

            // Bottom-up files store the bottom row first; map it to the
            // bottom of the (top-down) frame buffer so the result matches
            // the visual orientation.
            int destY = topDown ? r : (height - 1 - r);

            int offset = 0;

            for (int x = 0; x < width; x++)
            {
                byte b = row[offset + 0];
                byte g = row[offset + 1];
                byte rr = row[offset + 2];
                byte a = bytesPerPixel == 4 ? row[offset + 3] : (byte)255;

                offset += bytesPerPixel;

                frameBuffer.SetPixelUnsafe(
                    x, destY,
                    PixelPacker.Pack(new ColorRGBA32(rr, g, b, a)));
            }
        }

        return frameBuffer;
    }

    private static void ReadExactly(Stream stream, byte[] buffer, int count)
    {
        int read = 0;

        while (read < count)
        {
            int n = stream.Read(buffer, read, count - read);

            if (n <= 0)
                throw new EndOfStreamException(
                    "Unexpected end of BMP pixel data.");

            read += n;
        }
    }
}
