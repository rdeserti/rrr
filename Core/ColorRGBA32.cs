using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace rrr.Core
{
    public struct ColorRGBA32
    {
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }
        public byte A { get; set; }

        public ColorRGBA32(byte r, byte g, byte b)
        {
            R = r;
            G = g;
            B = b;
            A = 255;
        }

        public ColorRGBA32(byte r, byte g, byte b, byte a)
        {
            R = r;
            G = g;
            B = b;
            A = a;
        }

        public override string ToString()
        {
            return $"({R}, {G}, {B}, {A})";
        }

        public static ColorRGBA32 FromRGBAf(ColorRGBAf color)
        {
            return new ColorRGBA32(
                FloatToByte(color.R),
                FloatToByte(color.G),
                FloatToByte(color.B),
                FloatToByte(color.A));
        }

        private static byte FloatToByte(float value)
        {
            value = Math.Clamp(value, 0.0f, 1.0f);

            return (byte)Math.Round(value * 255.0f);
        }

        public static ColorRGBA32 Black => new(0, 0, 0);
        public static ColorRGBA32 White => new(255, 255, 255);

        public static ColorRGBA32 Red => new(255, 0, 0);
        public static ColorRGBA32 Green => new(0, 255, 0);
        public static ColorRGBA32 Blue => new(0, 0, 255);

        public static ColorRGBA32 Cyan => new(0, 255, 255);
        public static ColorRGBA32 Magenta => new(255, 0, 255);
        public static ColorRGBA32 Yellow => new(255, 255, 0);
    }
}
