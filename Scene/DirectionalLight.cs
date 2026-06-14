using rrr.Core;
using rrr.VMath;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace rrr.Scene
{
    public class DirectionalLight
        : Light
    {
        public Vector3f Direction;

        public ColorRGBAf Color =
            ColorRGBAf.White;

        public float Intensity = 1.0f;
    }
}
