using System.Globalization;

namespace GymStation.Domain.Tenancy;

/// <summary>
/// WCAG relative-luminance contrast checks for tenant accent colors. Accents must hold
/// ≥ 3:1 (large-text/UI-component threshold) against BOTH mode backgrounds, because
/// dark (mat) and light (paper ledger) are both first-class (D4).
/// </summary>
public static class ThemeMath
{
    public const string DarkBackground = "#171B21";
    public const string LightBackground = "#EFEAE0";

    public static bool TryParseHexColor(string? hex, out (double R, double G, double B) rgb)
    {
        rgb = default;
        if (hex is null || hex.Length != 7 || hex[0] != '#'
            || !int.TryParse(hex.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return false;
        }

        rgb = ((value >> 16 & 0xFF) / 255.0, (value >> 8 & 0xFF) / 255.0, (value & 0xFF) / 255.0);
        return true;
    }

    public static double RelativeLuminance(string hex)
    {
        if (!TryParseHexColor(hex, out var rgb))
        {
            throw new InvalidOperationException($"'{hex}' is not a #rrggbb color.");
        }

        static double Channel(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        return 0.2126 * Channel(rgb.R) + 0.7152 * Channel(rgb.G) + 0.0722 * Channel(rgb.B);
    }

    public static double ContrastRatio(string hexA, string hexB)
    {
        var la = RelativeLuminance(hexA);
        var lb = RelativeLuminance(hexB);
        var (lighter, darker) = la >= lb ? (la, lb) : (lb, la);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>Valid #rrggbb AND ≥ 3:1 against both mode backgrounds.</summary>
    public static bool IsAccessibleAccent(string? hex)
        => TryParseHexColor(hex, out _)
           && ContrastRatio(hex!, DarkBackground) >= 3.0
           && ContrastRatio(hex!, LightBackground) >= 3.0;
}
