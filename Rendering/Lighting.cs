using rrr.Core;
using rrr.Scene;
using rrr.VMath;
using System;
using System.Collections.Generic;

namespace rrr.Rendering;

public static class Lighting
{
    /// <summary>
    /// Ambient term, as a fraction of the base color, applied even
    /// when no light reaches the surface.
    /// </summary>
    private const float Ambient = 0.1f;

    /// <summary>
    /// Debug: when set, <see cref="ShadeCore{TVis}"/> returns the per-light
    /// visibility (shadow factor) as a grayscale value instead of the lit color
    /// — 1 (white) = fully lit, 0 (black) = fully shadowed. Works for both
    /// engines (raster shadow maps and ray-traced shadow rays go through the
    /// same path), so the two can be compared directly. Set from
    /// <c>RenderSettings.DebugShadow</c>.
    /// </summary>
    public static bool DebugShadowVisualize = false;

    /// <summary>
    /// Per-light visibility source. The BRDF is identical across engines; only
    /// how a light's shadow factor (1 = lit, 0 = shadowed) is computed differs:
    /// the rasterizer samples a shadow map, the ray tracer casts a shadow ray.
    /// Implemented by structs so <see cref="ShadeCore{TVis}"/> monomorphizes
    /// with no allocation and no virtual call (same pattern as IPixelShader).
    /// </summary>
    public interface IVisibility
    {
        float Visibility(int lightIndex, Vector3f worldPosition, Vector3f n, Vector3f L);
    }

    /// <summary>
    /// Rasterizer visibility: samples the per-light shadow map (or 1 if there is
    /// none). Reproduces the original in-loop shadow-map sampling exactly.
    /// </summary>
    public readonly struct ShadowMapVisibility : IVisibility
    {
        private readonly IReadOnlyList<ShadowMap?>? _maps;

        public ShadowMapVisibility(IReadOnlyList<ShadowMap?>? maps)
        {
            _maps = maps;
        }

        public float Visibility(int lightIndex, Vector3f worldPosition, Vector3f n, Vector3f L)
        {
            if (_maps != null && lightIndex < _maps.Count)
            {
                ShadowMap? map = _maps[lightIndex];
                if (map != null)
                    return map.Sample(worldPosition, n, L);
            }

            return 1.0f;
        }
    }

    /// <summary>
    /// Computes the lit color at a surface point (diffuse Lambert + ambient +
    /// Blinn-Phong specular + emissive, summed over all lights), sampling shadow
    /// maps for visibility. Backs every rasterizer shading mode (Flat per
    /// triangle, Gouraud per vertex, Phong/Texture per pixel).
    /// </summary>
    public static ColorRGBAf Shade(
        Vector3f worldPosition,
        Vector3f normal,
        Vector3f viewPosition,
        IReadOnlyList<Light> lights,
        ColorRGBAf baseColor,
        ColorRGBAf specularColor,
        float shininess,
        ColorRGBAf emissive,
        IReadOnlyList<ShadowMap?>? shadowMaps = null,
        float occlusion = 1.0f,
        bool twoSided = false,
        bool unlit = false)
    {
        return ShadeCore(
            worldPosition, normal, viewPosition, lights,
            baseColor, specularColor, shininess, emissive,
            occlusion, twoSided, unlit,
            new ShadowMapVisibility(shadowMaps));
    }

    /// <summary>
    /// Same BRDF, with a caller-supplied visibility source (e.g. shadow rays in
    /// the ray tracer). The struct constraint keeps it allocation-free.
    /// </summary>
    public static ColorRGBAf Shade<TVis>(
        Vector3f worldPosition,
        Vector3f normal,
        Vector3f viewPosition,
        IReadOnlyList<Light> lights,
        ColorRGBAf baseColor,
        ColorRGBAf specularColor,
        float shininess,
        ColorRGBAf emissive,
        in TVis visibility,
        float occlusion = 1.0f,
        bool twoSided = false,
        bool unlit = false)
        where TVis : struct, IVisibility
    {
        return ShadeCore(
            worldPosition, normal, viewPosition, lights,
            baseColor, specularColor, shininess, emissive,
            occlusion, twoSided, unlit,
            visibility);
    }

