using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using rrr.Core;
using rrr.Scene;
using rrr.VMath;

namespace rrr.Rendering.Raytracing;

/// <summary>
/// Ray-tracing rendering backend. Consumes the same <see cref="Scene.Scene"/>
/// (camera, lights, materials, textures) as the rasterizer and writes the same
/// <see cref="FrameBuffer"/>, so it slots into the existing render funnel
/// (profiler, stats and image output stay shared).
///
/// P5 (current): recursive Whitted ray tracing. Each primary ray is traced,
/// shaded through the shared <see cref="SurfaceShading"/> + shadow rays (P4),
/// and — for glossy materials — mixed with a recursively traced reflection ray
/// up to <c>RenderSettings.MaxBounces</c>. Reflectivity is derived from the
/// material's <see cref="Material.SpecularColor"/> and <see cref="Material.Shininess"/>
/// (no new Scene field): a gloss factor that ramps with shininess gates the
/// reflection, so matte materials (shininess below threshold) get <b>zero</b>
/// reflection and stay comparable to the raster. Reflection rays that miss
/// return the background. See docs/RAYTRACER_ROADMAP.md.
/// </summary>
public static class RayTracer
{
    // Shininess range over which a material ramps from matte (no reflection) to
    // a sharp mirror. Below the min, gloss is 0 -> identical to the rasterizer.
    private const float ReflMinShininess = 24.0f;
    private const float ReflMaxShininess = 160.0f;

    // Max alpha-mask cutout fragments a single ray may pass through before giving
    // up (prevents runaway loops on degenerate/overlapping geometry).
    private const int MaxTransparencySteps = 64;

    private readonly struct TraceContext
    {
        public readonly RayScene Scene;
        public readonly IReadOnlyList<Light> Lights;
        public readonly Vector3f CameraPosition;
        public readonly RayShadowVisibility Visibility;
        public readonly ColorRGBAf Background;
        public readonly int MaxBounces;
        public readonly float Bias;

        public TraceContext(
            RayScene scene, IReadOnlyList<Light> lights, Vector3f cameraPosition,
            RayShadowVisibility visibility, ColorRGBAf background, int maxBounces, float bias)
        {
            Scene = scene;
            Lights = lights;
            CameraPosition = cameraPosition;
            Visibility = visibility;
            Background = background;
            MaxBounces = maxBounces;
            Bias = bias;
        }
    }

