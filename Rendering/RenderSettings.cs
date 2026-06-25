using rrr.Samples;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace rrr.Rendering
{
    public class RenderSettings
    {
        public ShadingMode ShadingMode { get; set; } =
            ShadingMode.Flat;

        public int Parallel { get; set; } = 0;

        public bool BackfaceCulling { get; set; } =
            true;

        /// <summary>Render and apply shadow maps for shadow-casting lights.</summary>
        public bool ShadowsEnabled { get; set; } = true;

        /// <summary>Square resolution of each light's shadow map.</summary>
        public int ShadowMapResolution { get; set; } = 1024;

        /// <summary>
        /// PCF kernel radius in texels for soft shadow edges. 0 = hard
        /// shadows (single sample); 1 = 3x3, 2 = 5x5, etc.
        /// </summary>
        public int ShadowPcfRadius { get; set; } = 1;

        public static RenderSettings WireFrame()
        {
            return new RenderSettings
            {
                ShadingMode = ShadingMode.Wireframe,
                BackfaceCulling = false
            };

        }

        public static RenderSettings Flat()
        {
            return new RenderSettings
            {
                ShadingMode = ShadingMode.Flat,
                BackfaceCulling = true
            };

        }

        public static RenderSettings Gouraud()
        {
            return new RenderSettings
            {
                ShadingMode = ShadingMode.Gouraud,
                BackfaceCulling = true
            };

        }

        public static RenderSettings Phong()
        {
            return new RenderSettings
            {
                ShadingMode = ShadingMode.Phong,
                BackfaceCulling = true
            };

        }
    }
}
