using rrr.Core;
using rrr.VMath;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace rrr.Scene
{
    public class PointLight
        : Light
    {
        public Vector3f Position;

        public ColorRGBAf Color =
            ColorRGBAf.White;

        public float Intensity = 1.0f;

        public PointLight(Vector3f position, ColorRGBAf color, float intensity)
        {
            Position = position;
            Color = color;
            Intensity = intensity;
        }

        public static PointLight CreateComfyLight(
            Vector3f min,
            Vector3f max)
        {
            Vector3f center =
        (min + max) * 0.5f;

            Vector3f size =
                max - min;

            return new PointLight(center - size*2, ColorRGBAf.White, 1.0f);

        }
    }
}
