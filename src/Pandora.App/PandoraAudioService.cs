using System;
using System.IO;
using System.Linq;
using System.Windows.Media;
using Pandora.Core;

namespace Pandora.App;

public sealed class PandoraAudioService : IDisposable
{
    private static readonly string[] SoundExtensions = [".wav", ".mp3", ".wma", ".m4a"];

    private readonly MediaPlayer _soundPlayer = new();
    private readonly MediaPlayer _musicPlayer = new();
    private DateTime _lastTypingSoundUtc = DateTime.MinValue;
    private bool _isMusicPaused;

    public event EventHandler? MusicEnded;

    public PandoraAudioService()
    {
        _musicPlayer.MediaEnded += (_, _) => MusicEnded?.Invoke(this, EventArgs.Empty);
        _musicPlayer.MediaFailed += (_, _) => MusicEnded?.Invoke(this, EventArgs.Empty);
    }

    public bool IsMusicPaused => _isMusicPaused;

    public void PlaySoundEffect(Workspace workspace, string name)
    {
        var settings = workspace.Settings.Audio;
        if (!settings.EnableSoundEffects)
        {
            return;
        }

        if (string.Equals(name, "search-typing", StringComparison.OrdinalIgnoreCase) &&
            DateTime.UtcNow - _lastTypingSoundUtc < TimeSpan.FromMilliseconds(90))
        {
            return;
        }

        if (string.Equals(name, "search-typing", StringComparison.OrdinalIgnoreCase))
        {
            _lastTypingSoundUtc = DateTime.UtcNow;
        }

        var path = ResolveSoundPath(settings.SoundEffectsPath, name);
        if (path is null)
        {
            return;
        }

        try
        {
            _soundPlayer.Stop();
            _soundPlayer.Open(new Uri(path, UriKind.Absolute));
            _soundPlayer.Volume = Math.Clamp(settings.SoundEffectsVolume, 0, 1);
            _soundPlayer.Play();
        }
        catch
        {
            // Sound effects are optional polish and should never disrupt dock input.
        }
    }

    public void PlayMusic(string path, double volume)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return;
        }

        try
        {
            _musicPlayer.Open(new Uri(path, UriKind.Absolute));
            _musicPlayer.Volume = Math.Clamp(volume, 0, 1);
            _musicPlayer.Play();
            _isMusicPaused = false;
        }
        catch
        {
            MusicEnded?.Invoke(this, EventArgs.Empty);
        }
    }

    public void PauseMusic()
    {
        try
        {
            _musicPlayer.Pause();
            _isMusicPaused = true;
        }
        catch
        {
            // Optional playback should fail quietly.
        }
    }

    public void ResumeMusic()
    {
        try
        {
            _musicPlayer.Play();
            _isMusicPaused = false;
        }
        catch
        {
            // Optional playback should fail quietly.
        }
    }

    public void StopMusic()
    {
        try
        {
            _musicPlayer.Stop();
            _isMusicPaused = false;
        }
        catch
        {
            // Optional playback should fail quietly.
        }
    }

    public void SetMusicVolume(double volume)
    {
        _musicPlayer.Volume = Math.Clamp(volume, 0, 1);
    }

    public void Dispose()
    {
        _soundPlayer.Close();
        _musicPlayer.Close();
    }

    private static string? ResolveSoundPath(string folder, string name)
    {
        var expanded = PathExpander.Expand(folder);
        if (!Directory.Exists(expanded))
        {
            return null;
        }

        return SoundExtensions
            .Select(extension => Path.Combine(expanded, name + extension))
            .FirstOrDefault(File.Exists);
    }
}
