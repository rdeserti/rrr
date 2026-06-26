using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using rrr.Core;

namespace rrr.Imaging;

public static class ImageReader
{
    public static FrameBuffer Load(
        string fileName)
    {
        var format =
            GetFormatFromFileName(fileName);

        var reader =
            ImageReaderFactory.Create(format);

        return reader.Load(fileName);
    }

    /// <summary>
    /// Decodes an image already in memory, detecting the format from its magic
    /// bytes. Used for textures embedded in container formats (e.g. glTF
    /// bufferView / data-URI images) where there is no file extension.
    /// </summary>
    public static FrameBuffer Load(byte[] data)
    {
        ImageFormat format = DetectFormat(data);

        IImageReader reader = ImageReaderFactory.Create(format);

        using MemoryStream stream = new MemoryStream(data, writable: false);
        return reader.Load(stream);
    }

    /// <summary>
    /// Sniffs the image format from the leading bytes (PNG signature, JPEG
    /// SOI, BMP 'BM'). Throws if none match.
    /// </summary>
    public static ImageFormat DetectFormat(byte[] data)
    {
        if (data.Length >= 8 &&
            data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
            data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A)
            return ImageFormat.Png;

        if (data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8)
            return ImageFormat.Jpeg;

        if (data.Length >= 2 && data[0] == 0x42 && data[1] == 0x4D) // 'BM'
            return ImageFormat.Bmp;

        throw new NotSupportedException(
            "Unrecognized image data (not PNG, JPEG or BMP).");
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
