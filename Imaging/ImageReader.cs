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
