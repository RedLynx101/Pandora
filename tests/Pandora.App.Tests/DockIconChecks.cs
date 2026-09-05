using System.Buffers.Binary;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Pandora.App;
using Pandora.Core;

namespace Pandora.App.Tests;

internal static partial class Program
{
    private static void DockIconLoading()
    {
        var root = Path.Combine(_fixturePath, "dock-icon-loading");
        Directory.CreateDirectory(root);
        var path = CreateDockIconImage(root);
        var original = File.ReadAllBytes(path);
        var custom = new ZoneAppearance { HeaderIcon = DockHeaderIcon.Custom, HeaderIconPath = path };
        foreach (var style in new[] { "Aperture", "Selene", "Aster" })
        {
            var product = DockIconService.Resolve(new ZoneAppearance(), style);
            Assert(product.Image is BitmapSource && product.IsVisible && !product.IsFallback, "Default header icon failed for " + style);
            var result = DockIconService.Resolve(custom, style);
            Assert(result.Image is BitmapSource { PixelWidth: 48, PixelHeight: 48 } && result.IsVisible && !result.IsFallback,
                "A valid local custom image did not load independently of the product variant.");
        }
        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        Assert(File.ReadAllBytes(path).SequenceEqual(original), "Loading custom imagery retained a file lock or changed its bytes.");
        var none = DockIconService.Resolve(new ZoneAppearance { HeaderIcon = DockHeaderIcon.None, HeaderIconPath = path }, "Selene");
        Assert(!none.IsVisible && none.Image is null && !none.IsFallback, "None should neither show an icon nor fall back.");

        var jpeg = Path.Combine(root, "fixture.jpg");
        var encoder = new JpegBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create((BitmapSource)DockIconService.Resolve(custom, "Aperture").Image!));
        using (var output = File.Create(jpeg)) encoder.Save(output);
        var ico = Path.Combine(root, "fixture.ico");
        File.Copy(BrandIdentity.IconPath("Aperture"), ico);
        foreach (var supported in new[] { jpeg, ico })
            Assert(!DockIconService.Resolve(new ZoneAppearance { HeaderIcon = DockHeaderIcon.Custom, HeaderIconPath = supported }, "Aster").IsFallback,
                "Supported image format failed: " + Path.GetExtension(supported));

