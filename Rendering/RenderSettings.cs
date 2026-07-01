using rrr.Samples;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace rrr.Rendering
{
    /// <summary>Which rendering backend produces the image.</summary>
    public enum RenderEngine
    {
        /// <summary>Triangle rasterizer (the default, shadow-map based).</summary>
        Raster,

        /// <summary>Ray tracer (shadow rays + reflections).</summary>
        Raytrace
    }

    public class RenderSettings
    {
        /// <summary>Selects the rasterizer or the ray tracer backend.</summary>
        public RenderEngine Engine { get; set; } =
            RenderEngine.Raster;

        public ShadingMode ShadingMode { get; set; } =
            ShadingMode.Flat;

        public int Parallel { get; set; } = 0;

        //
        // Ray-tracer knobs (ignored by the rasterizer).
        //

        /// <summary>Max recursion depth for reflection (and later refraction) rays.</summary>
        public int MaxBounces { get; set; } = 4;

        /// <summary>Cast shadow rays in the ray tracer (per-light gated by Light.CastsShadows).</summary>
        public bool RayShadows { get; set; } = true;

        /// <summary>Supersampling factor per axis (1 = off, 2 = 2x2 = 4 samples/pixel).</summary>
        public int Supersampling { get; set; } = 1;

        /// <summary>
        /// Debug: output the per-light shadow factor as grayscale (1 = lit,
        /// 0 = shadowed) instead of shaded color. Works on both engines, so the
        /// raster shadow map and ray-traced shadows can be compared directly.
        /// </summary>
        public bool DebugShadow { get; set; } = false;

        /// <summary>
        /// Gamma — input linearization: decode color inputs (textures, flat
        /// colors, background) sRGB-&gt;linear so lighting is done in linear space.
        /// On by default; turn off to feed colors to the lighting math unchanged.
        /// </summary>
        public bool LinearizeInput { get; set; } = true;

        /// <summary>
        /// Gamma — output sRGB reconstruction: encode the final image
        /// linear-&gt;sRGB. On by default; turn off to write the (linear) buffer
        /// as-is. Independent of <see cref="LinearizeInput"/>. Both engines.
        /// </summary>
        public bool EncodeSrgb { get; set; } = true;

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

        /// <summary>
        /// Cull front faces when rendering shadow maps (renders back faces
        /// only), pushing self-shadow acne behind the geometry. May remove
        /// shadows from single-sided/thin meshes; turn off if that happens.
        /// </summary>
        public bool ShadowFrontFaceCull { get; set; } = true;

        /// <summary>
        /// Soft-shadow light size. Rasterizer: PCSS apparent light radius in
        /// shadow-map texels. Ray tracer: area-light radius in <b>world units</b>
        /// (point/spot) or angular spread (directional). 0 = hard shadows on
        /// both engines.
        /// </summary>
        public float ShadowSoftness { get; set; } = 0.0f;

        /// <summary>
        /// Ray tracer only: number of shadow rays per light when
        /// <see cref="ShadowSoftness"/> &gt; 0 (area-light sampling). 1 (or
        /// softness 0) means a single hard shadow ray.
        /// </summary>
        public int ShadowSamples { get; set; } = 16;

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
