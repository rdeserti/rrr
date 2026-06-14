using System.IO;
using rrr.Core;

namespace rrr.Imaging.Writers;

internal class BmpImageWriter : IImageWriter
{
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

        const int fileHeaderSize = 14;
        const int dibHeaderSize = 40;

        int pixelDataSize = width * height * 4;

        int fileSize =
            fileHeaderSize +
            dibHeaderSize +
            pixelDataSize;

        using FileStream stream = new FileStream(
            fileName,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);

        using BinaryWriter writer = new BinaryWriter(stream);

        //
        // BITMAP FILE HEADER
        //

        writer.Write((byte)'B');
        writer.Write((byte)'M');

        writer.Write(fileSize);

        writer.Write((short)0);
        writer.Write((short)0);

        writer.Write(fileHeaderSize + dibHeaderSize);

        //
        // BITMAPINFOHEADER
        //

        writer.Write(dibHeaderSize);

        writer.Write(width);

        //
        // BMP classico:
        // altezza positiva = bottom-up
        //
        writer.Write(height);

        writer.Write((short)1);

        writer.Write((short)32);

        writer.Write(0);

        writer.Write(pixelDataSize);

        writer.Write(0);
        writer.Write(0);

        writer.Write(0);
        writer.Write(0);

        //
        // PIXELS
        //
        // BMP = B G R A
        // FrameBuffer = AARRGGBB
        //

        uint[] pixels = frameBuffer.Pixels;

        for (int y = height - 1; y >= 0; y--)
        {
            int rowOffset = y * frameBuffer.Stride;

            for (int x = 0; x < width; x++)
            {
                uint pixel = pixels[rowOffset + x];

                byte a = (byte)((pixel >> 24) & 0xFF);
                byte r = (byte)((pixel >> 16) & 0xFF);
                byte g = (byte)((pixel >> 8) & 0xFF);
                byte b = (byte)(pixel & 0xFF);

                writer.Write(b);
                writer.Write(g);
                writer.Write(r);
                writer.Write(a);
            }
        }
    }
}