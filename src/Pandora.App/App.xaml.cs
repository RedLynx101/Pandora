using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using Pandora.Core;
using Forms = System.Windows.Forms;

namespace Pandora.App;

public partial class App : System.Windows.Application
{
    private const string SingleInstanceMutexName = "Pandora.SingleInstance";
    private const string ShowSettingsSignalName = "Pandora.ShowSettings";
    private const string ExitSignalName = "Pandora.Exit";

    private Mutex? _mutex;
    private EventWaitHandle? _showSettingsSignal;
    private EventWaitHandle? _exitSignal;
    private Thread? _signalThread;
    private DesktopZoneManager? _manager;
    private Forms.NotifyIcon? _trayIcon;
    private HotkeyWindow? _hotkeyWindow;
    private volatile bool _isExiting;
    private bool _desktopIconsHidden;

    protected override void OnStartup(System.Windows.StartupEventArgs e)
    {
        base.OnStartup(e);

        if (e.Args.Contains("--supervise", StringComparer.OrdinalIgnoreCase))
        {
            Shutdown(StartupSupervisor.Run());
            return;
        }

        if (e.Args.Contains("--exit", StringComparer.OrdinalIgnoreCase))
        {
            if (EventWaitHandle.TryOpenExisting(ExitSignalName, out var signal))
            {
                using (signal) signal.Set();
            }
            Shutdown();
            return;
        }

        // Route ordinary launches through the installed task so crash recovery
        // remains active even after a deliberate exit and later manual restart.
        if (!e.Args.Contains("--scheduled", StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                using var scheduled = StartupScheduledTask.Open();
                if (scheduled?.Enabled == true)
                {
                    scheduled.Run();
                    if (e.Args.Contains("--settings", StringComparer.OrdinalIgnoreCase))
                    {
                        SpinWait.SpinUntil(() =>
                        {
                            if (!EventWaitHandle.TryOpenExisting(ShowSettingsSignalName, out var ready)) return false;
                            ready.Dispose();
                            return true;
                        }, TimeSpan.FromSeconds(10));
                    }
                    SignalExistingInstance();
                    Shutdown();
                    return;
                }
            }
            catch (Exception ex) { Debug.WriteLine("Scheduled launch unavailable: " + ex.Message); }
        }

        _mutex = new Mutex(true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            if (!e.Args.Contains("--scheduled", StringComparer.OrdinalIgnoreCase)) SignalExistingInstance();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += HandleDispatcherStorageError;
        try
        {
            _showSettingsSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSettingsSignalName);
            _exitSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ExitSignalName);
            StartSignalListener();

            var store = WorkspaceStore.ForCurrentUser();
            RuntimeDiagnostics.Initialize(store.WorkspacePath);
            AppDomain.CurrentDomain.UnhandledException += RecordUnhandledFailure;
            _manager = new DesktopZoneManager(store);
            _manager.StorageError += ShowStorageError;
            _manager.CleanDesktopModeChanged += enabled =>
            {
                if (enabled) HideDesktopIcons(); else RestoreDesktopIcons();
            };
            // Validate and persist the initial display state before hiding any
            // Windows icons. A bad/unavailable workspace leaves the shell intact.
            _manager.Start();
            RepairStartupRegistration();
            ApplyDesktopIconMode();

            _trayIcon = CreateTrayIcon(store);
            ThemeService.ThemeChanged += RefreshTrayIcon;
            _hotkeyWindow = new HotkeyWindow(() => Dispatcher.Invoke(() => _manager.TogglePeek()));

            if (EventWaitHandle.TryOpenExisting("Pandora.StartupReady", out var ready))
            {
                using (ready) ready.Set();
            }

            if (e.Args.Any(arg => string.Equals(arg, "--settings", StringComparison.OrdinalIgnoreCase)))
            {
                _manager.ShowSettings();
            }
        }
        catch (Exception ex) when (DesktopZoneManager.IsExpectedStorageFailure(ex))
        {
            RuntimeDiagnostics.RecordFailure(ex);
            ShowStorageError("Pandora could not open its workspace. Check the file and its permissions, then start Pandora again. " + ex.Message);
            Shutdown(1);
        }
    }

    protected override void OnExit(System.Windows.ExitEventArgs e)
    {
        _isExiting = true;
        try
        {
            _showSettingsSignal?.Set();
            _signalThread?.Join(1000);
            _hotkeyWindow?.Dispose();
            ThemeService.ThemeChanged -= RefreshTrayIcon;
            _trayIcon?.Dispose();
            DispatcherUnhandledException -= HandleDispatcherStorageError;
            if (_manager is not null) _manager.StorageError -= ShowStorageError;
            _manager?.Dispose();
            RuntimeDiagnostics.Record("shutdown", new { at = DateTimeOffset.UtcNow, exitCode = e.ApplicationExitCode });
            AppDomain.CurrentDomain.UnhandledException -= RecordUnhandledFailure;
        }
        finally
        {
            RestoreDesktopIcons();
            _showSettingsSignal?.Dispose();
            _exitSignal?.Dispose();
            _mutex?.Dispose();
            base.OnExit(e);
        }
    }

    private void HandleDispatcherStorageError(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        RuntimeDiagnostics.RecordFailure(e.Exception);
        // Programming failures remain unhandled. Expected file/permission/JSON
        // failures reach this boundary only if a local editor did not handle them.
        if (!DesktopZoneManager.IsExpectedStorageFailure(e.Exception)) return;
        e.Handled = true;
        var message = "Pandora could not complete this operation. Review the error before retrying. " + e.Exception.Message;
        if (_manager is not null) _manager.ReportStorageError(message);
        else ShowStorageError(message);
    }

    private static void ShowStorageError(string message) =>
        MessageBox.Show(message, "Pandora workspace", MessageBoxButton.OK, MessageBoxImage.Warning);

    private static void RecordUnhandledFailure(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception error) RuntimeDiagnostics.RecordFailure(error);
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
                if (_exitSignal?.WaitOne(0) == true)
                {
                    Dispatcher.BeginInvoke(() => Shutdown(0));
                    return;
                }
                if (_showSettingsSignal.WaitOne(500) && !_isExiting)
                {
                    Dispatcher.BeginInvoke(() => _manager?.ShowSettings());
                }
            }
        })
        {
            IsBackground = true,
            Name = "Pandora single-instance listener"
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
        var trayIcon = new Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(_manager?.Workspace.Settings.IconStyle) ?? System.Drawing.SystemIcons.Application,
            Text = "Pandora",
            Visible = true
        };

        trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(() => _manager?.ShowSettings());
        trayIcon.MouseUp += (_, e) =>
        {
            if (e.Button != Forms.MouseButtons.Right) return;
            Dispatcher.Invoke(() =>
            {
                var menu = new ContextMenu { Placement = PlacementMode.MousePoint, MinWidth = 235 };
                menu.Items.Add(new MenuItem { Header = "Pandora", IsEnabled = false });
                menu.Items.Add(ActionItem("_Projects", () => _manager?.ShowProjects()));
                menu.Items.Add(ActionItem("_Settings…", () => _manager?.ShowSettings()));
                menu.Items.Add(new Separator());
                menu.Items.Add(ActionItem("Restore dock _layer", () => _manager?.TogglePeek()));
                menu.Items.Add(ActionItem("_Reload workspace", () => _manager?.Reload()));
                var desktop = new MenuItem { Header = "Desktop _icons" };
                desktop.Items.Add(ActionItem("Show", () => SetDesktopIconPreference(false)));
                desktop.Items.Add(ActionItem("Hide while Pandora runs", () => SetDesktopIconPreference(true)));
                menu.Items.Add(desktop);
                var advanced = new MenuItem { Header = "_Advanced" };
                advanced.Items.Add(ActionItem("Open workspace JSON", () => OpenPath(store.WorkspacePath)));
                menu.Items.Add(advanced);
                menu.Items.Add(new Separator());
                menu.Items.Add(ActionItem("E_xit Pandora", Shutdown));
                menu.IsOpen = true;
            });
        };
        return trayIcon;
    }

    private static MenuItem ActionItem(string text, Action action)
    {
        var item = new MenuItem { Header = text };
        item.Click += (_, _) => action();
        return item;
    }

    private void SetDesktopIconPreference(bool hide)
    {
        if (_manager is null) return;
        var previous = _manager.Workspace.Settings.HideDesktopIconsWhenRunning;
        _manager.Workspace.Settings.HideDesktopIconsWhenRunning = hide;
        try
        {
            _manager.Save();
        }
        catch
        {
            _manager.Workspace.Settings.HideDesktopIconsWhenRunning = previous;
            throw;
        }
        if (hide) HideDesktopIcons(); else RestoreDesktopIcons(force: true);
    }

    private void RefreshTrayIcon(object? sender, EventArgs e)
    {
        if (_trayIcon is null) return;
        var next = LoadTrayIcon(_manager?.Workspace.Settings.IconStyle);
        if (next is null) return;
        var previous = _trayIcon.Icon;
        _trayIcon.Icon = next;
        previous?.Dispose();
    }

    private void ApplyDesktopIconMode()
    {
        if (_manager?.Workspace.Settings.HideDesktopIconsWhenRunning == true)
        {
            HideDesktopIcons();
        }
    }

    private void RepairStartupRegistration()
    {
        if (_manager?.Workspace.Settings.StartWithWindows != true || StartupAppService.IsRegistered())
        {
            return;
        }

        try
        {
            StartupAppService.SetEnabled(true, _manager.Workspace.Settings.IconStyle);
        }
        catch
        {
            // Settings exposes any startup registration problems when the user changes the option.
        }
    }

    private void HideDesktopIcons()
    {
        _desktopIconsHidden = DesktopIconVisibility.TrySetVisible(false) || _desktopIconsHidden;
    }

    private void RestoreDesktopIcons(bool force = false)
    {
        // A second instance never hid icons and must not undo the running
        // instance's clean-desktop mode when it exits after signaling settings.
        if (!_desktopIconsHidden && !force) return;
        DesktopIconVisibility.TrySetVisible(true);
        _desktopIconsHidden = false;
    }

    private static System.Drawing.Icon? LoadTrayIcon(string? style)
    {
        try
        {
            var iconPath = BrandIdentity.IconPath(style);
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
