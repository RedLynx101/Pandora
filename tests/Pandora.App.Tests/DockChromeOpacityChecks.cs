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
    private static void DockChromeOpacity()
    {
        using var fixture = new ManagerFixture(Path.Combine(_fixturePath, "header-opacity"));
        var zone = CreateFixtureDock(fixture, "Glass workspace", 1);
        using var vm = new ZoneViewModel(zone, fixture.Manager);
        var window = new ZoneWindow(vm, fixture.Manager);
        try
        {
            foreach (var structure in Structures)
            foreach (var palette in Palettes)
            foreach (var opacity in new[] { 0.55, 0.73, 1.0 })
            {
                ThemeService.Apply(palette, opacity, true, structure, null, null);
                Drain();
                var header = Find<Border>(window, "HeaderBorder");
                var actual = (SolidColorBrush)header.Background;
                Assert(Math.Abs(actual.Opacity - ThemeService.DockOpacity) < 0.001, "Header did not follow live global opacity.");
                Assert(actual.Color == Brush("Surface").Color && Brush("Surface").Opacity == 1,
                    "Header opacity must not mutate palette colors or shared Settings/menu brushes.");
                Assert(header.Opacity == 1 && window.Opacity == 1 && Find<Grid>(window, "HeaderGrid").Opacity == 1,
                    "Header children/window must not be faded with the background.");
                var title = Descendants(header).OfType<TextBlock>().Single(t => t.Text == zone.Name);
                Assert(title.Opacity == 1 && title.Foreground is SolidColorBrush titleBrush && titleBrush.Opacity == 1 && titleBrush.Color.A == 255,
                    "Dock title must keep opaque text.");
                var footer = (SolidColorBrush)Find<Border>(window, "StatusBorder").Background;
                Assert(structure == "Halo" ? footer.Color.A == 0 : Math.Abs(footer.Opacity - ThemeService.DockOpacity) < 0.001,
                    "Footer must share opacity or use Halo's already translucent body underneath.");
            }

            foreach (var custom in new[]
            {
                new ZoneAppearance { Opacity = 0.65 },
                new ZoneAppearance { BackgroundColor = "#123456", Opacity = 0.88 },
                new ZoneAppearance { Opacity = 0.1 },
                new ZoneAppearance { Opacity = double.NaN },
                new ZoneAppearance { Opacity = double.PositiveInfinity }
            })
            {
                var body = ThemeService.GetDockBackground(custom);
                var chrome = ThemeService.GetDockChrome(custom);
                Assert(double.IsFinite(chrome.Opacity) && Math.Abs(chrome.Opacity - body.Opacity) < 0.001,
                    "Header/body must resolve custom or malformed opacity identically.");
                if (ThemeService.IsHighContrast) Assert(chrome.Opacity == 1, "High contrast must remain opaque.");
            }

            foreach (var structure in Structures)
            foreach (var palette in new[] { "LunarGlass", "Limestone" })
            foreach (var collapsed in new[] { false, true })
            foreach (var opacity in new[] { 0.55, 1.0 })
            {
                zone.IsCollapsed = collapsed;
                ThemeService.Apply(palette, opacity, true, structure, null, null);
                Invoke(window, "ApplyWindowBounds", zone.Bounds);
                var name = $"header-opacity-{structure}-{palette}-{(collapsed ? "closed" : "open")}-{opacity * 100:0}";
                Capture((FrameworkElement)window.Content, name, 540, collapsed ? window.CollapsedVisualHeight : 380);
                var header = Find<Border>(window, "HeaderBorder");
                var blankPoint = header.TranslatePoint(new Point(340, header.ActualHeight / 2), (UIElement)window.Content);
                using var stream = File.OpenRead(Path.Combine(_runPath, name + ".png"));
                var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                var pixel = new byte[4];
                frame.CopyPixels(new Int32Rect((int)blankPoint.X, (int)blankPoint.Y, 1, 1), pixel, 4, 0);
                Assert(ThemeService.IsHighContrast || opacity == 1 ? pixel[3] == 255 : pixel[3] is > 0 and < 245,
                    $"Header render has unexpected alpha {pixel[3]}: {name}. An opaque backing may hide transparency.");
                if (collapsed) Assert(Near(window.Height, window.CollapsedVisualHeight), "Transparency changed rolled-up height.");
                if (collapsed && palette == "LunarGlass")
                    Capture((FrameworkElement)window.Content, name + "-synthetic-backdrop", 540, window.CollapsedVisualHeight,
                        backdrop: new LinearGradientBrush(Color.FromRgb(33, 73, 82), Color.FromRgb(187, 151, 87), 0));
            }
        }
        finally { window.Close(); }
    }
}
