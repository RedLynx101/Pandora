using System;
using System.Collections.Generic;
using System.Windows;

namespace Pandora.App;

/// <summary>Structural design, independent from palette or persisted dock content.</summary>
public sealed record DockThemeProfile(
    string Id,
    string Name,
    string Description,
    double CornerRadius,
    double HeaderHeight,
    Thickness HeaderPadding,
    Thickness ContentPadding,
    double FooterHeight,
    double ControlCornerRadius,
    double ItemCornerRadius,
    Thickness ItemPadding,
    double ItemGap,
    Thickness FrameBorderThickness,
    double HeaderGap,
    double AccentRailWidth,
    bool SeparatedHeader,
    double TitleFontSize,
    double ShadowBlur,
    double ShadowOpacity);

public static class DockThemeCatalog
{
    private static readonly DockThemeProfile Classic = new(
        "Classic", "Classic", "One continuous frame with familiar spacing.",
        18, 44, new Thickness(12, 0, 12, 0), new Thickness(12, 6, 12, 8), 26,
        6, 8, new Thickness(8, 7, 8, 7), 4, new Thickness(1), 0, 0, false, 13, 18, 0.22);

    private static readonly DockThemeProfile Halo = new(
        "Halo", "Halo", "A floating rounded header, pill controls, and room to breathe.",
        24, 60, new Thickness(16, 0, 16, 0), new Thickness(12, 8, 12, 12), 28,
        18, 14, new Thickness(12, 10, 12, 10), 8, new Thickness(1), 8, 0, true, 14, 24, 0.18);

    private static readonly DockThemeProfile Meridian = new(
        "Meridian", "Meridian", "A precise squared frame, accent rail, and compact rhythm.",
        4, 44, new Thickness(12, 0, 12, 0), new Thickness(8, 4, 8, 8), 22,
        3, 2, new Thickness(8, 6, 8, 6), 3, new Thickness(1), 0, 3, false, 12.5, 10, 0.12);

    public static IReadOnlyList<DockThemeProfile> All { get; } = Array.AsReadOnly(new[] { Classic, Halo, Meridian });

    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "halo" => "Halo",
        "meridian" => "Meridian",
        _ => "Classic"
    };

    public static DockThemeProfile Get(string? value) => Normalize(value) switch
    {
        "Halo" => Halo,
        "Meridian" => Meridian,
        _ => Classic
    };
}
