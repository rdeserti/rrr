using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace rrr.Imaging;

internal static class ImageReaderFactory
{
    public static IImageReader Create(
        ImageFormat format)
    {
        switch (format)
        {
            case ImageFormat.Bmp:
                return new Readers.BmpImageReader();

            case ImageFormat.Png:
                return new Readers.PngImageReader();

            case ImageFormat.Jpeg:
                return new Readers.JpegImageReader();

            default:
                throw new NotSupportedException(
                    $"Image format '{format}' not supported.");
        }
    }
}
