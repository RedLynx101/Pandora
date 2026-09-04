using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OrbitDock.App;
using OrbitDock.Core;

namespace Pandora.App.Tests;

internal static partial class Program
{
    private static readonly List<TestResult> Results = [];
    private static readonly List<string> Images = [];
    private static string _runPath = string.Empty;
    private static string _fixturePath = string.Empty;

    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length != 2 || args[0] != "--output" || !Path.IsPathFullyQualified(args[1]))
        {
            Console.Error.WriteLine("Supply --output <absolute evidence directory>. No default user-data directory is used.");
            return 2;
        }

        _runPath = Path.Combine(Path.GetFullPath(args[1]), "run-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..8]);
        _fixturePath = Path.Combine(_runPath, "fixtures");
        Directory.CreateDirectory(_fixturePath);
        // App.xaml contains precisely this merged resource dictionary. Load the same compiled
        // dictionary into a plain test Application: the product App type is never instantiated.
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("/Pandora.App;component/Themes/PandoraControls.xaml", UriKind.Relative)
        });
        app.DispatcherUnhandledException += (_, e) =>
        {
            Results.Add(new TestResult("Unhandled WPF dispatcher exception", false, e.Exception.ToString()));
            e.Handled = true;
        };

        Run("Theme normalization and finite opacity bounds", ThemeNormalization);
        Run("All palettes expose readable semantic brushes", ThemeContrast);
        Run("Legacy dock defaults follow themes; deliberate overrides survive", DockThemeCompatibility);
        Run("Brand variants load actual image and icon assets", BrandAssets);
        Run("Startup approval bytes fail closed without registry writes", StartupApprovalBytes);
        Run("Implicit controls consume current dynamic theme", ThemeControls);
        Run("Settings appearance preview, save, revert, and close persistence", SettingsBehavior);
        Run("Settings categories and all appearance themes render", SettingsRenders);
        Run("Actual dock content renders without desktop startup", DockRenders);
        Run("Projects: empty registry and explicit error states", ProjectStates);
        Run("Projects: read-only multi-project details and item-sized buckets", ProjectDetails);
        Run("Verification never displayed an application window", () =>
            Assert(Application.Current.Windows.Cast<Window>().All(w => !w.IsVisible), "A test unexpectedly showed a native window."));

        var report = new
        {
            schema = "pandora-wpf-verification/v1",
            timestamp = DateTimeOffset.UtcNow,
            assembly = typeof(SettingsWindow).Assembly.GetName().Name,
            runtime = Environment.Version.ToString(),
            operatingSystem = Environment.OSVersion.ToString(),
            highContrastObserved = ThemeService.IsHighContrast,
            evidenceType = "Offscreen rendering of actual WPF controls; not a live desktop screenshot",
            boundaries = new[] { "No normal App startup/exit", "No manager Start/Reload", "No Window.Show", "No current-user stores", "No startup or desktop-icon writes", "No live mixed-DPI/compositor claim" },
            passed = Results.Count(r => r.Passed),
            failed = Results.Count(r => !r.Passed),
            results = Results,
            images = Images.Select(path => new { path, sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) }).ToArray()
        };
        var reportPath = Path.Combine(_runPath, "verification-results.json");
        File.WriteAllText(reportPath, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"Evidence: {reportPath}");
        Console.WriteLine($"{report.passed} passed; {report.failed} failed; {Images.Count} WPF renders.");
        return report.failed == 0 ? 0 : 1;
    }

    private static void ThemeNormalization()
    {
        Assert(ThemeService.NormalizeTheme("graphite") == "LunarGlass", "Legacy Graphite must normalize to LunarGlass.");
        Assert(ThemeService.NormalizeTheme(" limestone ") == "Limestone", "Normalization is trim/case tolerant.");
        Assert(ThemeService.NormalizeTheme("unknown") == "LunarGlass", "Unknown values must use a safe default.");
        ThemeService.Apply("Midnight", double.NaN, true);
        Assert(ThemeService.ReduceMotion, "Explicit reduce motion must be honored.");
        Assert(ThemeService.DockOpacity is >= 0.55 and <= 1, "Invalid opacity must remain finite and bounded.");
        ThemeService.Apply("Midnight", -100, false);
        Assert(ThemeService.DockOpacity is >= 0.55 and <= 1, "Negative opacity must be clamped.");
        ThemeService.Apply("Midnight", 100, false);
        Assert(ThemeService.DockOpacity == 1, "Excess opacity must clamp to opaque.");
        ThemeService.Apply("System", 0.88, false);
        Assert(ThemeService.EffectiveTheme is "Limestone" or "LunarGlass", "System setting must resolve to a concrete palette.");
    }

    private static void ThemeContrast()
    {
        var observations = new List<object>();
        foreach (var theme in new[] { "LunarGlass", "Midnight", "Limestone" })
        {
            ThemeService.Apply(theme, 0.88, false);
            foreach (var resource in new[] { "Window", "Surface", "Elevated", "Border", "Text", "Muted", "Accent", "AccentText", "Selection", "Hover", "Danger", "Success", "Glass" })
                Assert(Application.Current.TryFindResource("Pandora." + resource + "Brush") is SolidColorBrush, resource + " semantic brush is missing.");
            foreach (var foreground in new[] { "Text", "Muted" })
            foreach (var background in new[] { "Window", "Surface", "Elevated" })
            {
                var ratio = Contrast(Brush(foreground).Color, Brush(background).Color);
                observations.Add(new { theme, foreground, background, ratio });
                Assert(ratio >= 4.5, $"{theme} {foreground}/{background} contrast {ratio:F2} is below 4.5:1.");
            }
            Assert(Contrast(Brush("AccentText").Color, Brush("Accent").Color) >= 4.5, theme + " accent button text is below 4.5:1.");
        }
        File.WriteAllText(Path.Combine(_runPath, "palette-contrast.json"), JsonSerializer.Serialize(observations, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void DockThemeCompatibility()
    {
        ThemeService.Apply("Limestone", 0.76, false);
        var defaults = new ZoneAppearance();
        var background = ThemeService.GetDockBackground(defaults);
        Assert(background.Color == Brush("Window").Color, "Original default background should adopt the theme.");
        Assert(ThemeService.GetDockAccent(defaults).Color == Brush("Accent").Color, "Original accent should adopt the theme.");
        Assert(Math.Abs(background.Opacity - ThemeService.DockOpacity) < 0.001, "Original opacity should adopt global preference.");
        if (!ThemeService.IsHighContrast)
        {
            var custom = new ZoneAppearance { BackgroundColor = "#123456", AccentColor = "#ABCDEF", Opacity = 0.65 };
            Assert(ThemeService.GetDockBackground(custom).Color == (Color)ColorConverter.ConvertFromString("#123456"), "Custom background was discarded.");
            Assert(ThemeService.GetDockAccent(custom).Color == (Color)ColorConverter.ConvertFromString("#ABCDEF"), "Custom accent was discarded.");
            Assert(ThemeService.GetDockBackground(custom).Opacity == 0.65, "Custom opacity was discarded.");
            foreach (var color in new[] { "#121821", "#0B1018", "#090E16", "#070D16", "#08111A" })
            foreach (var opacity in new[] { 0.88, 0.82, 0.80, 0.76 })
            {
                var factory = ThemeService.GetDockBackground(new ZoneAppearance { BackgroundColor = color, Opacity = opacity });
                Assert(factory.Color == Brush("Window").Color && Math.Abs(factory.Opacity - ThemeService.DockOpacity) < 0.001, "Factory background/opacity failed theme migration: " + color);
            }
            foreach (var color in new[] { "#334455", "#F4F2EE", "#777777" })
            {
                var appearance = new ZoneAppearance { BackgroundColor = color, Opacity = 0.67 };
                Assert(Contrast(ThemeService.GetDockText(appearance).Color, ThemeService.GetDockBackground(appearance).Color) >= 4.5,
                    "Custom dock's own label contrast is below 4.5:1: " + color);
            }
        }
    }

    private static void BrandAssets()
    {
        foreach (var style in new[] { "Aperture", "Selene", "Aster" })
        {
            var image = BrandIdentity.Image(style);
            Assert(image is { PixelWidth: 128, PixelHeight: 128 }, style + " image failed to load at expected size.");
            Assert(File.Exists(BrandIdentity.IconPath(style)), style + " icon is missing.");
        }
        Assert(BrandIdentity.Name == "Pandora", "Product name must be Pandora.");
    }

    private static void StartupApprovalBytes()
    {
        var method = typeof(StartupAppService).GetMethod("IsApprovalEnabled", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("Missing pure startup approval decoder.");
        bool Enabled(object? value) => (bool)method.Invoke(null, [value])!;
        Assert(Enabled(null), "Missing approval should leave the registered entry enabled.");
        foreach (uint state in new uint[] { 2, 6 }) Assert(Enabled(BitConverter.GetBytes(state)), "Enabled approval state was rejected: " + state);
        foreach (uint state in new uint[] { 0, 3, 7, 255 }) Assert(!Enabled(BitConverter.GetBytes(state)), "Unknown/disabled approval state was accepted: " + state);
        Assert(!Enabled(new byte[] { 2, 0, 0 }) && !Enabled("2") && !Enabled(2), "Malformed approval data must fail closed.");
    }

    private static void ThemeControls()
    {
        var panel = new StackPanel { Margin = new Thickness(24) };
        panel.Children.Add(new TextBlock { Text = "Pandora · shared controls", FontSize = 24, Margin = new Thickness(0, 0, 0, 18) });
        panel.Children.Add(new Button { Content = "A focused workspace", Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new Button { Content = "Accent action", Style = (Style)Application.Current.FindResource("Pandora.AccentButton"), Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new TextBox { Text = "Search projects and settings", Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new ComboBox { ItemsSource = new[] { "Lunar Glass", "Midnight", "Limestone" }, SelectedIndex = 0, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new CheckBox { Content = "Reduce decorative motion", IsChecked = true, Margin = new Thickness(0, 0, 0, 12) });
        panel.Children.Add(new Slider { Minimum = 55, Maximum = 100, Value = 88 });
        var root = new Border { Child = panel };
        root.SetResourceReference(Border.BackgroundProperty, "Pandora.WindowBrush");
        root.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty, "Pandora.TextBrush");
        foreach (var theme in new[] { "LunarGlass", "Midnight", "Limestone" })
        {
            ThemeService.Apply(theme, 0.88, false);
            Capture(root, "controls-" + theme, 460, 390);
            var button = panel.Children.OfType<Button>().First();
            Assert(button.Foreground is SolidColorBrush text && Contrast(text.Color, Brush("Elevated").Color) >= 4.5, theme + " button did not inherit readable theme text.");
            var accent = panel.Children.OfType<Button>().Last();
            var generatedLabel = Descendants(accent).OfType<TextBlock>().First(t => t.Text == "Accent action");
            Assert(generatedLabel.Foreground is SolidColorBrush actualText && Contrast(actualText.Color, Brush("Accent").Color) >= 4.5,
                theme + " rendered accent text ignored control foreground inheritance.");
        }
    }

    private static void SettingsBehavior()
    {
        using var fixture = new ManagerFixture(Path.Combine(_fixturePath, "settings-behavior"));
        var window = new SettingsWindow(fixture.Manager);
        var diskBefore = File.ReadAllBytes(fixture.Store.WorkspacePath);
        SelectTag(Find<ComboBox>(window, "ThemeComboBox"), "Limestone");
        Find<Slider>(window, "GlassOpacitySlider").Value = 73;
        Find<CheckBox>(window, "ReduceMotionCheckBox").IsChecked = true;
        Assert(ThemeService.EffectiveTheme == "Limestone", "Theme selection did not preview.");
        Assert(File.ReadAllBytes(fixture.Store.WorkspacePath).SequenceEqual(diskBefore), "Preview wrote the workspace.");
        Assert(fixture.Manager.Workspace.Settings.Theme == "LunarGlass", "Preview mutated canonical saved settings.");
        Invoke(window, "RevertTheme_Click", window, new RoutedEventArgs());
        Assert(ThemeService.EffectiveTheme == "LunarGlass", "Revert did not restore saved theme.");
        Assert(File.ReadAllBytes(fixture.Store.WorkspacePath).SequenceEqual(diskBefore), "Revert wrote the workspace.");
        SelectTag(Find<ComboBox>(window, "ThemeComboBox"), "Midnight");
        Find<Slider>(window, "GlassOpacitySlider").Value = 81;
        Find<CheckBox>(window, "ReduceMotionCheckBox").IsChecked = true;
        SelectTag(Find<ComboBox>(window, "IconStyleComboBox"), "Selene");
        Invoke(window, "SaveAppearance_Click", window, new RoutedEventArgs());
        var reloaded = fixture.Store.LoadOrCreate();
        Assert(reloaded.Settings.Theme == "Midnight", "Appearance save did not persist theme.");
        Assert(Math.Abs(reloaded.Settings.GlassOpacity - 0.81) < 0.001, "Appearance save did not persist opacity.");
        Assert(reloaded.Settings.ReduceMotion, "Appearance save did not persist reduced motion.");
        Assert(reloaded.Settings.IconStyle == "Selene", "Appearance save did not persist icon choice.");
        SelectTag(Find<ComboBox>(window, "ThemeComboBox"), "Limestone");
        window.Close();
        Drain();
        Assert(ThemeService.EffectiveTheme == "Midnight", "Closing preview did not restore newly saved theme.");
        Assert(fixture.Store.LoadOrCreate().Settings.Theme == "Midnight", "Closing preview changed persistent settings.");
    }

    private static void SettingsRenders()
    {
        using var fixture = new ManagerFixture(Path.Combine(_fixturePath, "settings-renders"));
        var window = new SettingsWindow(fixture.Manager);
        foreach (var theme in new[] { "LunarGlass", "Midnight", "Limestone" })
        {
            SelectTag(Find<ComboBox>(window, "ThemeComboBox"), theme);
            Capture((FrameworkElement)window.Content, "settings-" + theme, 1440, 960);
            var glyphButton = Descendants(window.Content as DependencyObject).OfType<Button>().First(b => b.Content is string s && s == "\uE8BB");
            var glyph = Descendants(glyphButton).OfType<TextBlock>().First(t => t.Text == "\uE8BB");
            Assert(glyph.FontFamily.Source.Contains("MDL2", StringComparison.Ordinal), "Window close glyph must retain its symbol font.");
        }
        SelectTag(Find<ComboBox>(window, "ThemeComboBox"), "LunarGlass");
        var navigation = Find<ListBox>(window, "SettingsNavigation");
        foreach (var category in new[] { "Appearance", "Desktop", "Docks", "Layouts", "Audio", "About" })
        {
            var item = navigation.Items.OfType<ListBoxItem>().Single(i => Equals(i.Tag, category));
            navigation.SelectedItem = item;
            Capture((FrameworkElement)window.Content, "settings-category-" + category, 960, 740);
            Assert(item.IsSelected, "Category selection failed: " + category);
        }
        window.Close();
    }

    private static void DockRenders()
    {
        using var fixture = new ManagerFixture(Path.Combine(_fixturePath, "dock"));
        var filesPath = Path.Combine(fixture.Root, "files");
        Directory.CreateDirectory(filesPath);
        File.WriteAllText(Path.Combine(filesPath, "Review notes.txt"), "Synthetic local fixture.");
        File.WriteAllText(Path.Combine(filesPath, "Interface contract.md"), "# Synthetic interface contract");
        var zone = new ZoneDefinition { Id = "test-dock", Name = "Workspace", IsVisible = false, Tabs = [new ZoneTabDefinition { Id = "files", Name = "Files", Path = filesPath }] };
        using var vm = new ZoneViewModel(zone, fixture.Manager);
        var window = new ZoneWindow(vm, fixture.Manager);
        Invoke(window, "RenderTabs");
        foreach (var theme in new[] { "LunarGlass", "Midnight", "Limestone" })
        {
            ThemeService.Apply(theme, 0.88, false);
            Capture((FrameworkElement)window.Content, "dock-" + theme, 440, 320);
            var menu = Descendants(window.Content as DependencyObject).OfType<ListBox>().Select(list => list.ContextMenu).First(m => m is not null)!;
            Capture(menu, "dock-context-menu-" + theme, 300, 278);
        }
        Assert(vm.Items.Count == 2, "Fixture dock did not load its two local files.");
        window.Close();
    }

    private static T Find<T>(FrameworkElement root, string name) where T : class =>
        root.FindName(name) as T ?? throw new InvalidOperationException($"Missing {typeof(T).Name} control: {name}");

    private static void SelectTag(ComboBox combo, string tag) => combo.SelectedItem = combo.Items.OfType<ComboBoxItem>().Single(i => Equals(i.Tag, tag));

    private static void Invoke(object target, string method, params object[] args)
    {
        var info = target.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic) ?? throw new InvalidOperationException("Missing handler: " + method);
        try { info.Invoke(target, args); }
        catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
    }

    private static void Capture(FrameworkElement content, string name, double width, double height, double dpi = 96)
    {
        // Application invalidation normally reaches shown Window roots. Offscreen, detached
        // controls need an explicit resource boundary so a new palette invalidates their tree.
        if (Window.GetWindow(content) is null)
        {
            var resources = new ResourceDictionary();
            resources.MergedDictionaries.Add(Application.Current.Resources);
            content.Resources = resources;
        }
        content.Measure(new Size(width, height));
        content.Arrange(new Rect(0, 0, width, height));
        content.UpdateLayout();
        Drain();
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(width * dpi / 96), (int)Math.Ceiling(height * dpi / 96), dpi, dpi, PixelFormats.Pbgra32);
        if (Window.GetWindow(content) is { Background: { } background })
        {
            // The Window owns its background, not its Content. Include that actual brush in
            // the raster while still avoiding a native HWND or compositor interaction.
            var backing = new DrawingVisual();
            using (var context = backing.RenderOpen()) context.DrawRectangle(background, null, new Rect(0, 0, width, height));
            bitmap.Render(backing);
        }
        bitmap.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        var path = Path.Combine(_runPath, name + ".png");
        using (var stream = File.Create(path)) encoder.Save(stream);
        Images.Add(path);
        Assert(File.Exists(path) && new FileInfo(path).Length > 1500, "Render appears empty: " + name);
    }

    private static void Drain() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    private static SolidColorBrush Brush(string name) => (SolidColorBrush)Application.Current.FindResource("Pandora." + name + "Brush");
    private static double Contrast(Color first, Color second)
    {
        static double Channel(byte value) => value / 255.0 <= 0.04045 ? value / 255.0 / 12.92 : Math.Pow((value / 255.0 + 0.055) / 1.055, 2.4);
        static double Luminance(Color color) => 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);
        var a = Luminance(first); var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static void Run(string name, Action test)
    {
        try { test(); Results.Add(new TestResult(name, true, null)); Console.WriteLine("PASS " + name); }
        catch (Exception ex) { Results.Add(new TestResult(name, false, ex.ToString())); Console.Error.WriteLine("FAIL " + name + ": " + ex.Message); }
    }
    private static void Assert(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
    private sealed record TestResult(string Name, bool Passed, string? Error);

    private sealed class ManagerFixture : IDisposable
    {
        public ManagerFixture(string root)
        {
            Root = root;
            Directory.CreateDirectory(root);
            Store = new WorkspaceStore(Path.Combine(root, "workspace.json"));
            var workspace = WorkspaceFactory.CreateDefault();
            workspace.Settings.Theme = "LunarGlass";
            workspace.Settings.GlassOpacity = 0.88;
            workspace.Settings.ReduceMotion = false;
            workspace.Settings.StartWithWindows = false;
            workspace.Settings.AttachWindowsToDesktop = false;
            workspace.Settings.HideDesktopIconsWhenRunning = false;
            workspace.Settings.Audio.EnableMusicDock = false;
            workspace.Settings.Audio.EnableSoundEffects = false;
            workspace.Settings.Audio.MusicRootPath = Path.Combine(root, "music");
            workspace.Settings.Audio.SoundEffectsPath = Path.Combine(root, "sounds");
            foreach (var zone in workspace.Zones) zone.IsVisible = false;
            var signature = WorkspaceLayoutService.ComputeDisplaySignature(DisplaySnapshotProvider.GetPhysicalDisplays());
            var variantKey = WorkspaceLayoutService.ComputeDisplayVariantKey(signature);
            WorkspaceLayoutService.UseDisplayVariant(workspace, variantKey, signature, DisplaySnapshotProvider.GetDisplays());
            foreach (var zone in workspace.Zones) WorkspaceLayoutService.SetDockVisibility(workspace, zone.Id, false);
            Store.Save(workspace);
            Manager = new DesktopZoneManager(Store);
            Assert(Manager.IsCurrentDisplayVariantActive(), "Fixture display identity must be current so Save does not queue desktop reload.");
        }
        public string Root { get; }
        public WorkspaceStore Store { get; }
        public DesktopZoneManager Manager { get; }
        public void Dispose() => Manager.Dispose();
    }
}
