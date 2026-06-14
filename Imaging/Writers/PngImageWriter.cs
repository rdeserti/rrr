using rrr.Core;
using System;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace rrr.Imaging.Writers;

/// <summary>
/// Minimal PNG encoder: 8-bit RGBA (color type 6), non-interlaced, one
/// "None" filter byte per scanline, zlib/DEFLATE compressed with the BCL's
/// <see cref="ZLibStream"/>. Each chunk carries a correct CRC32 so the files
/// are valid for any compliant reader.
/// </summary>
internal class PngImageWriter : IImageWriter
{
    private static readonly byte[] Signature =
        { 137, 80, 78, 71, 13, 10, 26, 10 };

    public void Save(
        FrameBuffer frameBuffer,
        string fileName,
        ImageWriteSettings? settings = null)
    {
        if (frameBuffer.PixelFormat != PixelFormat.RGBA32)
        {
            throw new NotImplementedException(
                $"Pixel format '{frameBuffer.PixelFormat}' not supported.");
        }

        int width = frameBuffer.Width;
        int height = frameBuffer.Height;

        //
        // Raw image: per row a filter byte (0 = None) then RGBA pixels.
        // PNG is top-down, like the frame buffer, so no row flip is needed.
        //

        byte[] raw = new byte[height * (1 + width * 4)];
        uint[] pixels = frameBuffer.Pixels;

        int p = 0;

        for (int y = 0; y < height; y++)
        {
            raw[p++] = 0; // filter: None

            int rowOffset = y * frameBuffer.Stride;

            for (int x = 0; x < width; x++)
            {
                uint pixel = pixels[rowOffset + x];

                raw[p++] = (byte)((pixel >> 16) & 0xFF); // R
                raw[p++] = (byte)((pixel >> 8) & 0xFF);  // G
                raw[p++] = (byte)(pixel & 0xFF);         // B
                raw[p++] = (byte)((pixel >> 24) & 0xFF); // A
            }
        }

        //
        // Compress (zlib-wrapped DEFLATE).
        //

        byte[] compressed;

        using (MemoryStream buffer = new MemoryStream())
        {
            using (ZLibStream deflate =
                new ZLibStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
            {
                deflate.Write(raw, 0, raw.Length);
            }

            compressed = buffer.ToArray();
        }

        //
        // Write the file.
        //

        using FileStream stream = new FileStream(
            fileName, FileMode.Create, FileAccess.Write, FileShare.None);

        stream.Write(Signature, 0, Signature.Length);

        // IHDR
        byte[] ihdr = new byte[13];
        WriteBigEndian(ihdr, 0, (uint)width);
        WriteBigEndian(ihdr, 4, (uint)height);
        ihdr[8] = 8;   // bit depth
        ihdr[9] = 6;   // color type: RGBA
        ihdr[10] = 0;  // compression: zlib/DEFLATE
        ihdr[11] = 0;  // filter method: adaptive
        ihdr[12] = 0;  // interlace: none

        WriteChunk(stream, "IHDR", ihdr);
        WriteChunk(stream, "IDAT", compressed);
        WriteChunk(stream, "IEND", Array.Empty<byte>());
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);

        WriteUInt32BE(stream, (uint)data.Length);

        stream.Write(typeBytes, 0, typeBytes.Length);
        stream.Write(data, 0, data.Length);

        uint crc = Crc32.Compute(typeBytes, data);
        WriteUInt32BE(stream, crc);
    }

    private static void WriteUInt32BE(Stream stream, uint value)
    {
        stream.WriteByte((byte)(value >> 24));
        stream.WriteByte((byte)(value >> 16));
        stream.WriteByte((byte)(value >> 8));
        stream.WriteByte((byte)value);
    }

    private static void WriteBigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset + 0] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    private static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            uint[] table = new uint[256];

            for (uint n = 0; n < 256; n++)
            {
                uint c = n;

                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0
                        ? 0xEDB88320u ^ (c >> 1)
                        : c >> 1;
                }

                table[n] = c;
            }

            return table;
        }

        public static uint Compute(byte[] type, byte[] data)
        {
            uint crc = 0xFFFFFFFFu;

            crc = Update(crc, type);
            crc = Update(crc, data);

            return crc ^ 0xFFFFFFFFu;
        }

        private static uint Update(uint crc, byte[] bytes)
        {
            for (int i = 0; i < bytes.Length; i++)
                crc = Table[(crc ^ bytes[i]) & 0xFF] ^ (crc >> 8);

            return crc;
        }
    }
}
