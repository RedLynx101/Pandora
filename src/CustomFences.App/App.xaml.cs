using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using CustomFences.Core;
using Forms = System.Windows.Forms;

namespace CustomFences.App;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = "CustomFences.SingleInstance";
    private const string ShowSettingsSignalName = "CustomFences.ShowSettings";

    private Mutex? _mutex;
    private EventWaitHandle? _showSettingsSignal;
    private Thread? _signalThread;
    private DesktopZoneManager? _manager;
    private Forms.NotifyIcon? _trayIcon;
    private HotkeyWindow? _hotkeyWindow;
    private volatile bool _isExiting;
    private bool _desktopIconsHidden;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        _mutex = new Mutex(true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            SignalExistingInstance();
            Shutdown();
            return;
        }

        _showSettingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsSignalName);
        StartSignalListener();

        var store = WorkspaceStore.ForCurrentUser();
        _manager = new DesktopZoneManager(store);
        _manager.CleanDesktopModeChanged += enabled =>
        {
            if (enabled)
            {
                HideDesktopIcons();
            }
            else
            {
                RestoreDesktopIcons();
            }
        };
        _manager.Start();
        ApplyDesktopIconMode();

        _trayIcon = CreateTrayIcon(store);
        _hotkeyWindow = new HotkeyWindow(() => Dispatcher.Invoke(() => _manager.TogglePeek()));

        if (e.Args.Any(arg => string.Equals(arg, "--settings", StringComparison.OrdinalIgnoreCase)))
        {
            _manager.ShowSettings();
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _isExiting = true;
        _showSettingsSignal?.Set();
        _signalThread?.Join(1000);
        _hotkeyWindow?.Dispose();
        _trayIcon?.Dispose();
        _manager?.Dispose();
        RestoreDesktopIcons();
        _showSettingsSignal?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }

    private void StartSignalListener()
    {
        if (_showSettingsSignal is null)
        {
            return;
        }

        _signalThread = new Thread(() =>
        {
            while (!_isExiting)
            {
                if (_showSettingsSignal.WaitOne(500) && !_isExiting)
                {
                    Dispatcher.BeginInvoke(() => _manager?.ShowSettings());
                }
            }
        })
        {
            IsBackground = true,
            Name = "CustomFences single-instance listener"
        };
        _signalThread.Start();
    }

    private static void SignalExistingInstance()
    {
        try
        {
            using var signal = EventWaitHandle.OpenExisting(ShowSettingsSignalName);
            signal.Set();
        }
        catch
        {
            // If the running instance is still starting or already exiting, there is nothing else to do.
        }
    }

    private Forms.NotifyIcon CreateTrayIcon(WorkspaceStore store)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Settings", null, (_, _) => Dispatcher.Invoke(() => _manager?.ShowSettings()));
        menu.Items.Add("Set Docks Behind Windows", null, (_, _) => Dispatcher.Invoke(() => _manager?.TogglePeek()));
        menu.Items.Add("Hide Desktop Icons", null, (_, _) => Dispatcher.Invoke(HideDesktopIcons));
        menu.Items.Add("Show Desktop Icons", null, (_, _) => Dispatcher.Invoke(RestoreDesktopIcons));
        menu.Items.Add("Reload Workspace", null, (_, _) => Dispatcher.Invoke(() => _manager?.Reload()));
        menu.Items.Add("Open Workspace JSON", null, (_, _) => OpenPath(store.WorkspacePath));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Dispatcher.Invoke(Shutdown));

        var trayIcon = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon() ?? System.Drawing.SystemIcons.Application,
            Text = "OrbitDock",
            ContextMenuStrip = menu,
            Visible = true
        };

        trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(() => _manager?.ShowSettings());
        return trayIcon;
    }

    private void ApplyDesktopIconMode()
    {
        if (_manager?.Workspace.Settings.HideDesktopIconsWhenRunning == true)
        {
            HideDesktopIcons();
        }
    }

    private void HideDesktopIcons()
    {
        _desktopIconsHidden = DesktopIconVisibility.TrySetVisible(false) || _desktopIconsHidden;
    }

    private void RestoreDesktopIcons()
    {
        DesktopIconVisibility.TrySetVisible(true);
        _desktopIconsHidden = false;
    }

    private static System.Drawing.Icon? LoadTrayIcon()
    {
        try
        {
            var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Brand", "OrbitDock.ico");
            return File.Exists(iconPath) ? new System.Drawing.Icon(iconPath) : null;
        }
        catch
        {
            return null;
        }
    }

    private static void OpenPath(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch
        {
            // Tray actions are conveniences and should not bring down the desktop shell.
        }
    }
}
