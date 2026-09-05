using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Pandora.Core;

namespace Pandora.App;

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
    private AgentFeedCardViewModel? _selectedAgentFeed;
    private string _nowPlayingText = "No track selected";
    private bool _isMusicPlaying;
    private bool _updatingMusicSelection;
    private bool _refreshingAgentFeeds;
    private bool _disposed;
    private int _refreshQueued;
    private readonly DispatcherTimer _refreshTimer;
    private DockIconResult _headerIcon = new(null, true, false, string.Empty);

    public ZoneViewModel(ZoneDefinition zone, DesktopZoneManager manager)
    {
        _manager = manager;
        _refreshTimer = new DispatcherTimer(DispatcherPriority.Background, _dispatcher) { Interval = TimeSpan.FromMilliseconds(150) };
        _refreshTimer.Tick += RefreshTimer_Tick;
        Zone = zone;
        var activeTabId = WorkspaceLayoutService.GetActiveTabId(manager.Workspace, zone);
        SelectedTab = zone.Tabs.FirstOrDefault(tab => string.Equals(tab.Id, activeTabId, StringComparison.OrdinalIgnoreCase))
            ?? zone.Tabs.FirstOrDefault();
        Refresh();
        ThemeService.ThemeChanged += OnThemeChanged;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public ZoneDefinition Zone { get; }
    public ObservableCollection<FileItemViewModel> Items { get; } = [];
    public ObservableCollection<MusicPlaylistViewModel> MusicPlaylists { get; } = [];
    public ObservableCollection<MusicTrackViewModel> MusicTracks { get; } = [];
    public ObservableCollection<AgentFeedCardViewModel> AgentFeeds { get; } = [];

    public IReadOnlyList<ZoneTabDefinition> Tabs => Zone.Tabs;
    public string Name => Zone.Name;
    public double IconSize => Zone.Appearance.IconSize;
    public string ThemeId => ThemeService.EffectiveDockTheme;
    public DockThemeProfile ThemeProfile => DockThemeCatalog.Get(ThemeId);
    public double ItemWidth => ThemeId == "Meridian" ? 150 : Math.Max(78, Zone.Appearance.IconSize + (ThemeId == "Halo" ? 48 : 42));
    public DockBarMetrics BarMetrics => DockBarSizing.Get(ThemeProfile, ThemeService.EffectiveDockBarSize);
    public double HeaderHeight => BarMetrics.Height;
    public Thickness ItemMargin => new(ThemeProfile.ItemGap);
    public Brush ItemSurfaceBrush => ThemeId == "Halo" && ThemeService.IsFactoryBackground(Zone.Appearance.BackgroundColor)
        ? ThemeResource("Pandora.SurfaceBrush") : Brushes.Transparent;
    public Brush ItemBorderBrush => ThemeId == "Meridian" ? ThemeResource("Pandora.BorderBrush") : Brushes.Transparent;
    public System.Windows.Media.Brush AccentBrush => ThemeService.GetDockAccent(Zone.Appearance);
    public System.Windows.Media.Brush HeaderBrush => ThemeService.GetDockChrome(Zone.Appearance);
    public System.Windows.Media.Brush BackgroundBrush => ThemeService.GetDockBackground(Zone.Appearance);
    public System.Windows.Media.Brush BorderBrush => ThemeResource("Pandora.BorderBrush");
    public ImageSource? BrandImage => _headerIcon.Image;
    public bool HasHeaderIcon => _headerIcon.IsVisible;
    public string HeaderIconStatus => _headerIcon.StatusMessage;
    public Brush ItemTextBrush => ThemeService.GetDockText(Zone.Appearance);
    public CornerRadius CornerRadius => new(ThemeId == "Classic" ? Zone.Appearance.CornerRadius : ThemeProfile.CornerRadius);

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
    public bool IsAgentFeedDock => Zone.Kind == ZoneKind.AgentFeed;
    public bool IsProjectsDock => Zone.Kind == ZoneKind.Projects;
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
            var volume = double.IsFinite(value) ? Math.Clamp(value, 0, 1) : state.Volume;
            if (state.Volume == volume) return;
            state.Volume = volume;
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
            if (IsMusicShuffle == value) return;
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
            if (MusicRepeat == value) return;
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
            if (!_updatingMusicSelection) ApplySelectedPlaylist(save: true);
        }
    }

    public AgentFeedCardViewModel? SelectedAgentFeed
    {
        get => _selectedAgentFeed;
        set
        {
            if (_selectedAgentFeed == value)
            {
                return;
            }

            _selectedAgentFeed = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(AgentFeedTitle));
            OnPropertyChanged(nameof(AgentFeedSummary));
            OnPropertyChanged(nameof(AgentFeedSourceLine));
            OnPropertyChanged(nameof(AgentFeedStatusText));
            OnPropertyChanged(nameof(AgentFeedBadgeText));
        }
    }

    public string AgentFeedTitle => SelectedAgentFeed?.Title ?? "Agent Feed";
    public string AgentFeedSummary => SelectedAgentFeed?.Summary ?? "No agent update is available yet.";
    public string AgentFeedSourceLine => SelectedAgentFeed?.SourceLine ?? "Waiting for a local agent update.";
    public string AgentFeedStatusText => SelectedAgentFeed?.StatusText ?? "Quiet";
    public string AgentFeedBadgeText
    {
        get
        {
            var unread = AgentFeeds.Count(feed => feed.IsUnread);
            var open = AgentFeeds.Sum(feed => feed.OpenAttentionCount);
            if (unread > 0 && open > 0)
            {
                return $"new {open}";
            }

            if (unread > 0)
            {
                return "new";
            }

            return open > 0 ? open.ToString() : string.Empty;
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
            if (!_updatingMusicSelection) SaveSelectedTrack();
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
        get => string.IsNullOrEmpty(HeaderIconStatus) ? _statusMessage : _statusMessage + " · " + HeaderIconStatus;
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
        if (_disposed) return;
        RefreshHeaderIcon();
        _refreshTimer.Stop();
        StopWatchers();
        Items.Clear();
        _allItems.Clear();

        if (IsProjectsDock)
        {
            StatusMessage = "Metis snapshots · read-only";
            return;
        }

        if (IsMusicDock)
        {
            RefreshMusic();
            return;
        }

        if (IsAgentFeedDock)
        {
            RefreshAgentFeeds();
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

    public int AddDroppedFiles(IReadOnlyCollection<string> paths, DropAction requestedAction)
    {
        if (paths.Count == 0 || string.IsNullOrWhiteSpace(SelectedFolderPath))
        {
            return 0;
        }

        if (IsSmartDock)
        {
            AddVirtualItems(paths);
            return paths.Count;
        }

        var completed = 0;
        string? error = null;
        foreach (var source in paths)
        {
            try
            {
                SafeFileTransfer.Transfer(source, SelectedFolderPath, requestedAction);
                completed++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                error = ex.Message;
            }
        }

        if (completed > 0)
        {
            Refresh();
            StatusMessage = requestedAction == DropAction.Move
                ? $"Moved {completed} item(s)."
                : $"Copied {completed} item(s).";
        }
        if (error is not null) StatusMessage = $"{completed} item(s) transferred. Drop skipped: {error} Partial destination files may remain.";
        return completed;
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
            return AddDroppedFiles([path], _manager.Workspace.Settings.DefaultDropAction) == 1;
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
        if (_disposed) return;
        _disposed = true;
        _refreshTimer.Stop();
        _refreshTimer.Tick -= RefreshTimer_Tick;
        ThemeService.ThemeChanged -= OnThemeChanged;
        StopWatchers();
    }

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        RefreshHeaderIcon();
        OnPropertyChanged(nameof(AccentBrush));
        OnPropertyChanged(nameof(HeaderBrush));
        OnPropertyChanged(nameof(BackgroundBrush));
        OnPropertyChanged(nameof(BorderBrush));
        OnPropertyChanged(nameof(ItemTextBrush));
        OnPropertyChanged(nameof(ThemeId));
        OnPropertyChanged(nameof(ThemeProfile));
        OnPropertyChanged(nameof(BarMetrics));
        OnPropertyChanged(nameof(HeaderHeight));
        OnPropertyChanged(nameof(CornerRadius));
        OnPropertyChanged(nameof(ItemWidth));
        OnPropertyChanged(nameof(ItemMargin));
        OnPropertyChanged(nameof(ItemSurfaceBrush));
        OnPropertyChanged(nameof(ItemBorderBrush));
    }

    public void RefreshHeaderIcon()
    {
        if (_disposed) return;
        _headerIcon = DockIconService.Resolve(Zone.Appearance, _manager.Workspace.Settings.IconStyle);
        OnPropertyChanged(nameof(BrandImage));
        OnPropertyChanged(nameof(HasHeaderIcon));
        OnPropertyChanged(nameof(HeaderIconStatus));
        OnPropertyChanged(nameof(StatusMessage));
    }

    private static Brush ThemeResource(string key) =>
        System.Windows.Application.Current?.TryFindResource(key) as Brush ?? Brushes.Transparent;

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

    public void MarkSelectedAgentFeedRead()
    {
        if (!IsAgentFeedDock ||
            _disposed || _refreshingAgentFeeds || Zone.IsCollapsed ||
            !Zone.AgentFeed.MarkReadOnExpand ||
            SelectedAgentFeed is null ||
            SelectedAgentFeed.IsFallback ||
            !SelectedAgentFeed.IsUnread)
        {
            return;
        }

        try
        {
            _manager.AgentFeeds.MarkRead(SelectedAgentFeed.FeedId, AgentFeedStore.GetRevisionKey(SelectedAgentFeed.Document));
            RefreshAgentFeeds();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            RefreshAgentFeeds();
            StatusMessage = $"Could not mark feed read: {ex.Message}";
        }
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
        var library = MusicLibraryScanner.Scan(_manager.Workspace.Settings.Audio.MusicRootPath);
        var state = WorkspaceLayoutService.EnsureActiveDisplayVariant(_manager.Workspace).Music;
        _updatingMusicSelection = true;
        try
        {
            MusicPlaylists.Clear();
            foreach (var playlist in library.Playlists) MusicPlaylists.Add(new MusicPlaylistViewModel(playlist));
            _selectedMusicPlaylist = MusicPlaylists.FirstOrDefault(playlist =>
                string.Equals(playlist.Id, state.SelectedPlaylist, StringComparison.OrdinalIgnoreCase))
                ?? MusicPlaylists.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedMusicPlaylist));
            ApplySelectedPlaylist(save: false);
        }
        finally { _updatingMusicSelection = false; }
        NowPlayingText = SelectedMusicTrack?.Title ?? "No track selected";
        _baseStatusMessage = library.StatusMessage;
        StatusMessage = library.StatusMessage;
    }

    private void RefreshAgentFeeds()
    {
        if (_disposed || _refreshingAgentFeeds) return;
        _refreshingAgentFeeds = true;
        StopWatchers();
        try
        {
            var selectedId = SelectedAgentFeed?.FeedId;
            var feedIds = Zone.AgentFeed.FeedIds.Count == 0
                ? new[] { "morning-brief" }
                : Zone.AgentFeed.FeedIds.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            AgentFeedStateDocument state;
            string? stateError = null;
            try { state = _manager.AgentFeeds.LoadStateStrict(); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
            { state = new AgentFeedStateDocument(); stateError = ex.Message; }
            var cards = feedIds.Select(id => stateError is null ? LoadAgentFeedCard(id, state) :
                CreateErrorAgentFeedCard(id, state, "Local feed state could not be read: " + stateError)).ToArray();
            AgentFeeds.Clear();
            foreach (var card in cards) AgentFeeds.Add(card);
            SelectedAgentFeed = AgentFeeds.FirstOrDefault(feed =>
                string.Equals(feed.FeedId, selectedId, StringComparison.OrdinalIgnoreCase)) ?? AgentFeeds.FirstOrDefault();
            _baseStatusMessage = AgentFeeds.Count == 0 ? "No agent feeds configured." : $"{AgentFeeds.Count} agent feed(s)";
            StatusMessage = stateError is not null ? "Local feed state unavailable: " + stateError :
                AgentFeedBadgeText.Length == 0 ? _baseStatusMessage : $"{_baseStatusMessage} - {AgentFeedBadgeText}";
            OnPropertyChanged(nameof(AgentFeedBadgeText));
        }
        finally
        {
            _refreshingAgentFeeds = false;
            if (!_disposed) StartAgentFeedWatcher();
        }
    }

    private AgentFeedCardViewModel LoadAgentFeedCard(string feedId, AgentFeedStateDocument state)
    {
        try
        {
            var document = _manager.AgentFeeds.LoadFeed(feedId);
            if (document is null)
            {
                return CreateAgentFeedCard(CreateMissingAgentFeed(feedId), state, isFallback: true);
            }

            return CreateAgentFeedCard(document, state, isFallback: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            return CreateErrorAgentFeedCard(feedId, state, ex.Message);
        }
    }

    private AgentFeedCardViewModel CreateErrorAgentFeedCard(string feedId, AgentFeedStateDocument state, string error)
    {
        return CreateAgentFeedCard(new AgentFeedDocument
            {
                FeedId = feedId,
                Title = feedId,
                SourceAgent = "Pandora",
                Status = AgentFeedStatus.Error,
                UpdatedUtc = DateTime.UtcNow,
                Revision = "error",
                Summary = $"Feed could not be read: {error}",
                Sections =
                [
                    new AgentFeedSection
                    {
                        Id = "error",
                        Title = "Feed Error",
                        Kind = AgentFeedSectionKind.Summary,
                        Text = error
                    }
                ]
            }, state, isFallback: true);
    }

    private AgentFeedCardViewModel CreateAgentFeedCard(AgentFeedDocument document, AgentFeedStateDocument state, bool isFallback)
    {
        var unread = !isFallback && _manager.AgentFeeds.IsUnread(document, state);
        var count = _manager.AgentFeeds.CountOpenAttentionItems(document, state);
        var revision = AgentFeedStore.GetRevisionKey(document);
        return new AgentFeedCardViewModel(document, unread, count, isFallback, state, _manager.AgentFeeds,
            (feedId, itemId, nextState) => SetAgentFeedItemState(feedId, itemId, nextState, revision));
    }

    private static AgentFeedDocument CreateMissingAgentFeed(string feedId)
    {
        return new AgentFeedDocument
        {
            FeedId = feedId,
            Title = feedId == "morning-brief" ? "Morning Brief" : feedId,
            SourceAgent = "Pandora",
            Status = AgentFeedStatus.Quiet,
            UpdatedUtc = DateTime.UtcNow,
            Revision = "missing",
            Summary = "No update has been published yet. Local agents can write this feed with pandoractl agent-feed publish or write.",
            Sections =
            [
                new AgentFeedSection
                {
                    Id = "waiting",
                    Title = "Waiting for Agent",
                    Kind = AgentFeedSectionKind.Summary,
                    Text = "This dock is ready for local agent updates."
                }
            ]
        };
    }

    private void SetAgentFeedItemState(string feedId, string itemId, AgentFeedItemState state, string revision)
    {
        if (_disposed || _refreshingAgentFeeds) return;
        try
        {
            _manager.AgentFeeds.SetItemState(feedId, itemId, state, revision);
            RefreshAgentFeeds();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or JsonException)
        {
            // Recreate the checklist from saved state so a failed write never leaves a false tick.
            RefreshAgentFeeds();
            StatusMessage = $"Could not save checklist state: {ex.Message}";
        }
    }

    private void ApplySelectedPlaylist(bool save)
    {
        var state = WorkspaceLayoutService.EnsureActiveDisplayVariant(_manager.Workspace).Music;
        var rememberedTrack = save ? null : state.SelectedTrackPath;
        var wasUpdating = _updatingMusicSelection;
        _updatingMusicSelection = true;
        try
        {
            MusicTracks.Clear();
            if (SelectedMusicPlaylist is not null)
                foreach (var track in SelectedMusicPlaylist.Tracks) MusicTracks.Add(track);
            _selectedMusicTrack = MusicTracks.FirstOrDefault(track =>
                rememberedTrack is not null && WorkspaceLayoutService.PathsEqual(track.Path, rememberedTrack)) ?? MusicTracks.FirstOrDefault();
            OnPropertyChanged(nameof(SelectedMusicTrack));
            NowPlayingText = SelectedMusicTrack?.Title ?? "No track selected";
        }
        finally { _updatingMusicSelection = wasUpdating; }
        if (save && SelectedMusicPlaylist is not null)
        {
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
        if (_disposed) return;
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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                StatusMessage = $"Live refresh unavailable: {ex.Message}";
            }
        }
    }

    private void StartAgentFeedWatcher()
    {
        if (_disposed) return;
        try
        {
            Directory.CreateDirectory(_manager.AgentFeeds.FeedsDirectory);
            var watcher = new FileSystemWatcher(_manager.AgentFeeds.FeedsDirectory, "*.json")
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            watcher.Created += AgentFeedChanged;
            watcher.Deleted += AgentFeedChanged;
            watcher.Renamed += AgentFeedChanged;
            watcher.Changed += AgentFeedChanged;
            _watchers.Add(watcher);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            StatusMessage = $"Agent feed live refresh unavailable: {ex.Message}";
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
            watcher.Created -= AgentFeedChanged;
            watcher.Deleted -= AgentFeedChanged;
            watcher.Renamed -= AgentFeedChanged;
            watcher.Changed -= AgentFeedChanged;
            watcher.Dispose();
        }

        _watchers.Clear();
    }

    private void WatcherChanged(object sender, FileSystemEventArgs e)
    {
        QueueRefresh();
    }

    private void AgentFeedChanged(object sender, FileSystemEventArgs e)
    {
        if (e.Name is not null && e.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (Volatile.Read(ref _disposed) || _dispatcher.HasShutdownStarted || Interlocked.Exchange(ref _refreshQueued, 1) != 0) return;
        try
        {
            _dispatcher.BeginInvoke(() =>
            {
                Interlocked.Exchange(ref _refreshQueued, 0);
                if (_disposed) return;
                _refreshTimer.Stop();
                _refreshTimer.Start();
            });
        }
        catch (InvalidOperationException) { Interlocked.Exchange(ref _refreshQueued, 0); }
    }

    private void RefreshTimer_Tick(object? sender, EventArgs e)
    {
        _refreshTimer.Stop();
        if (!_disposed) Refresh();
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