    public static void Render(
        FrameBuffer framebuffer,
        Scene.Scene scene,
        RenderSettings settings)
    {
        RayScene rayScene = RayScene.Build(scene);
        RayCamera camera = RayCamera.Create(
            scene.Camera, framebuffer.Width, framebuffer.Height);

        IReadOnlyList<Light> lights = scene.Lights;
        Vector3f cameraPosition = scene.Camera.Position;

        // Background was cleared into every pixel by the funnel; capture it for
        // primary- and reflection-ray misses before we overwrite anything.
        ColorRGBAf background =
            ColorRGBAf.FromRGBA32(PixelPacker.Unpack(framebuffer.Pixels[0]));

        // Shadow-ray visibility: normal-offset bias scaled to the scene size.
        float diagonal = (rayScene.Bounds.Max - rayScene.Bounds.Min).Length();
        float bias = MathF.Max(1e-4f, diagonal * 2e-4f);
        RayShadowVisibility visibility =
            new RayShadowVisibility(rayScene, lights, bias,
                settings.ShadowsEnabled && settings.RayShadows,
                settings.ShadowSoftness, settings.ShadowSamples);

        TraceContext ctx = new TraceContext(
            rayScene, lights, cameraPosition, visibility, background,
            Math.Max(0, settings.MaxBounces), bias);

        rrr.App.Log.Debug(
            $"Raytrace P5: {framebuffer.Width}x{framebuffer.Height}, " +
            $"{rayScene.Triangles.Count} triangles, BVH {rayScene.BvhNodeCount} nodes, " +
            $"shadows={(settings.ShadowsEnabled && settings.RayShadows)}, bounces={ctx.MaxBounces}.");

        int width = framebuffer.Width;
        int height = framebuffer.Height;

        // Disjoint rows -> no synchronization (same philosophy as the raster bands).
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                Ray ray = camera.GetRay(x, y);
                ColorRGBAf color = Trace(in ctx, in ray, 0);

                framebuffer.SetPixel(x, y,
                    PixelPacker.Pack(ColorRGBA32.FromRGBAf(color)));
            }
        });
    }

    /// <summary>
    /// Traces one ray and returns its radiance: background on a miss, otherwise
    /// the shaded surface color mixed with a recursively traced reflection for
    /// glossy materials.
    /// </summary>
    private static ColorRGBAf Trace(in TraceContext ctx, in Ray ray, int depth)
    {
        // Find the nearest visible surface, passing through alpha-tested cutout
        // fragments (Mask, sentinel alpha < 0) without consuming a bounce. The
        // ray direction never changes, so the reflection/refraction code below
        // still uses ray.Direction; only the hit and its position advance.
        RayHit hit;
        ColorRGBAf local;
        Vector3f shadingNormal = default;
        Ray cur = ray;

        for (int step = 0; ; step++)
        {
            if (!ctx.Scene.Intersect(in cur, float.MaxValue, out hit))
                return ctx.Background;

            local = Shade(in hit, ctx.CameraPosition, ctx.Lights, in ctx.Visibility,
                out shadingNormal);

            if (local.A >= 0.0f)
                break; // visible fragment (opaque, or alpha-blend handled below)

            if (step >= MaxTransparencySteps)
                return ctx.Background;

            // Masked-out: continue the ray just past this hit.
            cur = new Ray(hit.Position + cur.Direction * ctx.Bias, cur.Direction);
        }

        // Refraction: transmissive materials reflect + refract via Fresnel
        // (a dielectric like glass). ior = 1 gives straight-through transparency.
        float transmission = hit.Material?.Transmission ?? 0.0f;

        if (transmission > 0.0f && depth < ctx.MaxBounces)
            return Dielectric(in ctx, in ray, in hit, shadingNormal, local, transmission, depth);

        // Alpha-blend transparency: composite the lit surface over what is behind
        // it (src-over). The continuation ray resolves the background recursively,
        // so stacked transparents composite front-to-back correctly.
        if ((hit.Material?.AlphaMode ?? AlphaMode.Opaque) == AlphaMode.Blend
            && local.A < 1.0f && depth < ctx.MaxBounces)
        {
            Ray behindRay = new Ray(hit.Position + ray.Direction * ctx.Bias, ray.Direction);
            ColorRGBAf behind = Trace(in ctx, in behindRay, depth + 1);

            float a = local.A;
            float ia = 1.0f - a;
            return new ColorRGBAf(
                local.R * a + behind.R * ia,
                local.G * a + behind.G * ia,
                local.B * a + behind.B * ia,
                1.0f);
        }

        float gloss = Gloss(hit.Material);

        if (depth >= ctx.MaxBounces || gloss <= 0.0f)
            return local;

        // Shading normal (perturbed by the normal/height map, so a bumpy mirror
        // reflects its detail), flipped to face the viewer for a stable reflection.
        Vector3f n = shadingNormal;
        if (Vector3f.Dot(n, ray.Direction) > 0.0f)
            n = -n;

        Vector3f reflectDir = Vector3f.Reflect(ray.Direction, n).Normalized();
        Ray reflectRay = new Ray(hit.Position + n * ctx.Bias, reflectDir);

        ColorRGBAf reflected = Trace(in ctx, in reflectRay, depth + 1);

        // Fresnel-Schlick reflectance, F0 = specular color, gated by gloss so
        // matte surfaces never reflect (parity with the raster).
        ColorRGBAf spec = hit.Material!.SpecularColor;
        float cosTheta = MathF.Max(0.0f, Vector3f.Dot(n, -ray.Direction));
        float fresnel = MathF.Pow(1.0f - cosTheta, 5.0f);

        float krR = gloss * (spec.R + (1.0f - spec.R) * fresnel);
        float krG = gloss * (spec.G + (1.0f - spec.G) * fresnel);
        float krB = gloss * (spec.B + (1.0f - spec.B) * fresnel);

        return new ColorRGBAf(
            local.R * (1.0f - krR) + reflected.R * krR,
            local.G * (1.0f - krG) + reflected.G * krG,
            local.B * (1.0f - krB) + reflected.B * krB,
            1.0f);
    }

    /// <summary>
    /// Dielectric (glass) shading: splits the incident ray into a reflected and
    /// a refracted ray by Fresnel reflectance (Schlick), handling total internal
    /// reflection, then blends the result with the opaque local shading by the
    /// transmission factor. Refraction follows Snell's law with the material's
    /// index of refraction (ior = 1 ⇒ no bending = plain transparency).
    /// </summary>
    private static ColorRGBAf Dielectric(
        in TraceContext ctx, in Ray ray, in RayHit hit, Vector3f normal,
        ColorRGBAf local, float transmission, int depth)
    {
        Vector3f incident = ray.Direction;
        float ior = hit.Material!.IndexOfRefraction;

        float cosi = Vector3f.Dot(incident, normal);
        float etai = 1.0f, etat = ior;
        Vector3f n = normal;                   // oriented to face the incoming side

        if (cosi < 0.0f)
        {
            cosi = -cosi;                      // entering the surface
        }
        else
        {
            n = -normal;                       // exiting: flip normal, swap media
            (etai, etat) = (etat, etai);
        }

        float eta = etai / etat;
        float k = 1.0f - eta * eta * (1.0f - cosi * cosi);

        // Fresnel (Schlick) reflectance from the air/material indices.
        float r0 = (etai - etat) / (etai + etat);
        r0 *= r0;
        float fresnel = r0 + (1.0f - r0) * MathF.Pow(1.0f - cosi, 5.0f);

        // Reflected ray about the viewer-facing normal.
        Vector3f reflectDir = Vector3f.Reflect(incident, n).Normalized();
        Ray reflectRay = new Ray(hit.Position + n * ctx.Bias, reflectDir);
        ColorRGBAf reflected = Trace(in ctx, in reflectRay, depth + 1);

        ColorRGBAf glass;

        if (k < 0.0f)
        {
            // Total internal reflection: no refracted ray.
            glass = reflected;
        }
        else
        {
            Vector3f refractDir =
                (incident * eta + n * (eta * cosi - MathF.Sqrt(k))).Normalized();
            Ray refractRay = new Ray(hit.Position - n * ctx.Bias, refractDir);
            ColorRGBAf refracted = Trace(in ctx, in refractRay, depth + 1);

            float ft = 1.0f - fresnel;
            glass = new ColorRGBAf(
                reflected.R * fresnel + refracted.R * ft,
                reflected.G * fresnel + refracted.G * ft,
                reflected.B * fresnel + refracted.B * ft,
                1.0f);
        }

        // Blend opaque local shading with the glass term by transmission.
        return new ColorRGBAf(
            local.R * (1.0f - transmission) + glass.R * transmission,
            local.G * (1.0f - transmission) + glass.G * transmission,
            local.B * (1.0f - transmission) + glass.B * transmission,
            1.0f);
    }

    /// <summary>
    /// Reflection strength in [0,1] derived from shininess: 0 below
    /// <see cref="ReflMinShininess"/> (matte, no reflection), ramping to 1 at
    /// <see cref="ReflMaxShininess"/> (sharp mirror).
    /// </summary>
    private static float Gloss(Material? m)
    {
        if (m == null || m.Shininess <= 0.0f)
            return 0.0f;

        return Smoothstep(ReflMinShininess, ReflMaxShininess, m.Shininess);
    }

    private static float Smoothstep(float edge0, float edge1, float x)
    {
        if (edge0 == edge1)
            return x >= edge1 ? 1.0f : 0.0f;

        float t = MathF.Max(0.0f, MathF.Min(1.0f, (x - edge0) / (edge1 - edge0)));
        return t * t * (3.0f - 2.0f * t);
    }

    /// <summary>
    /// Shades a surface hit via the shared <see cref="SurfaceShading"/> helper,
    /// resolving material colors/textures the same way the rasterizer's
    /// <c>MakeTexture</c> does, with shadow-ray visibility.
    /// </summary>
    private static ColorRGBAf Shade(
        in RayHit hit, Vector3f cameraPosition, IReadOnlyList<Light> lights,
        in RayShadowVisibility visibility, out Vector3f shadingNormal)
    {
        Material? m = hit.Material;

        ColorRGBAf tint = m?.DiffuseColor ?? ColorRGBAf.White;
        ColorRGBAf specularColor = m?.SpecularColor ?? ColorRGBAf.White;
        float shininess = m?.Shininess ?? 0.0f;
        ColorRGBAf emissiveColor = m?.EmissiveColor ?? ColorRGBAf.Black;

        float alphaCutoff =
            (m?.AlphaMode ?? AlphaMode.Opaque) == AlphaMode.Mask
                ? (m?.AlphaCutoff ?? 0.5f)
                : -1.0f;

        return SurfaceShading.Shade(
            hit.Position, hit.Normal, hit.Tangent, hit.Handedness, hit.UV, hit.Color,
            cameraPosition, lights,
            in visibility,
            m?.DiffuseTexture, m?.SpecularTexture, m?.EmissiveTexture, m?.NormalTexture,
            m?.NormalIsHeightMap ?? false, m?.InvertNormalGreen ?? false, m?.BumpScale ?? 1.0f,
            tint, specularColor, shininess, emissiveColor,
            m?.OcclusionTexture, alphaCutoff,
            m?.DoubleSided ?? false, m?.Unlit ?? false,
            out shadingNormal);
    }
}
