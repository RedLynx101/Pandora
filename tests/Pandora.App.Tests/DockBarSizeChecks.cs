using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Pandora.App;
using Pandora.Core;

namespace Pandora.App.Tests;

internal static partial class Program
{
    private static void DockBarSizeGeometry()
    {
        Assert(DockBarSizing.Normalize(" cOMpact ") == "Compact" && DockBarSizing.Normalize("invalid") == "Standard"
            && DockBarSizing.Normalize(null) == "Standard", "Bar sizes must normalize safely.");
        using var fixture = new ManagerFixture(Path.Combine(_fixturePath, "bar-sizes"));
        foreach (var structure in Structures)
        foreach (var edge in new[] { DockExpansionEdge.Top, DockExpansionEdge.Bottom })
        foreach (var tabCount in new[] { 1, 6 })
        {
            ThemeService.Apply("LunarGlass", 0.73, true, structure, null, null);
            var zone = CreateFixtureDock(fixture, "Project workspace", tabCount);
            var originalTabs = zone.Tabs.Select(t => t.Id).ToArray();
            var originalBounds = CopyBounds(zone.Bounds);
            WorkspaceLayoutService.SetExpansionEdge(fixture.Manager.Workspace, zone.Id, edge);
            using var vm = new ZoneViewModel(zone, fixture.Manager);
            var selectedTab = vm.SelectedTab?.Id;
            var window = new ZoneWindow(vm, fixture.Manager);
            Invoke(window, "RenderTabs");
            try
            {
                foreach (var size in new[] { "Compact", "Standard", "Large" })
                foreach (var collapsed in new[] { false, true })
                {
                    zone.IsCollapsed = collapsed;
                    ThemeService.Apply("LunarGlass", 0.73, true, structure, null, null, size);
                    Invoke(window, "ApplyWindowBounds", originalBounds);
                    var metrics = DockBarSizing.Get(DockThemeCatalog.Get(structure), size);
                    var content = (FrameworkElement)window.Content;
                    var height = collapsed ? window.CollapsedVisualHeight : 380;
                    content.Measure(new Size(300, height));
                    content.Arrange(new Rect(0, 0, 300, height));
                    content.UpdateLayout();
                    Drain();
                    // A hidden Window has no compositor layout pass after deferred resource
                    // invalidation; settle the same second measure a visible window receives.
                    content.InvalidateMeasure();
                    content.Measure(new Size(300, height));
                    content.Arrange(new Rect(0, 0, 300, height));
                    content.UpdateLayout();
                    Drain();
                    var header = Find<Border>(window, "HeaderBorder");
                    var title = Descendants(header).OfType<TextBlock>().Single(t => t.Text == zone.Name);
                    Assert(Near(vm.HeaderHeight, metrics.Height) && Near(header.ActualHeight, metrics.Height), "Tab count must not create a second name-bar level.");
                    Assert(Near(title.FontSize, metrics.TitleFontSize) && title.TextWrapping == TextWrapping.NoWrap, "Title size must follow the selected single-line bar size.");
                    foreach (var name in new[] { "SearchButton", "MoreButton", "CollapseButton" })
                    {
                        var button = Find<Button>(window, name);
                        var point = button.TranslatePoint(new Point(0, 0), header);
                        Assert(Near(button.ActualHeight, metrics.ControlSize) && Near(button.FontSize, metrics.GlyphSize), "Header controls must scale with the bar.");
                        Assert(point.Y >= -0.1 && point.Y + button.ActualHeight <= header.ActualHeight + 0.1, "Header control clips vertically.");
                    }
                    var navigation = Find<Border>(window, "NavigationHost");
                    Assert(navigation.Visibility == (!collapsed && tabCount > 1 ? Visibility.Visible : Visibility.Collapsed), "Tabs must only appear in an expanded multi-tab dock.");
                    Assert(!Descendants(header).Contains(Find<StackPanel>(window, "TabsPanel")), "Legacy tabs still occupy the name bar.");
                    Assert(zone.Tabs.Select(t => t.Id).SequenceEqual(originalTabs) && vm.SelectedTab?.Id == selectedTab && vm.Items.Count == 3,
                        "Bar size/layout change altered tabs, active selection or content.");
                    var remembered = InvokeResult<ZoneBounds>(window, "GetCurrentWindowBounds");
                    Assert(Near(remembered.Height, originalBounds.Height) && Near(remembered.Y, originalBounds.Y), "Bar sizing changed remembered expanded bounds.");
                    if (collapsed) AssertCollapsedProjection(window, vm, originalBounds, window.CollapsedVisualHeight, edge);
                    if (!collapsed && tabCount > 1)
                    {
                        var scroll = Find<ScrollViewer>(window, "NavigationScroll");
                        Assert(scroll.ComputedHorizontalScrollBarVisibility == Visibility.Visible, "Overflow fixture must exercise a tab scrollbar.");
                        var scrollbar = Descendants(scroll).OfType<ScrollBar>().Single(b => b.Orientation == Orientation.Horizontal);
                        Assert(scrollbar.ActualWidth >= scroll.ActualWidth - 1, "Horizontal scrollbar retained the vertical scrollbar's narrow width.");
                        scroll.ScrollToHorizontalOffset(20);
                        Drain();
                        Assert(scroll.HorizontalOffset > 0, "Overflowing tabs must remain horizontally accessible.");
                        scroll.ScrollToHorizontalOffset(0);
                        Drain();
                        var tabs = Find<StackPanel>(window, "TabsPanel");
                        Assert(scroll.ViewportHeight >= tabs.DesiredSize.Height - 0.1,
                            $"Horizontal overflow clips tab labels: {structure}/{size}/{edge}, viewport={scroll.ViewportHeight}, desired={tabs.DesiredSize.Height}, host={navigation.ActualHeight}, scroll={scroll.ActualHeight}.");
                    }
                    if (edge == DockExpansionEdge.Top && tabCount == 6)
                        Capture(content, $"bar-size-{structure}-{size}-{(collapsed ? "closed" : "tabs")}", 300, height);
                }
            }
            finally { window.Close(); }
        }
    }

