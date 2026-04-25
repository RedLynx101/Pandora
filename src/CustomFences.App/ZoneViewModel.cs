using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CustomFences.Core;

namespace CustomFences.App;

public sealed class ZoneViewModel : INotifyPropertyChanged, IDisposable
{
    private const int MaximumItemsPerDock = 240;

    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
    private readonly DesktopZoneManager _manager;
    private readonly List<FileSystemWatcher> _watchers = [];
    private ZoneTabDefinition? _selectedTab;
    private string _statusMessage = string.Empty;

    public ZoneViewModel(ZoneDefinition zone, DesktopZoneManager manager)
    {
        _manager = manager;
        Zone = zone;
        var activeTabId = WorkspaceLayoutService.GetActiveTabId(manager.Workspace, zone);
        SelectedTab = zone.Tabs.FirstOrDefault(tab => string.Equals(tab.Id, activeTabId, StringComparison.OrdinalIgnoreCase))
            ?? zone.Tabs.FirstOrDefault();
        Refresh();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ZoneDefinition Zone { get; }
    public ObservableCollection<FileItemViewModel> Items { get; } = [];

    public IReadOnlyList<ZoneTabDefinition> Tabs => Zone.Tabs;
    public string Name => Zone.Name;
    public double IconSize => Zone.Appearance.IconSize;
    public double ItemWidth => Math.Max(78, Zone.Appearance.IconSize + 42);
    public double HeaderHeight => Tabs.Count > 1 ? 58 : 48;
    public System.Windows.Media.Brush AccentBrush => CreateBrush(Zone.Appearance.AccentColor, 1);
    public System.Windows.Media.Brush HeaderBrush => CreateBrush(Zone.Appearance.AccentColor, 0.40);
    public System.Windows.Media.Brush BackgroundBrush => CreateBrush(Zone.Appearance.BackgroundColor, Zone.Appearance.Opacity);
    public System.Windows.Media.Brush BorderBrush => CreateBrush(Zone.Appearance.AccentColor, 0.62);
    public CornerRadius CornerRadius => new(Zone.Appearance.CornerRadius);

    public ZoneTabDefinition? SelectedTab
    {
        get => _selectedTab;
        private set
        {
            if (_selectedTab == value)
            {
                return;
            }

            _selectedTab = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectedFolderPath));
        }
    }

    public string SelectedFolderPath => SelectedTab is null
        ? string.Empty
        : SelectedTab.Source == ZoneTabSource.SmartDesktop
            ? Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)
            : PathExpander.Expand(SelectedTab.Path);
    public bool IsSmartDock => SelectedTab?.Source == ZoneTabSource.SmartDesktop;
    public string DockId => Zone.Id;
    public string? SelectedTabId => SelectedTab?.Id;

    public string StatusMessage
    {
        get => _statusMessage;
        private set
        {
            if (_statusMessage == value)
            {
                return;
            }

            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public void SelectTab(ZoneTabDefinition tab)
    {
        SelectedTab = tab;
        WorkspaceLayoutService.CaptureActiveTab(_manager.Workspace, Zone, tab.Id);
        _manager.Save();
        Refresh();
    }

    public void Refresh()
    {
        StopWatchers();
        Items.Clear();

        if (SelectedTab is null)
        {
            StatusMessage = "No folder is assigned to this zone.";
            return;
        }

        if (SelectedTab.Source == ZoneTabSource.SmartDesktop)
        {
            RefreshSmartDesktop();
            return;
        }

        if (string.IsNullOrWhiteSpace(SelectedTab.Path))
        {
            StatusMessage = "No folder is assigned to this zone.";
            return;
        }

        var path = PathExpander.Expand(SelectedTab.Path);
        if (!Directory.Exists(path))
        {
            StatusMessage = $"Folder unavailable: {path}";
            return;
        }

        try
        {
            var entries = Directory.EnumerateFileSystemEntries(path)
                .Select(CreateFileSystemInfo)
                .Where(info => info is not null)
                .Cast<FileSystemInfo>();

            var items = Sort(entries, Zone.Sort).Select(info => new FileItemViewModel(info));
            foreach (var item in ApplyItemOverrides(items).Take(MaximumItemsPerDock))
            {
                Items.Add(item);
            }

            StatusMessage = Items.Count == 0 ? "Folder is empty." : $"{Items.Count} items";
            StartWatchers([path]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Cannot read folder: {ex.Message}";
        }
    }

    public void AddDroppedFiles(IReadOnlyCollection<string> paths, DropAction requestedAction)
    {
        if (paths.Count == 0 || string.IsNullOrWhiteSpace(SelectedFolderPath))
        {
            return;
        }

        if (IsSmartDock)
        {
            AddVirtualItems(paths);
            return;
        }

        if (!EnsureTargetFolder(SelectedFolderPath))
        {
            StatusMessage = "Drop target is unavailable.";
            return;
        }

        var completed = 0;
        foreach (var source in paths)
        {
            try
            {
                if (File.Exists(source))
                {
                    if (requestedAction == DropAction.Move)
                    {
                        File.Move(source, GetUniqueDestination(SelectedFolderPath, Path.GetFileName(source)));
                    }
                    else
                    {
                        File.Copy(source, GetUniqueDestination(SelectedFolderPath, Path.GetFileName(source)));
                    }

                    completed++;
                }
                else if (Directory.Exists(source))
                {
                    var destination = GetUniqueDestination(SelectedFolderPath, Path.GetFileName(source));
                    if (requestedAction == DropAction.Move)
                    {
                        Directory.Move(source, destination);
                    }
                    else
                    {
                        CopyDirectory(source, destination);
                    }

                    completed++;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                StatusMessage = $"Drop skipped: {ex.Message}";
            }
        }

        if (completed > 0)
        {
            StatusMessage = requestedAction == DropAction.Move
                ? $"Moved {completed} item(s)."
                : $"Copied {completed} item(s).";
            Refresh();
        }
    }

    public void AddVirtualItems(IReadOnlyCollection<string> paths)
    {
        if (SelectedTab is null || paths.Count == 0)
        {
            return;
        }

        var completed = 0;
        foreach (var path in paths.Where(path => !string.IsNullOrWhiteSpace(path)))
        {
            WorkspaceLayoutService.AddOrShowItem(_manager.Workspace, path, Zone.Id, SelectedTab.Id);
            completed++;
        }

        if (completed > 0)
        {
            _manager.Save();
            Refresh();
            StatusMessage = $"Added {completed} virtual item(s).";
        }
    }

    public bool MoveItemHere(string path, string? sourceDockId, string? sourceTabId, int targetIndex)
    {
        if (SelectedTab is null || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var sameDock = string.Equals(sourceDockId, Zone.Id, StringComparison.OrdinalIgnoreCase);
        var sameTab = string.IsNullOrWhiteSpace(sourceTabId) ||
                      string.Equals(sourceTabId, SelectedTab.Id, StringComparison.OrdinalIgnoreCase);
        if (sameDock && sameTab)
        {
            ReorderItem(path, targetIndex);
            return true;
        }

        if (IsSmartDock)
        {
            if (!string.IsNullOrWhiteSpace(sourceDockId))
            {
                WorkspaceLayoutService.MoveItem(_manager.Workspace, path, sourceDockId, sourceTabId, Zone.Id, SelectedTab.Id, targetIndex);
            }
            else
            {
                WorkspaceLayoutService.AddOrShowItem(_manager.Workspace, path, Zone.Id, SelectedTab.Id, targetIndex);
            }

            _manager.Save();
            Refresh();
            StatusMessage = "Moved item virtually.";
            return true;
        }

        if (File.Exists(path) || Directory.Exists(path))
        {
            AddDroppedFiles([path], _manager.Workspace.Settings.DefaultDropAction);
            return true;
        }

        StatusMessage = "Item no longer exists on disk.";
        return false;
    }

    public void ReorderItem(string path, int targetIndex)
    {
        if (SelectedTab is null || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var ordered = Items.Select(item => item.Path).ToList();
        var existingIndex = ordered.FindIndex(candidate => WorkspaceLayoutService.PathsEqual(candidate, path));
        if (existingIndex >= 0)
        {
            ordered.RemoveAt(existingIndex);
            if (existingIndex < targetIndex)
            {
                targetIndex--;
            }
        }

        var insertionIndex = Math.Clamp(targetIndex < 0 ? ordered.Count : targetIndex, 0, ordered.Count);
        ordered.Insert(insertionIndex, WorkspaceLayoutService.NormalizePath(path));
        WorkspaceLayoutService.SetItemOrder(_manager.Workspace, Zone.Id, SelectedTab.Id, ordered);
        _manager.Save();
        Refresh();
        StatusMessage = "Order saved.";
    }

    public void RemoveFromDock(string path)
    {
        if (SelectedTab is null || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        WorkspaceLayoutService.HideItemInDock(_manager.Workspace, path, Zone.Id, SelectedTab.Id);
        _manager.Save();
        Refresh();
        StatusMessage = "Removed from this dock. The real file was not touched.";
    }

    public void PinToDesktop(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var x = Math.Max(SystemParameters.VirtualScreenLeft + 80, Zone.Bounds.X - 92);
        var y = Math.Max(SystemParameters.VirtualScreenTop + 80, Zone.Bounds.Y);
        WorkspaceLayoutService.AddDesktopPin(_manager.Workspace, path, x, y, Zone.Appearance.IconSize);
        _manager.Save();
        _manager.ReloadDesktopPins();
        StatusMessage = "Pinned to desktop overlay.";
    }

    public void Dispose()
    {
        StopWatchers();
    }

    private void RefreshSmartDesktop()
    {
        try
        {
            var items = DesktopItemCatalog.Enumerate(SelectedTab!.DesktopGroup)
                .Select(info => new FileItemViewModel(info));
            foreach (var item in ApplyItemOverrides(items).Take(MaximumItemsPerDock))
            {
                Items.Add(item);
            }

            var label = SelectedTab.DesktopGroup switch
            {
                DesktopItemGroup.Apps => "apps",
                DesktopItemGroup.Dev => "dev tools",
                DesktopItemGroup.Creative => "creative apps",
                DesktopItemGroup.Games => "games",
                DesktopItemGroup.Web => "web apps",
                DesktopItemGroup.Utilities => "utilities",
                DesktopItemGroup.Folders => "folders",
                DesktopItemGroup.Files => "files",
                _ => "items"
            };

            StatusMessage = Items.Count == 0 ? $"No {label} found on the desktop." : $"{Items.Count} {label}";
            StartWatchers(DesktopItemCatalog.GetDesktopDirectories());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Cannot scan desktop: {ex.Message}";
        }
    }

    private IEnumerable<FileItemViewModel> ApplyItemOverrides(IEnumerable<FileItemViewModel> baseItems)
    {
        if (SelectedTab is null)
        {
            return baseItems;
        }

        var layout = WorkspaceLayoutService.EnsureActiveLayout(_manager.Workspace);
        var overrides = WorkspaceLayoutService.GetOverrides(layout, Zone.Id, SelectedTab.Id);
        var visible = new List<(FileItemViewModel Item, int? Order, int BaseIndex)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;

        foreach (var item in baseItems)
        {
            var normalized = WorkspaceLayoutService.NormalizePath(item.Path);
            if (WorkspaceLayoutService.IsHidden(layout, Zone.Id, SelectedTab.Id, normalized))
            {
                continue;
            }

            seen.Add(normalized);
            visible.Add((item, WorkspaceLayoutService.GetItemOrder(layout, Zone.Id, SelectedTab.Id, normalized), index++));
        }

        foreach (var itemOverride in overrides.Where(item => !item.IsHidden))
        {
            var normalized = WorkspaceLayoutService.NormalizePath(itemOverride.Path);
            if (!seen.Add(normalized))
            {
                continue;
            }

            visible.Add((new FileItemViewModel(normalized, itemOverride.DisplayName), itemOverride.Order, index++));
        }

        return visible
            .OrderBy(item => item.Order ?? int.MaxValue)
            .ThenBy(item => item.Order.HasValue ? 0 : item.BaseIndex)
            .ThenBy(item => item.Item.DisplayName, StringComparer.OrdinalIgnoreCase)
            .Select(item => item.Item);
    }

    private static FileSystemInfo? CreateFileSystemInfo(string path)
    {
        if (Directory.Exists(path))
        {
            return new DirectoryInfo(path);
        }

        return File.Exists(path) ? new FileInfo(path) : null;
    }

    private static IEnumerable<FileSystemInfo> Sort(IEnumerable<FileSystemInfo> entries, ItemSort sort)
    {
        return sort switch
        {
            ItemSort.NameDescending => entries.OrderByDescending(info => info.Name, StringComparer.OrdinalIgnoreCase),
            ItemSort.NewestFirst => entries.OrderByDescending(info => info.LastWriteTimeUtc),
            ItemSort.OldestFirst => entries.OrderBy(info => info.LastWriteTimeUtc),
            ItemSort.TypeThenName => entries
                .OrderBy(info => info.Attributes.HasFlag(FileAttributes.Directory) ? 0 : 1)
                .ThenBy(info => Path.GetExtension(info.Name), StringComparer.OrdinalIgnoreCase)
                .ThenBy(info => info.Name, StringComparer.OrdinalIgnoreCase),
            _ => entries.OrderBy(info => info.Name, StringComparer.OrdinalIgnoreCase)
        };
    }

    private void StartWatchers(IEnumerable<string> paths)
    {
        foreach (var path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var watcher = new FileSystemWatcher(path)
                {
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };
                watcher.Created += WatcherChanged;
                watcher.Deleted += WatcherChanged;
                watcher.Renamed += WatcherChanged;
                watcher.Changed += WatcherChanged;
                _watchers.Add(watcher);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                StatusMessage = $"Live refresh unavailable: {ex.Message}";
            }
        }
    }

    private void StopWatchers()
    {
        foreach (var watcher in _watchers)
        {
            watcher.Created -= WatcherChanged;
            watcher.Deleted -= WatcherChanged;
            watcher.Renamed -= WatcherChanged;
            watcher.Changed -= WatcherChanged;
            watcher.Dispose();
        }

        _watchers.Clear();
    }

    private void WatcherChanged(object sender, FileSystemEventArgs e)
    {
        _dispatcher.BeginInvoke(Refresh);
    }

    private static bool EnsureTargetFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            return Directory.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private static string GetUniqueDestination(string targetDirectory, string name)
    {
        var destination = Path.Combine(targetDirectory, name);
        if (!File.Exists(destination) && !Directory.Exists(destination))
        {
            return destination;
        }

        var baseName = Path.GetFileNameWithoutExtension(name);
        var extension = Path.GetExtension(name);
        for (var i = 1; i < 10_000; i++)
        {
            var candidate = Path.Combine(targetDirectory, $"{baseName} ({i}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }

        throw new IOException($"Could not find a unique name for {name}.");
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        foreach (var file in Directory.EnumerateFiles(sourceDirectory))
        {
            File.Copy(file, GetUniqueDestination(destinationDirectory, Path.GetFileName(file)));
        }

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory))
        {
            CopyDirectory(directory, Path.Combine(destinationDirectory, Path.GetFileName(directory)));
        }
    }

    private static SolidColorBrush CreateBrush(string color, double opacity)
    {
        try
        {
            var parsed = (Color)ColorConverter.ConvertFromString(color);
            var brush = new SolidColorBrush(parsed) { Opacity = Math.Clamp(opacity, 0.05, 1.0) };
            brush.Freeze();
            return brush;
        }
        catch
        {
            var fallback = new SolidColorBrush(Color.FromRgb(79, 179, 255)) { Opacity = Math.Clamp(opacity, 0.05, 1.0) };
            fallback.Freeze();
            return fallback;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
