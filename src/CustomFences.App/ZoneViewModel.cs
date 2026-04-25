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
    private readonly List<FileItemViewModel> _allItems = [];
    private readonly Random _random = new();
    private ZoneTabDefinition? _selectedTab;
    private string _statusMessage = string.Empty;
    private string _baseStatusMessage = string.Empty;
    private string _searchQuery = string.Empty;
    private MusicPlaylistViewModel? _selectedMusicPlaylist;
    private MusicTrackViewModel? _selectedMusicTrack;
    private string _nowPlayingText = "No track selected";
    private bool _isMusicPlaying;

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
    public ObservableCollection<MusicPlaylistViewModel> MusicPlaylists { get; } = [];
    public ObservableCollection<MusicTrackViewModel> MusicTracks { get; } = [];

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
    public bool IsMusicDock => Zone.Kind == ZoneKind.Music;
    public bool IsSmartDock => SelectedTab?.Source == ZoneTabSource.SmartDesktop;
    public string DockId => Zone.Id;
    public string? SelectedTabId => SelectedTab?.Id;
    public DockExpansionEdge ExpansionEdge => WorkspaceLayoutService.GetExpansionEdge(_manager.Workspace, Zone);
    public double MusicVolume
    {
        get => WorkspaceLayoutService.EnsureActiveDisplayVariant(_manager.Workspace).Music.Volume;
        set
        {
            var state = WorkspaceLayoutService.EnsureActiveDisplayVariant(_manager.Workspace).Music;
            state.Volume = Math.Clamp(value, 0, 1);
            _manager.Audio.SetMusicVolume(state.Volume);
            _manager.Save();
            OnPropertyChanged();
        }
    }
    public bool IsMusicShuffle
    {
        get => WorkspaceLayoutService.EnsureActiveDisplayVariant(_manager.Workspace).Music.Shuffle;
        set
        {
            WorkspaceLayoutService.EnsureActiveDisplayVariant(_manager.Workspace).Music.Shuffle = value;
            _manager.Save();
            OnPropertyChanged();
        }
    }
    public MusicRepeatMode MusicRepeat
    {
        get => WorkspaceLayoutService.EnsureActiveDisplayVariant(_manager.Workspace).Music.Repeat;
        set
        {
            WorkspaceLayoutService.EnsureActiveDisplayVariant(_manager.Workspace).Music.Repeat = value;
            _manager.Save();
            OnPropertyChanged();
        }
    }

    public MusicPlaylistViewModel? SelectedMusicPlaylist
    {
        get => _selectedMusicPlaylist;
        set
        {
            if (_selectedMusicPlaylist == value)
            {
                return;
            }

            _selectedMusicPlaylist = value;
            OnPropertyChanged();
            ApplySelectedPlaylist(save: true);
        }
    }

    public MusicTrackViewModel? SelectedMusicTrack
    {
        get => _selectedMusicTrack;
        set
        {
            if (_selectedMusicTrack == value)
            {
                return;
            }

            _selectedMusicTrack = value;
            OnPropertyChanged();
            SaveSelectedTrack();
        }
    }

    public string NowPlayingText
    {
        get => _nowPlayingText;
        private set
        {
            if (_nowPlayingText == value)
            {
                return;
            }

            _nowPlayingText = value;
            OnPropertyChanged();
        }
    }

    public bool IsMusicPlaying
    {
        get => _isMusicPlaying;
        private set
        {
            if (_isMusicPlaying == value)
            {
                return;
            }

            _isMusicPlaying = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PlayPauseGlyph));
        }
    }

    public string PlayPauseGlyph => IsMusicPlaying ? "\uE769" : "\uE768";

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            var next = value ?? string.Empty;
            if (_searchQuery == next)
            {
                return;
            }

            _searchQuery = next;
            OnPropertyChanged();
            ApplySearchFilter();
        }
    }

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
        _allItems.Clear();

        if (IsMusicDock)
        {
            RefreshMusic();
            return;
        }

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
                _allItems.Add(item);
            }

            _baseStatusMessage = _allItems.Count == 0 ? "Folder is empty." : $"{_allItems.Count} items";
            ApplySearchFilter();
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
                _allItems.Add(item);
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

            _baseStatusMessage = _allItems.Count == 0 ? $"No {label} found on the desktop." : $"{_allItems.Count} {label}";
            ApplySearchFilter();
            StartWatchers(DesktopItemCatalog.GetDesktopDirectories());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = $"Cannot scan desktop: {ex.Message}";
        }
    }

    public void ClearSearch()
    {
        SearchQuery = string.Empty;
    }

    public void PlaySelectedTrack()
    {
        if (SelectedMusicTrack is null)
        {
            SelectedMusicTrack = MusicTracks.FirstOrDefault();
        }

        if (SelectedMusicTrack is null)
        {
            StatusMessage = "No music track selected.";
            return;
        }

        var state = WorkspaceLayoutService.EnsureActiveDisplayVariant(_manager.Workspace).Music;
        state.SelectedTrackPath = SelectedMusicTrack.Path;
        _manager.Save();
        _manager.Audio.PlayMusic(SelectedMusicTrack.Path, state.Volume);
        IsMusicPlaying = true;
        NowPlayingText = SelectedMusicTrack.Title;
        StatusMessage = $"Playing {SelectedMusicTrack.Title}";
    }

    public void ToggleMusicPlayback()
    {
        if (IsMusicPlaying)
        {
            _manager.Audio.PauseMusic();
            IsMusicPlaying = false;
            StatusMessage = "Music paused.";
            return;
        }

        if (_manager.Audio.IsMusicPaused)
        {
            _manager.Audio.ResumeMusic();
            IsMusicPlaying = true;
            StatusMessage = $"Playing {SelectedMusicTrack?.Title ?? "music"}";
            return;
        }

        PlaySelectedTrack();
    }

    public void PlayNextTrack()
    {
        if (MusicTracks.Count == 0)
        {
            return;
        }

        var currentIndex = SelectedMusicTrack is null ? -1 : MusicTracks.IndexOf(SelectedMusicTrack);
        var nextIndex = IsMusicShuffle
            ? _random.Next(MusicTracks.Count)
            : (currentIndex + 1) % MusicTracks.Count;
        SelectedMusicTrack = MusicTracks[nextIndex];
        PlaySelectedTrack();
    }

    public void PlayPreviousTrack()
    {
        if (MusicTracks.Count == 0)
        {
            return;
        }

        var currentIndex = SelectedMusicTrack is null ? 0 : MusicTracks.IndexOf(SelectedMusicTrack);
        var previousIndex = currentIndex <= 0 ? MusicTracks.Count - 1 : currentIndex - 1;
        SelectedMusicTrack = MusicTracks[previousIndex];
        PlaySelectedTrack();
    }

    public void HandleMusicEnded()
    {
        if (!IsMusicDock)
        {
            return;
        }

        if (MusicRepeat == MusicRepeatMode.One)
        {
            PlaySelectedTrack();
            return;
        }

        var selectedIndex = SelectedMusicTrack is null ? -1 : MusicTracks.IndexOf(SelectedMusicTrack);
        if (MusicRepeat == MusicRepeatMode.All || selectedIndex < MusicTracks.Count - 1)
        {
            PlayNextTrack();
            return;
        }

        IsMusicPlaying = false;
        StatusMessage = "Music finished.";
    }

    private void RefreshMusic()
    {
        MusicPlaylists.Clear();
        MusicTracks.Clear();
        var library = MusicLibraryScanner.Scan(_manager.Workspace.Settings.Audio.MusicRootPath);
        foreach (var playlist in library.Playlists)
        {
            MusicPlaylists.Add(new MusicPlaylistViewModel(playlist));
        }

        var state = WorkspaceLayoutService.EnsureActiveDisplayVariant(_manager.Workspace).Music;
        _selectedMusicPlaylist = MusicPlaylists.FirstOrDefault(playlist =>
            string.Equals(playlist.Id, state.SelectedPlaylist, StringComparison.OrdinalIgnoreCase))
            ?? MusicPlaylists.FirstOrDefault();
        OnPropertyChanged(nameof(SelectedMusicPlaylist));
        ApplySelectedPlaylist(save: false);

        if (!string.IsNullOrWhiteSpace(state.SelectedTrackPath))
        {
            _selectedMusicTrack = MusicTracks.FirstOrDefault(track =>
                WorkspaceLayoutService.PathsEqual(track.Path, state.SelectedTrackPath));
            OnPropertyChanged(nameof(SelectedMusicTrack));
        }

        if (_selectedMusicTrack is null)
        {
            _selectedMusicTrack = MusicTracks.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedMusicTrack));
        }

        NowPlayingText = SelectedMusicTrack?.Title ?? "No track selected";
        _baseStatusMessage = library.StatusMessage;
        StatusMessage = library.StatusMessage;
    }

    private void ApplySelectedPlaylist(bool save)
    {
        MusicTracks.Clear();
        if (SelectedMusicPlaylist is not null)
        {
            foreach (var track in SelectedMusicPlaylist.Tracks)
            {
                MusicTracks.Add(track);
            }
        }

        SelectedMusicTrack = MusicTracks.FirstOrDefault();
        if (save && SelectedMusicPlaylist is not null)
        {
            var state = WorkspaceLayoutService.EnsureActiveDisplayVariant(_manager.Workspace).Music;
            state.SelectedPlaylist = SelectedMusicPlaylist.Id;
            state.SelectedTrackPath = SelectedMusicTrack?.Path;
            _manager.Save();
        }
    }

    private void SaveSelectedTrack()
    {
        var state = WorkspaceLayoutService.EnsureActiveDisplayVariant(_manager.Workspace).Music;
        state.SelectedTrackPath = SelectedMusicTrack?.Path;
        _manager.Save();
        NowPlayingText = SelectedMusicTrack?.Title ?? "No track selected";
    }

    private void ApplySearchFilter()
    {
        Items.Clear();
        var query = SearchQuery.Trim();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? _allItems
            : _allItems.Where(item =>
                DockSearchMatcher.Matches(item.DisplayName, item.Extension, item.Path, query)).ToList();

        foreach (var item in filtered)
        {
            Items.Add(item);
        }

        StatusMessage = string.IsNullOrWhiteSpace(query)
            ? _baseStatusMessage
            : $"{Items.Count} of {_allItems.Count} items";
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
