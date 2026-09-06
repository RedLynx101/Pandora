using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Pandora.App;
using Pandora.Core;

namespace Pandora.App.Tests;

internal static partial class Program
{
    private static void RadioContentLayout()
    {
        using var fixture = new ManagerFixture(Path.Combine(_fixturePath, "radio-content"));
        var music = fixture.Manager.Workspace.Settings.Audio.MusicRootPath;
        Directory.CreateDirectory(Path.Combine(music, "Evening"));
        foreach (var title in new[] { "01 - Slow Orbit", "02 - A Long Track Name That Must Fit the Narrow Radio", "03 - Last Light" })
            File.WriteAllBytes(Path.Combine(music, "Evening", title + ".mp3"), []);
        var zone = new ZoneDefinition { Id = "radio-content", Name = "Radio", Kind = ZoneKind.Music, IsVisible = false };
        using var vm = new ZoneViewModel(zone, fixture.Manager);
        var window = new ZoneWindow(vm, fixture.Manager);
        try
        {
            Invoke(window, "RenderTabs");
            Invoke(window, "RenderMusicControls");
            Invoke(window, "ApplyContentMode");
            foreach (var palette in new[] { "LunarGlass", "Limestone" })
            foreach (var width in new[] { 230.0, 300, 480 })
            foreach (var height in width == 230 ? new[] { 420.0 } : new[] { 300.0, 420 })
            {
                ThemeService.Apply(palette, 0.64, true, "Classic", null, null);
                Invoke(window, "ApplyWindowBounds", new ZoneBounds { X = 0, Y = 0, Width = width, Height = height });
                var content = (FrameworkElement)window.Content;
                content.Measure(new Size(width, height));
                content.Arrange(new Rect(0, 0, width, height));
                content.UpdateLayout();
                Invoke(window, "UpdateRadioContentLayout");
                Capture(content, "radio-" + palette + "-" + width + "x" + height, width, height);
                var panel = Find<Grid>(window, "MusicPanel");
                var list = Find<ListBox>(window, "MusicTracksList");
                Assert(panel.Background is SolidColorBrush p && p.Color.A == 0, "Radio panel must not paint an opaque fill.");
                Assert(list.Background is SolidColorBrush l && l.Color.A == 0, "Track list must preserve dock translucency.");
                foreach (var name in new[] { "MusicPlaylistPicker", "RepeatComboBox", "MusicVolumeSlider", "MusicVisualizerButton" })
                {
                    var control = Find<FrameworkElement>(window, name);
                    var origin = control.TranslatePoint(new Point(), panel);
                    Assert(origin.X >= -0.1 && origin.X + control.ActualWidth <= panel.ActualWidth + 0.1,
                        $"Radio content clips horizontally: {name}/{palette}/{width}; {origin.X}+{control.ActualWidth}>{panel.ActualWidth}");
                    Assert(origin.Y >= -0.1 && origin.Y + control.ActualHeight <= panel.ActualHeight + 0.1, "Radio control clips vertically: " + name);
                }
                Assert(list.ActualHeight > 60, "Track list must remain usable.");
            }
        }
        finally { window.Close(); }
    }
}
