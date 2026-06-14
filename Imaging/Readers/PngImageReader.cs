using rrr.Core;
using System;
using System.IO;
using System.IO.Compression;

namespace rrr.Imaging.Readers;

/// <summary>
/// Minimal PNG decoder. Supports non-interlaced 8-bit grayscale, gray+alpha,
/// RGB and RGBA, plus palette images at 1/2/4/8 bits (with optional tRNS
/// alpha). 16-bit channels and interlaced PNGs are rejected. The zlib/DEFLATE
/// stream is inflated with the BCL's <see cref="ZLibStream"/>. Everything is
/// converted to RGBA32.
/// </summary>
internal class PngImageReader : IImageReader
{
    private static readonly byte[] PngSignature =
        { 137, 80, 78, 71, 13, 10, 26, 10 };

    public FrameBuffer Load(Stream stream)
    {
        using MemoryStream memory = new MemoryStream();
        stream.CopyTo(memory);
        byte[] data = memory.ToArray();

        if (data.Length < 8)
            throw new InvalidDataException("File too small to be a PNG.");

        for (int i = 0; i < 8; i++)
        {
            if (data[i] != PngSignature[i])
                throw new InvalidDataException("Not a PNG file (bad signature).");
        }

        int pos = 8;

        int width = 0;
        int height = 0;
        int bitDepth = 0;
        int colorType = 0;
        int interlace = 0;

        byte[]? palette = null;
        byte[]? transparency = null;

        using MemoryStream idat = new MemoryStream();

        while (pos + 8 <= data.Length)
        {
            int length = ReadBigEndian(data, pos);
            pos += 4;

            string type = ChunkType(data, pos);
            pos += 4;

            int dataStart = pos;

            switch (type)
            {
                case "IHDR":
                    width = ReadBigEndian(data, dataStart);
                    height = ReadBigEndian(data, dataStart + 4);
                    bitDepth = data[dataStart + 8];
                    colorType = data[dataStart + 9];
                    // compression (10) and filter (11) methods are always 0.
                    interlace = data[dataStart + 12];
                    break;

                case "PLTE":
                    palette = new byte[length];
                    Array.Copy(data, dataStart, palette, 0, length);
                    break;

                case "tRNS":
                    transparency = new byte[length];
                    Array.Copy(data, dataStart, transparency, 0, length);
                    break;

                case "IDAT":
                    idat.Write(data, dataStart, length);
                    break;
            }

            // Advance past the chunk data and its 4-byte CRC.
            pos = dataStart + length + 4;

            if (type == "IEND")
                break;
        }

        //
        // Validate against the supported subset.
        //

        if (width <= 0 || height <= 0)
            throw new InvalidDataException("Invalid PNG dimensions.");

        if (interlace != 0)
            throw new NotSupportedException("Interlaced PNG is not supported.");

        if (bitDepth == 16)
            throw new NotSupportedException("16-bit PNG is not supported.");

        int channels = ChannelCount(colorType);

        if (colorType == 3)
        {
            if (bitDepth != 1 && bitDepth != 2 && bitDepth != 4 && bitDepth != 8)
                throw new NotSupportedException(
                    $"Unsupported palette bit depth {bitDepth}.");

            if (palette == null)
                throw new InvalidDataException("Palette PNG without PLTE chunk.");
        }
        else if (bitDepth != 8)
        {
            throw new NotSupportedException(
                $"Unsupported PNG bit depth {bitDepth} for color type {colorType}.");
        }

        //
        // Inflate the concatenated IDAT data (zlib-wrapped DEFLATE).
        //

        int bitsPerPixel = channels * bitDepth;
        int scanlineBytes = (width * bitsPerPixel + 7) / 8;
        int filterBpp = Math.Max(1, bitsPerPixel / 8);
        int rawLength = height * (1 + scanlineBytes);

        byte[] raw = new byte[rawLength];

        idat.Position = 0;

        using (ZLibStream inflate =
            new ZLibStream(idat, CompressionMode.Decompress))
        {
            ReadExactly(inflate, raw, rawLength);
        }

        //
        // Un-filter scanlines and convert to RGBA32.
        //

        FrameBuffer frameBuffer =
            new FrameBuffer(width, height, PixelFormat.RGBA32);

        byte[] current = new byte[scanlineBytes];
        byte[] previous = new byte[scanlineBytes]; // zero-filled for row 0

        int p = 0;

        for (int y = 0; y < height; y++)
        {
            int filter = raw[p++];

            Buffer.BlockCopy(raw, p, current, 0, scanlineBytes);
            p += scanlineBytes;

            Unfilter(filter, current, previous, filterBpp);

            DecodeRow(
                current, frameBuffer, y, width,
                colorType, bitDepth, palette, transparency);

            // The just-decoded row becomes the predictor for the next.
            (previous, current) = (current, previous);
        }

        return frameBuffer;
    }

