using rrr.Core;
using rrr.Scene;
using rrr.VMath;
using System.Collections.Generic;

namespace rrr.Rendering;

/// <summary>
/// Material/texture surface evaluation shared by the rasterizer's
/// <c>TextureShader</c> and the ray tracer, so both engines interpret diffuse/
/// specular/emissive/normal/occlusion maps, alpha-mask, two-sided and unlit
/// flags <b>identically</b>. The only thing that differs between engines is the
/// visibility source fed to <see cref="Lighting.Shade"/> (shadow maps for the
/// raster, shadow rays for the ray tracer) — supplied via
/// <paramref name="shadowMaps"/> (or null for no shadows).
///
/// Inputs are already interpolated to the shading point: <paramref name="normal"/>
/// is the normalized geometric/interpolated normal, <paramref name="tangent"/>
/// the interpolated (not yet re-orthonormalized) tangent, and
/// <paramref name="handednessRaw"/> the interpolated tangent handedness (its
/// sign is taken here).
/// </summary>
public static class SurfaceShading
{
    /// <summary>
    /// Returns the lit color. A negative alpha is the alpha-test discard
    /// sentinel (rasterizer convention); callers handle it per engine.
    /// <paramref name="visibility"/> is the per-light shadow source (shadow maps
    /// for the raster, shadow rays for the tracer).
    /// </summary>
    public static ColorRGBAf Shade<TVis>(
        Vector3f position,
        Vector3f normal,
        Vector3f tangent,
        float handednessRaw,
        Vector2f uv,
        ColorRGBAf vertexColor,
        Vector3f cameraPosition,
        IReadOnlyList<Light> lights,
        in TVis visibility,
        Texture2D? diffuse,
        Texture2D? specular,
        Texture2D? emissive,
        Texture2D? normalMap,
        bool normalIsHeight,
        bool invertGreen,
        float bumpScale,
        ColorRGBAf tint,
        ColorRGBAf specularColor,
        float shininess,
        ColorRGBAf emissiveColor,
        Texture2D? occlusion,
        float alphaCutoff,
        bool twoSided,
        bool unlit,
        out Vector3f shadingNormal)
        where TVis : struct, Lighting.IVisibility
    {
        // Flat material colors are authored in sRGB too, so decode the tint to
        // linear (identity when gamma is off). Per-vertex color stays as-is.
        ColorRGBAf tintLin = ColorSpace.SrgbToLinear(tint);

        ColorRGBAf finalTint = new ColorRGBAf(
            tintLin.R * vertexColor.R, tintLin.G * vertexColor.G,
            tintLin.B * vertexColor.B, tintLin.A * vertexColor.A);

        // Albedo: diffuse map (sRGB placeholder) tinted, or just the tint.
        ColorRGBAf albedo = finalTint;

        if (diffuse != null)
        {
            ColorRGBAf d = ColorSpace.SrgbToLinear(diffuse.Sample(uv.X, uv.Y));
            albedo = new ColorRGBAf(
                d.R * finalTint.R, d.G * finalTint.G, d.B * finalTint.B, d.A * finalTint.A);
        }

        // Alpha-test (alphaMode = MASK): discard fragments below the cutoff.
        if (alphaCutoff >= 0.0f && albedo.A < alphaCutoff)
        {
            shadingNormal = normal;
            return new ColorRGBAf(0.0f, 0.0f, 0.0f, -1.0f);
        }

        // Specular color modulated by the specular map.
        ColorRGBAf spec = specularColor;

        if (specular != null)
        {
            ColorRGBAf s = specular.Sample(uv.X, uv.Y);
            spec = new ColorRGBAf(
                s.R * specularColor.R, s.G * specularColor.G, s.B * specularColor.B);
        }

        // Emissive color (flat color decoded sRGB->linear too), modulated by the
        // emissive map.
        ColorRGBAf emisColor = ColorSpace.SrgbToLinear(emissiveColor);
        ColorRGBAf emis = emisColor;

        if (emissive != null)
        {
            ColorRGBAf e = ColorSpace.SrgbToLinear(emissive.Sample(uv.X, uv.Y));
            emis = new ColorRGBAf(
                e.R * emisColor.R, e.G * emisColor.G, e.B * emisColor.B);
        }

        Vector3f n = normal;

        if (normalMap != null)
            n = PerturbNormal(n, tangent, handednessRaw, uv,
                normalMap, normalIsHeight, invertGreen, bumpScale);

        // Expose the actual shading normal (perturbed by the normal/height map)
        // so the ray tracer can reflect/refract off the bumped surface.
        shadingNormal = n;

        // Ambient occlusion (red channel) darkens ambient + lit terms.
        float occ = occlusion != null ? occlusion.Sample(uv.X, uv.Y).R : 1.0f;

        return Lighting.Shade(
            position, n, cameraPosition, lights,
            albedo, spec, shininess, emis, in visibility,
            occ, twoSided, unlit);
    }

    private static Vector3f PerturbNormal(
        Vector3f normal, Vector3f tangentIn, float handednessRaw, Vector2f uv,
        Texture2D normalMap, bool normalIsHeight, bool invertGreen, float bumpScale)
    {
        // Re-orthonormalize the tangent against the interpolated normal.
        Vector3f t = tangentIn - normal * Vector3f.Dot(normal, tangentIn);

        if (t.LengthSquared() < 1e-12f)
            return normal;

        t = t.Normalized();

        float handedness = handednessRaw < 0.0f ? -1.0f : 1.0f;
        Vector3f bitangent = Vector3f.Cross(normal, t) * handedness;

        Vector3f tn = SampleTangentNormal(uv, normalMap, normalIsHeight, invertGreen, bumpScale);

        // tangent-space -> world-space
        return (t * tn.X + bitangent * tn.Y + normal * tn.Z).Normalized();
    }

    private static Vector3f SampleTangentNormal(
        Vector2f uv, Texture2D normalMap, bool normalIsHeight, bool invertGreen, float bumpScale)
    {
        // Normal/height maps store linear data, so no sRGB conversion.
        if (normalIsHeight)
        {
            float du = 1.0f / normalMap.Width;
            float dv = 1.0f / normalMap.Height;

            float h = normalMap.Sample(uv.X, uv.Y).R;
            float hx = normalMap.Sample(uv.X + du, uv.Y).R;
            float hy = normalMap.Sample(uv.X, uv.Y + dv).R;

            return new Vector3f(
                (h - hx) * bumpScale,
                (h - hy) * bumpScale,
                1.0f).Normalized();
        }

        ColorRGBAf c = normalMap.Sample(uv.X, uv.Y);

        float ny = (c.G * 2.0f - 1.0f) * bumpScale;
        if (invertGreen)
            ny = -ny;

        return new Vector3f(
            (c.R * 2.0f - 1.0f) * bumpScale,
            ny,
            c.B * 2.0f - 1.0f).Normalized();
    }
}
