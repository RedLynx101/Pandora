using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using OrbitDock.Core;

namespace OrbitDock.App;

/// <summary>Independent live structure and palette; never rewrites persisted dock overrides.</summary>
public static class ThemeService
{
    private static bool _initialized;
    private static string _requestedTheme = "LunarGlass";
    private static double _requestedOpacity = 0.88;
    private static bool _requestedReducedMotion;
    private static string _requestedDockTheme = "Classic";
    private static string? _customAccent;
    private static string? _customSurface;

    public static event EventHandler? ThemeChanged;
    public static string EffectivePalette { get; private set; } = "LunarGlass";
    public static string EffectiveTheme => EffectivePalette;
    public static string EffectiveDockTheme => _requestedDockTheme;
    public static string? EffectiveCustomAccentColor => _customAccent;
    public static string? EffectiveCustomSurfaceColor => _customSurface;
    public static bool IsHighContrast => SystemParameters.HighContrast;
    public static bool ReduceMotion => _requestedReducedMotion || !SystemParameters.ClientAreaAnimation || IsHighContrast;
    public static double DockOpacity => IsHighContrast ? 1 : _requestedOpacity;

    public static void Initialize(AppSettings settings)
    {
        if (!_initialized)
        {
            SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            _initialized = true;
        }

        Apply(settings);
    }

    public static void Apply(AppSettings settings) => Apply(settings.Theme, settings.GlassOpacity, settings.ReduceMotion,
        settings.DockTheme, settings.CustomAccentColor, settings.CustomSurfaceColor);

    public static string NormalizeTheme(string? theme) => theme?.Trim().ToLowerInvariant() switch
    {
        "midnight" => "Midnight",
        "limestone" => "Limestone",
        "aegean" => "Aegean",
        "system" => "System",
        // Graphite was stored by OrbitDock without a theme picker. It now resolves to Lunar Glass.
        _ => "LunarGlass"
    };

    public static string NormalizeDockTheme(string? value) => DockThemeCatalog.Normalize(value);

