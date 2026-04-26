using System.Collections.Generic;
using System.Linq;
using CustomFences.Core;

namespace CustomFences.App;

public sealed class MusicPlaylistViewModel
{
    public MusicPlaylistViewModel(MusicPlaylist playlist)
    {
        Id = playlist.Id;
        Name = playlist.Name;
        Tracks = playlist.Tracks.Select(track => new MusicTrackViewModel(track)).ToList();
    }

    public string Id { get; }
    public string Name { get; }
    public IReadOnlyList<MusicTrackViewModel> Tracks { get; }

    public override string ToString()
    {
        return Name;
    }
}

public sealed class MusicTrackViewModel
{
    public MusicTrackViewModel(MusicTrack track)
    {
        Path = track.Path;
        Title = track.Title;
        PlaylistId = track.PlaylistId;
    }

    public string Path { get; }
    public string Title { get; }
    public string PlaylistId { get; }
}