    private static void DockBarSizeSettings()
    {
        using var fixture = new ManagerFixture(Path.Combine(_fixturePath, "bar-settings"));
        var window = new SettingsWindow(fixture.Manager);
        var initial = File.ReadAllBytes(fixture.Store.WorkspacePath);
        var choices = Find<ComboBox>(window, "DockBarSizeComboBox");
        foreach (var size in new[] { "Compact", "Large" })
        {
            SelectTag(choices, size);
            Assert(ThemeService.EffectiveDockBarSize == size && File.ReadAllBytes(fixture.Store.WorkspacePath).SequenceEqual(initial), "Bar preview must be live and unsaved.");
            Invoke(window, "RevertTheme_Click", window, new RoutedEventArgs());
            Assert(ThemeService.EffectiveDockBarSize == "Standard", "Revert did not restore bar size.");
        }
        SelectTag(choices, "Compact");
        Invoke(window, "SaveAppearance_Click", window, new RoutedEventArgs());
        Assert(fixture.Store.LoadOrCreate().Settings.DockBarSize == "Compact", "Applied bar size was not persisted.");
        SelectTag(choices, "Large");
        using (var guard = new FileStream(fixture.Store.WorkspacePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            Invoke(window, "SaveAppearance_Click", window, new RoutedEventArgs());
        Assert(fixture.Manager.Workspace.Settings.DockBarSize == "Compact" && ThemeService.EffectiveDockBarSize == "Compact"
            && Equals(((ComboBoxItem)choices.SelectedItem).Tag, "Compact"), "Failed save did not roll back the bar size and visible selection.");
        SelectTag(choices, "Large");
        window.Close();
        Assert(ThemeService.EffectiveDockBarSize == "Compact", "Closing an unsaved size preview failed to revert.");
        var reopened = new SettingsWindow(fixture.Manager);
        Assert(Equals(((ComboBoxItem)Find<ComboBox>(reopened, "DockBarSizeComboBox").SelectedItem).Tag, "Compact"), "Reopening Settings forgot bar size.");
        Capture((FrameworkElement)reopened.Content, "settings-bar-size-compact", 960, 740);
        reopened.Close();
    }
}
