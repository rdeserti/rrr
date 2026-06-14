using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace rrr.Imaging;

internal static class ImageWriterFactory
{
    public static IImageWriter Create(
        ImageFormat format)
    {
        switch (format)
        {
            case ImageFormat.Bmp:
                return new Writers.BmpImageWriter();

            case ImageFormat.Png:
                return new Writers.PngImageWriter();

            default:
                throw new NotSupportedException(
                    $"Image format '{format}' not supported.");
        }
    }
}
