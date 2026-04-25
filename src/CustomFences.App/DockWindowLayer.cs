using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CustomFences.App;

public static class DockWindowLayer
{
    private static readonly IntPtr HwndBottom = new(1);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;

    public static void SendBehindNormalWindows(Window window)
    {
        try
        {
            window.Topmost = false;
            var handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            SetWindowPos(handle, HwndBottom, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder);
        }
        catch
        {
            // Z-order fallback failure should not affect dock usability.
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr windowHandle,
        IntPtr insertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);
}
