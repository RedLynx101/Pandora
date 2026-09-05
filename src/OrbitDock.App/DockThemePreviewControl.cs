using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace OrbitDock.App;

/// <summary>
/// A small, synthetic dock illustration. Geometry comes from the same profiles as live docks;
/// colors come from the active preview palette. It never reads workspace content.
/// </summary>
public sealed class DockThemePreviewControl : FrameworkElement
{
    public static readonly DependencyProperty DockThemeProperty = DependencyProperty.Register(
        nameof(DockTheme), typeof(string), typeof(DockThemePreviewControl),
        new FrameworkPropertyMetadata("Classic", FrameworkPropertyMetadataOptions.AffectsRender));

    public string DockTheme
    {
        get => (string)GetValue(DockThemeProperty);
        set => SetValue(DockThemeProperty, value);
    }

    protected override void OnRender(DrawingContext drawing)
    {
        base.OnRender(drawing);
        if (ActualWidth < 24 || ActualHeight < 24) return;
        var profile = DockThemeCatalog.Get(DockTheme);
        var scale = Math.Min((ActualWidth - 20) / 260, (ActualHeight - 10) / 148);
        var width = 260 * scale;
        var height = 148 * scale;
        var left = (ActualWidth - width) / 2;
        var top = (ActualHeight - height) / 2;
        var radius = profile.CornerRadius * scale;
        var headerHeight = profile.HeaderHeight * scale;
        var gap = profile.HeaderGap * scale;
        var background = Brush("Glass", Color.FromRgb(21, 26, 41));
        var surface = Brush("Surface", Color.FromRgb(28, 35, 51));
        var elevated = Brush("Elevated", Color.FromRgb(41, 50, 70));
        var accent = Brush("Accent", Color.FromRgb(193, 188, 255));
        var text = Brush("Text", Colors.White);
        var muted = Brush("Muted", Color.FromRgb(186, 195, 216));
        var border = new Pen(Brush("Border", Color.FromRgb(70, 81, 105)), 1);

        var bodyTop = profile.SeparatedHeader ? top + headerHeight + gap : top;
        var body = new Rect(left, bodyTop, width, height - (bodyTop - top));
        drawing.DrawRoundedRectangle(background, border, body, radius, radius);
        if (profile.SeparatedHeader)
        {
            drawing.DrawRoundedRectangle(surface, border, new Rect(left, top, width, headerHeight), headerHeight / 2, headerHeight / 2);
        }
        else
        {
            drawing.PushClip(new RectangleGeometry(new Rect(left, top, width, height), radius, radius));
            drawing.DrawRectangle(surface, null, new Rect(left, top, width, headerHeight));
            drawing.DrawLine(border, new Point(left + 1, top + headerHeight), new Point(left + width - 1, top + headerHeight));
            if (profile.AccentRailWidth > 0)
                drawing.DrawRectangle(accent, null, new Rect(left, top, Math.Max(2, profile.AccentRailWidth * scale), height));
            drawing.Pop();
        }

        var headerInset = profile.HeaderPadding.Left * scale;
        var midline = top + headerHeight / 2;
        var glyphLeft = left + headerInset;
        drawing.DrawEllipse(null, new Pen(accent, 1.2), new Point(glyphLeft + 4, midline), 3.5, 3.5);
        drawing.DrawLine(new Pen(accent, 1), new Point(glyphLeft + 4, midline - 5), new Point(glyphLeft + 4, midline + 5));
        DrawText(drawing, "Launchpad", text, new Point(glyphLeft + 13, midline - 6), 9.5);
        drawing.DrawEllipse(muted, null, new Point(left + width - 25, midline), 1, 1);
        drawing.DrawEllipse(muted, null, new Point(left + width - 21, midline), 1, 1);
        drawing.DrawEllipse(muted, null, new Point(left + width - 17, midline), 1, 1);

        var contentTop = top + headerHeight + gap + profile.ContentPadding.Top * scale + 3;
        var contentLeft = left + profile.ContentPadding.Left * scale;
        var contentWidth = width - profile.ContentPadding.Left * scale - profile.ContentPadding.Right * scale;
        var itemGap = Math.Max(3, profile.ItemGap * scale);
        var itemWidth = (contentWidth - itemGap * 2) / 3;
        var footerHeight = profile.FooterHeight * scale;
        var itemHeight = Math.Max(22, (top + height - footerHeight - contentTop) * 0.82);
        if (profile.Id == "Meridian")
        {
            var rowWidth = (contentWidth - itemGap) / 2;
            var rowHeight = Math.Max(12, (top + height - footerHeight - contentTop - itemGap) / 2);
            for (var index = 0; index < 3; index++)
            {
                var row = new Rect(contentLeft + (index % 2) * (rowWidth + itemGap),
                    contentTop + (index / 2) * (rowHeight + itemGap), rowWidth, rowHeight);
                drawing.DrawRoundedRectangle(elevated, null, row, profile.ItemCornerRadius * scale, profile.ItemCornerRadius * scale);
                drawing.DrawRoundedRectangle(index == 0 ? accent : muted, null,
                    new Rect(row.X + 4, row.Y + (row.Height - 7) / 2, 6, 7), 0.5, 0.5);
                drawing.DrawRoundedRectangle(muted, null, new Rect(row.X + 14, row.Y + row.Height / 2, row.Width - 21, 1.4), 0.7, 0.7);
            }
        }
        else
        {
            for (var index = 0; index < 3; index++)
            {
                var item = new Rect(contentLeft + index * (itemWidth + itemGap), contentTop, itemWidth, itemHeight);
                if (profile.Id == "Halo")
                    drawing.DrawRoundedRectangle(elevated, null, item, profile.ItemCornerRadius * scale, profile.ItemCornerRadius * scale);
                var iconX = item.X + item.Width / 2 - 4;
                var iconY = item.Y + 5;
                drawing.DrawRoundedRectangle(index == 0 ? accent : muted, null, new Rect(iconX, iconY, 8, 9), 1, 1);
                drawing.DrawRoundedRectangle(muted, null, new Rect(item.X + item.Width * 0.22, item.Bottom - 5, item.Width * 0.56, 1.4), 0.7, 0.7);
            }
        }
        drawing.DrawRoundedRectangle(muted, null, new Rect(contentLeft + 2, top + height - footerHeight / 2 - 1, width * 0.19, 1.5), 0.75, 0.75);
        drawing.DrawEllipse(accent, null, new Point(left + width - 12, top + height - footerHeight / 2), 1.8, 1.8);
    }

    private Brush Brush(string resource, Color fallback) => TryFindResource($"Pandora.{resource}Brush") as Brush ?? new SolidColorBrush(fallback);

    private void DrawText(DrawingContext drawing, string value, Brush color, Point origin, double size)
    {
        var text = new FormattedText(value, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI Semibold"), size, color, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        drawing.DrawText(text, origin);
    }
}
