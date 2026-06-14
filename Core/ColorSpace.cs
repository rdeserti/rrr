namespace rrr.Core;

/// <summary>
/// Color-space conversions.
///
/// Textures are normally authored in sRGB, while lighting math is correct
/// only in linear space. The renderer does not yet perform this conversion:
/// these methods are placeholders that return their input unchanged, so the
/// call sites already exist and can be activated later in one place.
/// </summary>
public static class ColorSpace
{
    // TODO: implement real sRGB <-> linear conversion (per-channel:
    // linear = c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4)).
    // For now identity, so behaviour is unchanged.

    public static ColorRGBAf SrgbToLinear(ColorRGBAf color)
    {
        return color;
    }

    public static ColorRGBAf LinearToSrgb(ColorRGBAf color)
    {
        return color;
    }
}