    /// <summary>Blank means palette default. Only an opaque #RRGGBB value is accepted.</summary>
    public static bool TryNormalizeCustomColor(string? value, out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(value)) return true;
        var trimmed = value.Trim();
        if (trimmed.Length != 7 || trimmed[0] != '#') return false;
        for (var index = 1; index < trimmed.Length; index++)
        {
            var character = trimmed[index];
            if (!(character is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F')) return false;
        }
        normalized = trimmed.ToUpperInvariant();
        return true;
    }

    public static void Apply(string? theme, double opacity, bool reduceMotion) =>
        Apply(theme, opacity, reduceMotion, "Classic", null, null);

    public static void Apply(string? theme, double opacity, bool reduceMotion, string? dockTheme, string? accent, string? surface)
    {
        _requestedTheme = NormalizeTheme(theme);
        _requestedOpacity = double.IsFinite(opacity) ? Math.Clamp(opacity, 0.55, 1) : 0.88;
        _requestedReducedMotion = reduceMotion;
        _requestedDockTheme = NormalizeDockTheme(dockTheme);
        TryNormalizeCustomColor(accent, out _customAccent);
        TryNormalizeCustomColor(surface, out _customSurface);
        RefreshPalette();
    }

    private static void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e) => ScheduleRefresh();
    private static void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e) => ScheduleRefresh();

    private static void ScheduleRefresh()
    {
        var app = Application.Current;
        if (app is null || app.Dispatcher.HasShutdownStarted) return;
        app.Dispatcher.BeginInvoke(new Action(RefreshPalette));
    }

    private static void RefreshPalette()
    {
        var app = Application.Current;
        if (app is null) return;
        EffectivePalette = _requestedTheme == "System" ? (UsesLightWindowsTheme() ? "Limestone" : "LunarGlass") : _requestedTheme;
        var palette = EffectivePalette switch
        {
            "Limestone" => new Palette("#F4F1EB", "#EAE6DF", "#FFFFFF", "#C2BDB3", "#252833", "#5F626B", "#67578D", "#FFFFFF", "#DDD5EC", "#E0DCD5", "#9D344A", "#276D55"),
            "Midnight" => new Palette("#0B0E16", "#111621", "#1B2231", "#3B4458", "#F2F4FB", "#AFB8CC", "#B8B5FC", "#111426", "#353454", "#262D40", "#FF9EAD", "#8BDBC0"),
            "Aegean" => new Palette("#102B35", "#163741", "#224854", "#47717B", "#EEF9F9", "#BBD5DB", "#85D4CE", "#102B35", "#2A5B63", "#24505A", "#FFB5BA", "#97D8AE"),
            _ => new Palette("#151A29", "#1C2333", "#293246", "#465169", "#F0F2FC", "#BAC3D8", "#C1BCFF", "#171929", "#45415F", "#343D52", "#FFADBA", "#A1DFC9")
        };
        palette = CustomizePalette(palette);

        SetBrush("Window", IsHighContrast ? SystemColors.WindowColor : Parse(palette.Window));
        SetBrush("Surface", IsHighContrast ? SystemColors.ControlColor : Parse(palette.Surface));
        SetBrush("Elevated", IsHighContrast ? SystemColors.WindowColor : Parse(palette.Elevated));
        SetBrush("Border", IsHighContrast ? SystemColors.WindowTextColor : Parse(palette.Border));
        SetBrush("Text", IsHighContrast ? SystemColors.WindowTextColor : Parse(palette.Text));
        SetBrush("Muted", IsHighContrast ? SystemColors.WindowTextColor : Parse(palette.Muted));
        SetBrush("Accent", IsHighContrast ? SystemColors.HighlightColor : Parse(palette.Accent));
        SetBrush("AccentText", IsHighContrast ? SystemColors.HighlightTextColor : Parse(palette.AccentText));
        SetBrush("Focus", IsHighContrast ? SystemColors.WindowTextColor : EnsureContrast(Parse(palette.Accent),
            [Parse(palette.Window), Parse(palette.Surface), Parse(palette.Elevated), Parse(palette.Hover), Parse(palette.Selection)], 3));
        SetBrush("Selection", IsHighContrast ? SystemColors.HighlightColor : Parse(palette.Selection));
        SetBrush("SelectionText", IsHighContrast ? SystemColors.HighlightTextColor : EnsureContrast(Parse(palette.Text), [Parse(palette.Selection)], 4.5));
        SetBrush("Hover", IsHighContrast ? SystemColors.ControlColor : Parse(palette.Hover));
        SetBrush("Danger", IsHighContrast ? SystemColors.WindowTextColor : Parse(palette.Danger));
        SetBrush("Success", IsHighContrast ? SystemColors.WindowTextColor : Parse(palette.Success));
        SetBrush("Glass", IsHighContrast ? SystemColors.WindowColor : Parse(palette.Window), DockOpacity);
        app.Resources["Pandora.ReduceMotion"] = ReduceMotion;
        ApplyStructure(DockThemeCatalog.Get(_requestedDockTheme));
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

    private static Palette CustomizePalette(Palette palette)
    {
        if (_customAccent is null && _customSurface is null) return palette;

        var window = _customSurface is null ? Parse(palette.Window) : Parse(_customSurface);
        var light = Luminance(window) > 0.179;
        // Derived opaque surfaces move away from the contrast crossover, not toward it.
        var toward = light ? Colors.White : Colors.Black;
        var surface = _customSurface is null ? Parse(palette.Surface) : Blend(window, toward, 0.08);
        var elevated = _customSurface is null ? Parse(palette.Elevated) : Blend(window, toward, 0.16);
        var backgrounds = new[] { window, surface, elevated };
        var text = EnsureContrast(_customSurface is null ? Parse(palette.Text) : (light ? Colors.Black : Colors.White), backgrounds, 4.5);
        var muted = EnsureContrast(_customSurface is null ? Parse(palette.Muted) : Blend(text, window, 0.26), backgrounds, 4.5);
        var accent = _customAccent is null ? Parse(palette.Accent) : Parse(_customAccent);
        var selection = Blend(surface, accent, 0.24);
        var hover = _customSurface is null ? Parse(palette.Hover) : Blend(surface, text, 0.055);
        // A custom middle-gray surface can leave little contrast margin. Keep regular labels readable.
        selection = KeepTextContrast(selection, surface, text);
        hover = KeepTextContrast(hover, surface, text);
        muted = EnsureContrast(muted, [window, surface, elevated, selection, hover], 4.5);
        return palette with
        {
            Window = Hex(window), Surface = Hex(surface), Elevated = Hex(elevated),
            Text = Hex(text), Muted = Hex(muted), Accent = Hex(accent),
            AccentText = Hex(EnsureContrast(Parse(palette.AccentText), [accent], 4.5)),
            Selection = Hex(selection), Hover = Hex(hover),
            Border = Hex(EnsureContrast(Blend(surface, text, 0.35), backgrounds, 3)),
            Danger = Hex(EnsureContrast(Parse(palette.Danger), backgrounds, 4.5)),
            Success = Hex(EnsureContrast(Parse(palette.Success), backgrounds, 4.5))
        };
    }

    private static void ApplyStructure(DockThemeProfile profile)
    {
        var resources = Application.Current.Resources;
        resources["Pandora.DockCornerRadius"] = new CornerRadius(profile.CornerRadius);
        resources["Pandora.HeaderHeight"] = profile.HeaderHeight;
        resources["Pandora.HeaderPadding"] = profile.HeaderPadding;
        resources["Pandora.ContentPadding"] = profile.ContentPadding;
        resources["Pandora.FooterHeight"] = profile.FooterHeight;
        resources["Pandora.ControlCornerRadius"] = new CornerRadius(profile.ControlCornerRadius);
        resources["Pandora.ItemCornerRadius"] = new CornerRadius(profile.ItemCornerRadius);
        resources["Pandora.ItemPadding"] = profile.ItemPadding;
        resources["Pandora.ItemGap"] = profile.ItemGap;
        resources["Pandora.ItemMargin"] = new Thickness(0, profile.ItemGap / 2, 0, profile.ItemGap / 2);
        resources["Pandora.FrameBorderThickness"] = profile.FrameBorderThickness;
        resources["Pandora.HeaderGap"] = profile.HeaderGap;
        resources["Pandora.AccentRailWidth"] = profile.AccentRailWidth;
        resources["Pandora.SeparatedHeader"] = profile.SeparatedHeader;
        resources["Pandora.TitleFontSize"] = profile.TitleFontSize;
        resources["Pandora.ShadowBlur"] = IsHighContrast ? 0.0 : profile.ShadowBlur;
        resources["Pandora.ShadowOpacity"] = IsHighContrast ? 0.0 : profile.ShadowOpacity;
        resources["Pandora.ControlPadding"] = profile.Id switch
        {
            "Halo" => new Thickness(15, 9, 15, 9),
            "Meridian" => new Thickness(10, 6, 10, 6),
            _ => new Thickness(14, 8, 14, 8)
        };
        resources["Pandora.MenuPadding"] = profile.Id == "Halo" ? new Thickness(12, 9, 12, 9) :
            profile.Id == "Meridian" ? new Thickness(9, 5, 9, 5) : new Thickness(9, 7, 9, 7);
        resources["Pandora.ProjectCardCornerRadius"] = new CornerRadius(profile.ItemCornerRadius);
        resources["Pandora.ProjectCardPadding"] = profile.ItemPadding;
        resources["Pandora.ProjectCardMargin"] = new Thickness(0, 0, 0, profile.ItemGap);
        resources["Pandora.ExpanderHeaderPadding"] = profile.ItemPadding;
    }

    private static Color KeepTextContrast(Color candidate, Color fallback, Color text)
    {
        if (Contrast(candidate, text) >= 4.5) return candidate;
        for (var step = 1; step <= 10; step++)
        {
            var adjusted = Blend(candidate, fallback, step / 10.0);
            if (Contrast(adjusted, text) >= 4.5) return adjusted;
        }
        return fallback;
    }

    private static Color EnsureContrast(Color candidate, Color[] backgrounds, double ratio)
    {
        if (MinimumContrast(candidate, backgrounds) >= ratio) return candidate;
        var blackScore = MinimumContrast(Colors.Black, backgrounds);
        var whiteScore = MinimumContrast(Colors.White, backgrounds);
        var target = blackScore >= whiteScore ? Colors.Black : Colors.White;
        for (var step = 1; step <= 20; step++)
        {
            var adjusted = Blend(candidate, target, step / 20.0);
            if (MinimumContrast(adjusted, backgrounds) >= ratio) return adjusted;
        }
        return target;
    }

    private static double MinimumContrast(Color foreground, Color[] backgrounds)
    {
        var minimum = double.MaxValue;
        foreach (var background in backgrounds) minimum = Math.Min(minimum, Contrast(foreground, background));
        return minimum;
    }
    private static double Contrast(Color first, Color second)
    {
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }
    private static double Luminance(Color color) => 0.2126 * LinearChannel(color.R) + 0.7152 * LinearChannel(color.G) + 0.0722 * LinearChannel(color.B);
    private static Color Blend(Color start, Color end, double fraction) => Color.FromRgb(
        (byte)Math.Round(start.R + (end.R - start.R) * fraction),
        (byte)Math.Round(start.G + (end.G - start.G) * fraction),
        (byte)Math.Round(start.B + (end.B - start.B) * fraction));
    private static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>Legacy defaults follow the selected palette. Deliberate custom colors remain intact.</summary>
    public static SolidColorBrush GetDockBackground(ZoneAppearance appearance)
    {
        if (IsHighContrast) return new SolidColorBrush(SystemColors.WindowColor);
        var useTheme = IsFactoryBackground(appearance.BackgroundColor);
        var color = useTheme ? ResourceColor("Pandora.WindowBrush", "#151A29") : TryParse(appearance.BackgroundColor, "#151A29");
        // Known factory color/opacity pairs follow the global control; arbitrary custom values do not.
        var factoryOpacity = Math.Abs(appearance.Opacity - 0.88) < 0.0001 ||
            Math.Abs(appearance.Opacity - 0.82) < 0.0001 || Math.Abs(appearance.Opacity - 0.80) < 0.0001 ||
            Math.Abs(appearance.Opacity - 0.76) < 0.0001;
        var opacity = useTheme && factoryOpacity ? DockOpacity : appearance.Opacity;
        return new SolidColorBrush(color) { Opacity = double.IsFinite(opacity) ? Math.Clamp(opacity, 0.1, 1) : DockOpacity };
    }

    public static SolidColorBrush GetDockAccent(ZoneAppearance appearance)
    {
        if (IsHighContrast) return new SolidColorBrush(SystemColors.HighlightColor);
        var useTheme = string.IsNullOrWhiteSpace(appearance.AccentColor) || appearance.AccentColor.ToUpperInvariant() is
            "#4FB3FF" or "#56D6FF" or "#9F8CFF" or "#F37BC3" or "#F2C94C" or "#5CC8A7" or "#7DDCFF" or "#8BE9C7";
        return new SolidColorBrush(useTheme ? ResourceColor("Pandora.AccentBrush", "#C1BCFF") : TryParse(appearance.AccentColor, "#C1BCFF"));
    }

    public static bool IsFactoryBackground(string? color) => string.IsNullOrWhiteSpace(color) || color.ToUpperInvariant() is
        "#121821" or "#0B1018" or "#090E16" or "#070D16" or "#08111A";

    /// <summary>Custom dock backgrounds keep a contrasting label palette when global appearance changes.</summary>
    public static SolidColorBrush GetDockText(ZoneAppearance appearance, bool muted = false)
    {
        if (IsHighContrast) return new SolidColorBrush(SystemColors.WindowTextColor);
        if (IsFactoryBackground(appearance.BackgroundColor))
            return new SolidColorBrush(ResourceColor(muted ? "Pandora.MutedBrush" : "Pandora.TextBrush", muted ? "#BAC3D8" : "#F0F2FC"));
        var background = TryParse(appearance.BackgroundColor, "#151A29");
        var lightBackground = 0.2126 * LinearChannel(background.R) + 0.7152 * LinearChannel(background.G) + 0.0722 * LinearChannel(background.B) > 0.179;
        // Custom colors can sit at the black/white crossover: retain contrast rather than dimming secondary text.
        return new SolidColorBrush(lightBackground ? Colors.Black : Colors.White);
    }

    private static double LinearChannel(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static Color ResourceColor(string key, string fallback) => Application.Current?.TryFindResource(key) is SolidColorBrush brush ? brush.Color : Parse(fallback);
    private static Color TryParse(string? value, string fallback)
    {
        if (string.IsNullOrWhiteSpace(value)) return Parse(fallback);
        try { return Parse(value); }
        catch (FormatException) { return Parse(fallback); }
        catch (NotSupportedException) { return Parse(fallback); }
    }
    private static Color Parse(string value) => (Color)ColorConverter.ConvertFromString(value);
    private static void SetBrush(string name, Color color, double opacity = 1)
    {
        var brush = new SolidColorBrush(color) { Opacity = opacity };
        brush.Freeze();
        Application.Current.Resources[$"Pandora.{name}Brush"] = brush;
    }
    private static bool UsesLightWindowsTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or System.IO.IOException)
        {
            return false;
        }
    }

    private sealed record Palette(string Window, string Surface, string Elevated, string Border, string Text, string Muted, string Accent, string AccentText, string Selection, string Hover, string Danger, string Success);
}
