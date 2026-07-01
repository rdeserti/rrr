using System;

namespace rrr.Core;

/// <summary>
/// sRGB &lt;-&gt; linear color-space conversions (the real piecewise sRGB curve).
///
/// Lighting math is correct only in linear space, while color textures and
/// authored colors are sRGB-encoded and displays expect sRGB output. The
/// pipeline therefore decodes color inputs (sRGB-&gt;linear) on the way in and
/// encodes the final image (linear-&gt;sRGB) on the way out.
///
/// The two directions are gated independently so each half can be turned off:
/// <see cref="SrgbToLinearEnabled"/> controls input linearization,
/// <see cref="LinearToSrgbEnabled"/> the final sRGB reconstruction. Both default
/// on (full gamma-correct rendering). When a direction is off it returns its
/// input immediately — no branch-per-channel pow cost. The flags are set once
/// per render from <c>RenderSettings</c>, before any parallel work, and only
/// read afterwards.
/// </summary>
public static class ColorSpace
{
    /// <summary>Decode color inputs sRGB-&gt;linear (texture/flat-color/background).</summary>
    public static bool SrgbToLinearEnabled { get; set; } = true;

    /// <summary>Encode the final image linear-&gt;sRGB.</summary>
    public static bool LinearToSrgbEnabled { get; set; } = true;

    public static ColorRGBAf SrgbToLinear(ColorRGBAf color)
    {
        if (!SrgbToLinearEnabled)
            return color;

        return new ColorRGBAf(
            SrgbToLinear(color.R),
            SrgbToLinear(color.G),
            SrgbToLinear(color.B),
            color.A); // alpha is linear data, never gamma-converted
    }

    public static ColorRGBAf LinearToSrgb(ColorRGBAf color)
    {
        if (!LinearToSrgbEnabled)
            return color;

        return new ColorRGBAf(
            LinearToSrgb(color.R),
            LinearToSrgb(color.G),
            LinearToSrgb(color.B),
            color.A);
    }

    public static float SrgbToLinear(float c)
        => c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);

    public static float LinearToSrgb(float c)
        => c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1.0f / 2.4f) - 0.055f;
}