    private static int ChannelCount(int colorType)
    {
        switch (colorType)
        {
            case 0: return 1; // grayscale
            case 2: return 3; // RGB
            case 3: return 1; // palette index
            case 4: return 2; // gray + alpha
            case 6: return 4; // RGBA
            default:
                throw new NotSupportedException(
                    $"Unsupported PNG color type {colorType}.");
        }
    }

    private static void DecodeRow(
        byte[] row,
        FrameBuffer frameBuffer,
        int y,
        int width,
        int colorType,
        int bitDepth,
        byte[]? palette,
        byte[]? transparency)
    {
        for (int x = 0; x < width; x++)
        {
            byte r, g, b, a;

            switch (colorType)
            {
                case 0: // grayscale
                    {
                        byte v = row[x];
                        r = g = b = v;
                        a = 255;
                        break;
                    }

                case 2: // RGB
                    {
                        int i = x * 3;
                        r = row[i];
                        g = row[i + 1];
                        b = row[i + 2];
                        a = 255;
                        break;
                    }

                case 4: // gray + alpha
                    {
                        int i = x * 2;
                        r = g = b = row[i];
                        a = row[i + 1];
                        break;
                    }

                case 6: // RGBA
                    {
                        int i = x * 4;
                        r = row[i];
                        g = row[i + 1];
                        b = row[i + 2];
                        a = row[i + 3];
                        break;
                    }

                case 3: // palette
                    {
                        int index = PaletteIndex(row, x, bitDepth);
                        int pi = index * 3;

                        r = palette![pi];
                        g = palette[pi + 1];
                        b = palette[pi + 2];

                        a = transparency != null && index < transparency.Length
                            ? transparency[index]
                            : (byte)255;
                        break;
                    }

                default:
                    throw new NotSupportedException(
                        $"Unsupported PNG color type {colorType}.");
            }

            frameBuffer.SetPixelUnsafe(
                x, y,
                PixelPacker.Pack(new ColorRGBA32(r, g, b, a)));
        }
    }

    private static int PaletteIndex(byte[] row, int x, int bitDepth)
    {
        if (bitDepth == 8)
            return row[x];

        // Sub-byte indices are packed MSB-first.
        int bitPos = x * bitDepth;
        int value = row[bitPos >> 3];
        int shift = 8 - bitDepth - (bitPos & 7);

        return (value >> shift) & ((1 << bitDepth) - 1);
    }

    private static void Unfilter(int filter, byte[] cur, byte[] prev, int bpp)
    {
        int len = cur.Length;

        switch (filter)
        {
            case 0: // None
                break;

            case 1: // Sub
                for (int i = bpp; i < len; i++)
                    cur[i] = (byte)(cur[i] + cur[i - bpp]);
                break;

            case 2: // Up
                for (int i = 0; i < len; i++)
                    cur[i] = (byte)(cur[i] + prev[i]);
                break;

            case 3: // Average
                for (int i = 0; i < len; i++)
                {
                    int a = i >= bpp ? cur[i - bpp] : 0;
                    int b = prev[i];
                    cur[i] = (byte)(cur[i] + ((a + b) >> 1));
                }
                break;

            case 4: // Paeth
                for (int i = 0; i < len; i++)
                {
                    int a = i >= bpp ? cur[i - bpp] : 0;
                    int b = prev[i];
                    int c = i >= bpp ? prev[i - bpp] : 0;
                    cur[i] = (byte)(cur[i] + Paeth(a, b, c));
                }
                break;

            default:
                throw new InvalidDataException(
                    $"Unknown PNG filter type {filter}.");
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;

        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);

        if (pa <= pb && pa <= pc)
            return a;

        return pb <= pc ? b : c;
    }

    private static int ReadBigEndian(byte[] data, int offset)
    {
        return (data[offset] << 24)
             | (data[offset + 1] << 16)
             | (data[offset + 2] << 8)
             | data[offset + 3];
    }

    private static string ChunkType(byte[] data, int offset)
    {
        return new string(new[]
        {
            (char)data[offset],
            (char)data[offset + 1],
            (char)data[offset + 2],
            (char)data[offset + 3]
        });
    }

    private static void ReadExactly(Stream stream, byte[] buffer, int count)
    {
        int read = 0;

        while (read < count)
        {
            int n = stream.Read(buffer, read, count - read);

            if (n <= 0)
                throw new EndOfStreamException(
                    "Unexpected end of PNG image data.");

            read += n;
        }
    }
}
