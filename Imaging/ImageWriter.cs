using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using rrr.Core;

namespace rrr.Imaging;

public static class ImageWriter
{
    public static void Save(
        FrameBuffer frameBuffer,
        string fileName)
    {
        Save(
            frameBuffer,
            fileName,
            null);
    }

    public static void Save(
          FrameBuffer frameBuffer,
          string fileName,
          ImageWriteSettings? settings
        )
    {
        ImageFormat format = ImageWriter.GetFormatFromFileName(fileName);
        Save(frameBuffer, fileName, format, settings);
    }

    public static void Save(
        FrameBuffer frameBuffer,
        string fileName,
        ImageFormat imageFormat,
        ImageWriteSettings? settings)
    {
        var writer =
            ImageWriterFactory.Create(imageFormat);

        writer.Save(
            frameBuffer,
            fileName,
            settings);
    }

    private static ImageFormat GetFormatFromFileName(
        string fileName)
    {
        string extension =
            Path.GetExtension(fileName)
                .ToLowerInvariant();

        switch (extension)
        {
            case ".bmp":
                return ImageFormat.Bmp;

            case ".png":
                return ImageFormat.Png;

            case ".jpg":
            case ".jpeg":
                return ImageFormat.Jpeg;

            default:
                throw new NotSupportedException(
                    $"Image extension '{extension}' is not supported.");
        }
    }
}