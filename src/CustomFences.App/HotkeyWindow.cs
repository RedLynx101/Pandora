using System;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace CustomFences.App;

public sealed class HotkeyWindow : IDisposable
{
    private const int HotkeyId = 0x4650;
    private const int WmHotkey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint VkSpace = 0x20;

    private readonly Action _onHotkey;
    private readonly HwndSource _source;
    private bool _registered;

    public HotkeyWindow(Action onHotkey)
    {
        _onHotkey = onHotkey;
        var parameters = new HwndSourceParameters("CustomFencesHotkey")
        {
            Width = 0,
            Height = 0,
            WindowStyle = unchecked((int)0x80000000)
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WindowProcedure);
        _registered = RegisterHotKey(_source.Handle, HotkeyId, ModAlt | ModControl, VkSpace);
    }

    public void Dispose()
    {
        if (_registered)
        {
            UnregisterHotKey(_source.Handle, HotkeyId);
            _registered = false;
        }

        _source.RemoveHook(WindowProcedure);
        _source.Dispose();
    }

    private IntPtr WindowProcedure(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam.ToInt32() == HotkeyId)
        {
            _onHotkey();
            handled = true;
        }

        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr windowHandle, int id, uint modifiers, uint virtualKey);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr windowHandle, int id);
}
