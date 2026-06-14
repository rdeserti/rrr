using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace rrr.Core
{
    /// <summary>
    /// Pixel format:
    /// AARRGGBB
    /// </summary>
    public static class PixelPacker
    {
        public static uint Pack(ColorRGBA32 color)
        {
            return ((uint)color.A << 24)
                 | ((uint)color.R << 16)
                 | ((uint)color.G << 8)
                 | color.B;
        }

        public static ColorRGBA32 Unpack(uint pixel)
        {
            return new ColorRGBA32(
                (byte)((pixel >> 16) & 0xFF),
                (byte)((pixel >> 8) & 0xFF),
                (byte)(pixel & 0xFF),
                (byte)((pixel >> 24) & 0xFF)
            );
        }

        public static string ToHex(uint pixel)
        {
            return $"0x{pixel:X8}";
        }
    }
}
