using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using OrbitDock.Core;

namespace OrbitDock.App;

/// <summary>One live palette for all Pandora surfaces; changes never rewrite dock overrides.</summary>
public static class ThemeService
{
    private static bool _initialized;
    private static string _requestedTheme = "LunarGlass";
    private static double _requestedOpacity = 0.88;
    private static bool _requestedReducedMotion;

    public static event EventHandler? ThemeChanged;
    public static string EffectiveTheme { get; private set; } = "LunarGlass";
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

    public static void Apply(AppSettings settings) => Apply(settings.Theme, settings.GlassOpacity, settings.ReduceMotion);

    public static string NormalizeTheme(string? theme) => theme?.Trim().ToLowerInvariant() switch
    {
        "midnight" => "Midnight",
        "limestone" => "Limestone",
        "system" => "System",
        // Graphite was stored by OrbitDock without a theme picker. It now resolves to Lunar Glass.
        _ => "LunarGlass"
    };

    public static void Apply(string? theme, double opacity, bool reduceMotion)
    {
        _requestedTheme = NormalizeTheme(theme);
        _requestedOpacity = double.IsFinite(opacity) ? Math.Clamp(opacity, 0.55, 1) : 0.88;
        _requestedReducedMotion = reduceMotion;
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
        EffectiveTheme = _requestedTheme == "System" ? (UsesLightWindowsTheme() ? "Limestone" : "LunarGlass") : _requestedTheme;
        var palette = EffectiveTheme switch
        {
            "Limestone" => new Palette("#F4F1EB", "#EAE6DF", "#FFFFFF", "#C2BDB3", "#252833", "#5F626B", "#67578D", "#FFFFFF", "#DDD5EC", "#E0DCD5", "#9D344A", "#276D55"),
            "Midnight" => new Palette("#0B0E16", "#111621", "#1B2231", "#3B4458", "#F2F4FB", "#AFB8CC", "#B8B5FC", "#111426", "#353454", "#262D40", "#FF9EAD", "#8BDBC0"),
            _ => new Palette("#151A29", "#1C2333", "#293246", "#465169", "#F0F2FC", "#BAC3D8", "#C1BCFF", "#171929", "#45415F", "#343D52", "#FFADBA", "#A1DFC9")
        };

        SetBrush("Window", IsHighContrast ? SystemColors.WindowColor : Parse(palette.Window));
        SetBrush("Surface", IsHighContrast ? SystemColors.ControlColor : Parse(palette.Surface));
        SetBrush("Elevated", IsHighContrast ? SystemColors.WindowColor : Parse(palette.Elevated));
        SetBrush("Border", IsHighContrast ? SystemColors.WindowTextColor : Parse(palette.Border));
        SetBrush("Text", IsHighContrast ? SystemColors.WindowTextColor : Parse(palette.Text));
        SetBrush("Muted", IsHighContrast ? SystemColors.WindowTextColor : Parse(palette.Muted));
        SetBrush("Accent", IsHighContrast ? SystemColors.HighlightColor : Parse(palette.Accent));
        SetBrush("AccentText", IsHighContrast ? SystemColors.HighlightTextColor : Parse(palette.AccentText));
        SetBrush("Selection", IsHighContrast ? SystemColors.HighlightColor : Parse(palette.Selection));
        SetBrush("SelectionText", IsHighContrast ? SystemColors.HighlightTextColor : Parse(palette.Text));
        SetBrush("Hover", IsHighContrast ? SystemColors.ControlColor : Parse(palette.Hover));
        SetBrush("Danger", IsHighContrast ? SystemColors.WindowTextColor : Parse(palette.Danger));
        SetBrush("Success", IsHighContrast ? SystemColors.WindowTextColor : Parse(palette.Success));
        SetBrush("Glass", IsHighContrast ? SystemColors.WindowColor : Parse(palette.Window), DockOpacity);
        app.Resources["Pandora.ReduceMotion"] = ReduceMotion;
        ThemeChanged?.Invoke(null, EventArgs.Empty);
    }

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
    private static Color TryParse(string value, string fallback)
    {
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