    private static ColorRGBAf ShadeCore<TVis>(
        Vector3f worldPosition,
        Vector3f normal,
        Vector3f viewPosition,
        IReadOnlyList<Light> lights,
        ColorRGBAf baseColor,
        ColorRGBAf specularColor,
        float shininess,
        ColorRGBAf emissive,
        float occlusion,
        bool twoSided,
        bool unlit,
        in TVis visibility)
        where TVis : struct, IVisibility
    {
        // Unlit (KHR_materials_unlit): the base color is the final color.
        if (unlit)
            return new ColorRGBAf(
                MathF.Min(1.0f, baseColor.R),
                MathF.Min(1.0f, baseColor.G),
                MathF.Min(1.0f, baseColor.B),
                baseColor.A);

        Vector3f n =
            normal.Normalized();

        // Two-sided (doubleSided): flip the normal to face the viewer so back
        // faces are lit instead of appearing black.
        if (twoSided)
        {
            Vector3f toView = viewPosition - worldPosition;
            if (Vector3f.Dot(n, toView) < 0.0f)
                n = -n;
        }

        bool hasSpecular =
            shininess > 0.0f;

        // View direction, only needed for the specular term.
        Vector3f V =
            hasSpecular
                ? (viewPosition - worldPosition).Normalized()
                : Vector3f.Zero;

        // Ambient + lit terms accumulate here, then are scaled by ambient
        // occlusion; emissive is added afterwards (AO must not dim emission).
        float r = baseColor.R * Ambient;
        float g = baseColor.G * Ambient;
        float b = baseColor.B * Ambient;

        // Debug: accumulate the per-light shadow factor for visualization.
        float dbgShadowSum = 0.0f;
        int dbgShadowCount = 0;

        if (lights != null)
        {
            for (int li = 0; li < lights.Count; li++)
            {
                Light light = lights[li];

                Vector3f L;
                ColorRGBAf lightColor;
                float intensity;

                switch (light)
                {
                    case PointLight point:
                        L = (point.Position - worldPosition)
                            .Normalized();
                        lightColor = point.Color;
                        intensity = point.Intensity;
                        break;

                    case DirectionalLight directional:
                        // Direction is the way the light travels;
                        // the vector toward the light is its negation.
                        L = (-directional.Direction)
                            .Normalized();
                        lightColor = directional.Color;
                        intensity = directional.Intensity;
                        break;

                    case SpotLight spot:
                        {
                            L = (spot.Position - worldPosition).Normalized();
                            lightColor = spot.Color;

                            // Cone attenuation: angle between the spot axis and
                            // the direction from the light to this fragment.
                            Vector3f axis = spot.Direction.Normalized();
                            float cosAngle = Vector3f.Dot(axis, -L);

                            const float deg = MathF.PI / 180.0f;
                            float cosOuter = MathF.Cos(spot.ConeAngleDegrees * deg);
                            float cosInner = MathF.Cos(spot.InnerAngleDegrees * deg);

                            float cone = Smoothstep(cosOuter, cosInner, cosAngle);

                            if (cone <= 0.0f)
                                continue; // outside the cone

                            intensity = spot.Intensity * cone;
                            break;
                        }

                    default:
                        continue;
                }

                // Shadow factor (1 = lit, 0 = shadowed) from the visibility
                // source (shadow map for the raster, shadow ray for the tracer).
                float shadow = visibility.Visibility(li, worldPosition, n, L);

                dbgShadowSum += shadow;
                dbgShadowCount++;

                float ndotl =
                    MathF.Max(
                        0.0f,
                        Vector3f.Dot(n, L));

                // Diffuse
                float diffuse =
                    ndotl * intensity * shadow;

                r += baseColor.R * lightColor.R * diffuse;
                g += baseColor.G * lightColor.G * diffuse;
                b += baseColor.B * lightColor.B * diffuse;

                // Specular (Blinn-Phong half-vector), only on lit faces.
                if (hasSpecular && ndotl > 0.0f && shadow > 0.0f)
                {
                    Vector3f h =
                        (L + V).Normalized();

                    float ndoth =
                        MathF.Max(
                            0.0f,
                            Vector3f.Dot(n, h));

                    float specular =
                        MathF.Pow(ndoth, shininess) * intensity * shadow;

                    r += specularColor.R * lightColor.R * specular;
                    g += specularColor.G * lightColor.G * specular;
                    b += specularColor.B * lightColor.B * specular;
                }
            }
        }

        // Debug: output the average per-light shadow factor as grayscale.
        if (DebugShadowVisualize)
        {
            float s = dbgShadowCount > 0 ? dbgShadowSum / dbgShadowCount : 1.0f;
            return new ColorRGBAf(s, s, s, 1.0f);
        }

        // Ambient occlusion scales the ambient + lit result (not emissive).
        if (occlusion < 1.0f)
        {
            r *= occlusion;
            g *= occlusion;
            b *= occlusion;
        }

        // Self-illumination, added after lighting and before clamping.
        r += emissive.R;
        g += emissive.G;
        b += emissive.B;

        return new ColorRGBAf(
            MathF.Min(1.0f, r),
            MathF.Min(1.0f, g),
            MathF.Min(1.0f, b),
            baseColor.A);
    }

    private static float Smoothstep(float edge0, float edge1, float x)
    {
        if (edge0 == edge1)
            return x >= edge1 ? 1.0f : 0.0f;

        float t = MathF.Max(0.0f, MathF.Min(1.0f, (x - edge0) / (edge1 - edge0)));
        return t * t * (3.0f - 2.0f * t);
    }

    /// <summary>
    /// Bare Lambert diffuse term for a single point light.
    /// Kept for backward compatibility.
    /// </summary>
    public static float Lambert(
        Vector3f position,
        Vector3f normal,
        PointLight light)
    {
        Vector3f L =
            (light.Position - position)
            .Normalized();

        return MathF.Max(
            0.0f,
            Vector3f.Dot(
                normal,
                L))
            * light.Intensity;
    }
}
