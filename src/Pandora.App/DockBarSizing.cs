namespace Pandora.App;

/// <summary>Scale only dock bars, never Settings, menus, file icons or stored expanded bounds.</summary>
public sealed record DockBarMetrics(double Height, double TitleFontSize, double ControlSize,
    double GlyphSize, double BrandSize, double BrandImageSize, double NavigationHeight, double TabFontSize);

public static class DockBarSizing
{
    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "compact" => "Compact",
        "large" => "Large",
        _ => "Standard"
    };

    public static DockBarMetrics Get(DockThemeProfile profile, string? size)
    {
        var brand = profile.SeparatedHeader ? 34 : 30;
        return Normalize(size) switch
        {
            "Compact" => new(profile.SeparatedHeader ? 44 : 36, profile.TitleFontSize - 1,
                26, 11, brand - 4, 20, 34, 11),
            "Large" => new(profile.HeaderHeight + 12, profile.TitleFontSize + 2,
                36, 15, brand + 6, 26, 44, 13),
            _ => new(profile.HeaderHeight, profile.TitleFontSize, 30, 13, brand, 22, 38, 11)
        };
    }
}
