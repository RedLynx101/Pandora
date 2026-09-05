namespace Pandora.Core;

/// <summary>Keep persisted expanded geometry separate from the window's rolled-up projection.</summary>
public static class DockBoundsProjection
{
    public static ZoneBounds ToVisible(ZoneBounds expanded, bool collapsed, DockExpansionEdge edge, double collapsedHeight)
    {
        var height = collapsed ? collapsedHeight : expanded.Height;
        return new ZoneBounds
        {
            X = expanded.X,
            Y = collapsed && edge == DockExpansionEdge.Bottom ? expanded.Y + expanded.Height - height : expanded.Y,
            Width = expanded.Width,
            Height = height
        };
    }

    public static ZoneBounds ToExpanded(ZoneBounds visible, bool collapsed, DockExpansionEdge edge, double expandedHeight) => new()
    {
        X = visible.X,
        Y = collapsed && edge == DockExpansionEdge.Bottom ? visible.Y + visible.Height - expandedHeight : visible.Y,
        Width = visible.Width,
        Height = collapsed ? expandedHeight : visible.Height
    };
}
