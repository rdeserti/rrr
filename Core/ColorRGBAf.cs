using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace rrr.Core
{
    public struct ColorRGBAf
    {
        public float R { get; set; }
        public float G { get; set; }
        public float B { get; set; }
        public float A { get; set; }

        public ColorRGBAf(float r, float g, float b)
        {
            R = r;
            G = g;
            B = b;
            A = 1.0f;
        }

        public ColorRGBAf(float r, float g, float b, float a)
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

        public static ColorRGBAf FromRGBA32(ColorRGBA32 colorRGBA32)
        {
            return new ColorRGBAf(colorRGBA32.R / 255f,
                               colorRGBA32.G / 255f,
                               colorRGBA32.B / 255f,
                               colorRGBA32.A / 255f);
        }

        private static ColorRGBAf RandomPastel(
            Random random)
        {
            return new ColorRGBAf(
                0.4f + random.NextSingle() * 0.6f,
                0.4f + random.NextSingle() * 0.6f,
                0.4f + random.NextSingle() * 0.6f);
        }

        public static ColorRGBAf Black => new(0.0f, 0.0f, 0.0f);
        public static ColorRGBAf White => new(1.0f, 1.0f, 1.0f);

        public static ColorRGBAf Red => new(1.0f, 0.0f, 0.0f);
        public static ColorRGBAf Green => new(0.0f, 1.0f, 0.0f);
        public static ColorRGBAf Blue => new(0.0f, 0.0f, 1.0f);

        public static ColorRGBAf Cyan => new(0.0f, 1.0f, 1.0f);
        public static ColorRGBAf Magenta => new(1.0f, 0.0f, 1.0f);
        public static ColorRGBAf Yellow => new(1.0f, 1.0f, 0.0f);
    }
}
