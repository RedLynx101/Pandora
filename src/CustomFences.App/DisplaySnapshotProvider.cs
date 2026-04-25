using System.Collections.Generic;
using CustomFences.Core;
using Forms = System.Windows.Forms;

namespace CustomFences.App;

public static class DisplaySnapshotProvider
{
    public static IReadOnlyList<DisplayDescriptor> GetDisplays()
    {
        var displays = new List<DisplayDescriptor>();
        foreach (var screen in Forms.Screen.AllScreens)
        {
            displays.Add(new DisplayDescriptor
            {
                DeviceName = screen.DeviceName,
                IsPrimary = screen.Primary,
                BoundsX = screen.Bounds.X,
                BoundsY = screen.Bounds.Y,
                BoundsWidth = screen.Bounds.Width,
                BoundsHeight = screen.Bounds.Height,
                WorkAreaX = screen.WorkingArea.X,
                WorkAreaY = screen.WorkingArea.Y,
                WorkAreaWidth = screen.WorkingArea.Width,
                WorkAreaHeight = screen.WorkingArea.Height
            });
        }

        return displays;
    }
}