        var malformed = Path.Combine(root, "malformed.png");
        File.WriteAllText(malformed, "<svg><image href='https://invalid.example/image'/></svg>");
        var oversized = Path.Combine(root, "oversized.png");
        using (var output = File.Create(oversized)) output.SetLength(DockIconService.MaximumFileBytes + 1L);
        var hugePixels = Path.Combine(root, "huge-pixels.png");
        var hugeBytes = (byte[])original.Clone();
        BinaryPrimitives.WriteUInt32BigEndian(hugeBytes.AsSpan(16, 4), DockIconService.MaximumDimension + 1);
        File.WriteAllBytes(hugePixels, hugeBytes);
        foreach (var invalid in new[] { "", Path.Combine(root, "missing.png"), malformed, oversized, hugePixels,
            "https://invalid.example/image.png", @"\\invalid.example\share\image.png", "//invalid.example/share/image.png" })
        {
            var result = DockIconService.Resolve(new ZoneAppearance { HeaderIcon = DockHeaderIcon.Custom, HeaderIconPath = invalid }, "Selene");
            Assert(result.IsFallback && result.IsVisible && result.Image is not null && result.StatusMessage.Contains("using Pandora", StringComparison.Ordinal),
                "Invalid or nonlocal image did not fall back with a useful status.");
        }
    }

    private static void DockIconSettings()
    {
        using var fixture = new ManagerFixture(Path.Combine(_fixturePath, "dock-icon-settings"));
        var zone = fixture.Manager.Workspace.Zones[0];
        var other = fixture.Manager.Workspace.Zones[1];
        fixture.Manager.Workspace.Settings.IconStyle = "Aster";
        fixture.Manager.SaveAppearanceSettings();
        var original = File.ReadAllBytes(fixture.Store.WorkspacePath);
        var originalSettings = SettingsJson(fixture.Manager.Workspace.Settings);
        var path = CreateDockIconImage(fixture.Root);
        var window = new SettingsWindow(fixture.Manager);
        try
        {
            Assert(InvokeResult<bool>(window, "TrySelectDockIconFile", path), "The picker boundary rejected a valid image.");
            Assert(File.ReadAllBytes(fixture.Store.WorkspacePath).SequenceEqual(original) && zone.Appearance.HeaderIcon == DockHeaderIcon.Pandora,
                "Image selection/preview wrote the workspace before Save dock.");
            Assert(Find<Image>(window, "DockIconPreview").Source is BitmapSource { PixelWidth: 48 }, "Settings did not preview the selected custom image.");
            Assert(!InvokeResult<bool>(window, "TrySelectDockIconFile", Path.Combine(fixture.Root, "missing.png")) &&
                Find<TextBox>(window, "DockHeaderIconPathTextBox").Text == path, "An invalid picker result replaced the previous valid selection.");
            // Exercise the same field application and persistence as Save dock without manager Reload/native windows.
            Invoke(window, "ApplyFields", zone);
            fixture.Manager.SaveAppearanceSettings();
            Assert(fixture.Store.LoadReadOnly().Zones[0].Appearance.HeaderIconPath == path && other.Appearance.HeaderIcon == DockHeaderIcon.Pandora,
                "Saving a custom icon did not stay scoped to the selected dock.");
            Assert(SettingsJson(fixture.Manager.Workspace.Settings) == originalSettings, "Dock icon changes altered global product preferences.");
        }
        finally { window.Close(); }
        var reopened = new SettingsWindow(fixture.Manager);
        try
        {
            Assert(Find<TextBox>(reopened, "DockHeaderIconPathTextBox").Text == path &&
                ((ComboBoxItem)Find<ComboBox>(reopened, "DockHeaderIconComboBox").SelectedItem).Tag?.ToString() == "Custom",
                "Reopened settings lost the custom image choice.");
            SelectTag(Find<ComboBox>(reopened, "DockHeaderIconComboBox"), "None");
            Invoke(reopened, "ApplyFields", zone);
            fixture.Manager.SaveAppearanceSettings();
            Assert(fixture.Store.LoadReadOnly().Zones[0].Appearance.HeaderIcon == DockHeaderIcon.None && zone.Appearance.HeaderIconPath == path,
                "None did not persist or discarded the user's previous image path.");
            Assert(Find<Image>(reopened, "DockIconPreview").Visibility == Visibility.Collapsed, "None left an empty preview tile.");
            zone.Appearance.HeaderIcon = DockHeaderIcon.Custom;
            zone.Appearance.HeaderIconPath = Path.Combine(fixture.Root, "unavailable.png");
            reopened.RefreshFromWorkspace();
            Assert(Find<TextBlock>(reopened, "DockIconStatusText").Text.Contains("using Pandora", StringComparison.Ordinal),
                "Settings did not explain the fallback for a missing persisted image.");
            var navigation = Find<ListBox>(reopened, "SettingsNavigation");
            navigation.SelectedItem = navigation.Items.OfType<ListBoxItem>().Single(item => item.Tag?.ToString() == "Docks");
            Capture((FrameworkElement)reopened.Content, "dock-icon-settings", 1120, 900);
        }
        finally { reopened.Close(); }
    }

    private static void DockIconHeaderLayout()
    {
        using var fixture = new ManagerFixture(Path.Combine(_fixturePath, "dock-icon-layout"));
        var path = CreateDockIconImage(fixture.Root);
        foreach (var structure in Structures)
        foreach (var size in new[] { "Compact", "Standard", "Large" })
        {
            fixture.Manager.Workspace.Settings.DockTheme = structure;
            fixture.Manager.Workspace.Settings.DockBarSize = size;
            ThemeService.Apply(fixture.Manager.Workspace.Settings);
            var zone = CreateFixtureDock(fixture, "Personal workspace", 1);
            using var vm = new ZoneViewModel(zone, fixture.Manager);
            var window = new ZoneWindow(vm, fixture.Manager);
            try
            {
                foreach (var width in new[] { 230d, 480d })
                foreach (var collapsed in new[] { false, true })
                {
                    zone.Bounds.Width = width;
                    zone.IsCollapsed = collapsed;
                    Invoke(window, "ApplyCollapsedState");
                    zone.Appearance.HeaderIcon = DockHeaderIcon.Custom;
                    zone.Appearance.HeaderIconPath = path;
                    vm.RefreshHeaderIcon();
                    var content = (FrameworkElement)window.Content;
                    content.Measure(new Size(width, collapsed ? window.CollapsedVisualHeight : 300));
                    content.Arrange(new Rect(0, 0, width, collapsed ? window.CollapsedVisualHeight : 300));
                    content.UpdateLayout(); Drain();
                    var withIcon = Find<StackPanel>(window, "TitleStackPanel").ActualWidth;
                    Assert(Find<Image>(window, "HeaderIconImage").Source is BitmapSource { PixelWidth: 48 }, "Custom image was not bound into the actual dock header.");
                    zone.Appearance.HeaderIcon = DockHeaderIcon.None;
                    vm.RefreshHeaderIcon();
                    content.UpdateLayout(); Drain();
                    Assert(Find<Border>(window, "BrandTile").Visibility == Visibility.Collapsed &&
                        Find<StackPanel>(window, "TitleStackPanel").ActualWidth >= withIcon, "No-icon mode failed to reclaim header space.");
                    if (width == 480)
                        Assert(Find<StackPanel>(window, "TitleStackPanel").ActualWidth > withIcon + 20, "No-icon mode retained decorative icon spacing on a wide bar.");
                    var actions = Find<Border>(window, "HeaderControlsChrome");
                    var point = actions.TranslatePoint(new Point(0, 0), content);
                    Assert(point.X >= -1 && point.X + actions.ActualWidth <= width + 1, "Icon preference displaced narrow/rolled-up header actions.");
                    if (size == "Standard" && width == 480 && collapsed)
                        Capture(content, "dock-icon-none-" + structure, width, window.CollapsedVisualHeight);
                }
                zone.Appearance.HeaderIcon = DockHeaderIcon.Custom;
                zone.Appearance.HeaderIconPath = Path.Combine(fixture.Root, "missing.png");
                vm.RefreshHeaderIcon();
                Assert(vm.HasHeaderIcon && vm.BrandImage is not null && vm.StatusMessage.Contains("using Pandora", StringComparison.Ordinal),
                    "Missing custom image failed to recover visibly in the view model.");
            }
            finally { window.Close(); }
            vm.RefreshHeaderIcon(); // Closed/disposed views must not resume work.
        }
    }

    private static string CreateDockIconImage(string root)
    {
        var path = Path.Combine(root, "custom-header.png");
        var pixels = new byte[48 * 48 * 4];
        for (var index = 0; index < pixels.Length; index += 4)
        { pixels[index] = 120; pixels[index + 1] = 205; pixels[index + 2] = 90; pixels[index + 3] = 255; }
        var bitmap = BitmapSource.Create(48, 48, 96, 96, PixelFormats.Bgra32, null, pixels, 48 * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = File.Create(path);
        encoder.Save(output);
        return path;
    }
}
