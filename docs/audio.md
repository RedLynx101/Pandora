# Pandora Audio

Pandora audio is optional and local-only. Sound effects and the music dock are disabled by default.

## Settings

Workspace defaults:

```json
{
  "audio": {
    "enableSoundEffects": false,
    "soundEffectsVolume": 0.35,
    "enableMusicDock": false,
    "musicRootPath": "%USERPROFILE%\\Music\\Pandora",
    "soundEffectsPath": "%APPDATA%\\Pandora\\Audio\\Sfx"
  }
}
```

Use settings or the CLI:

```powershell
.\scripts\pandoractl.ps1 audio sfx on
.\scripts\pandoractl.ps1 audio music on
.\scripts\pandoractl.ps1 audio set-music-folder "$env:USERPROFILE\Music\Pandora"
```

## Music Folder

The music dock scans `%USERPROFILE%\Music\Pandora` by default.

- Files directly in the root appear in `All Tracks`.
- Subfolders appear as playlists.
- Nested subfolders appear by relative path, such as `Focus/Deep`.
- Supported files: `.mp3`, `.wav`, `.wma`, `.m4a`, `.flac`.
- Unsupported files are ignored.

The music dock stores selected playlist, selected track, shuffle, repeat, volume, and collapsed state per layout display variant.

## Silk Current Visualizer

The music dock includes a small visualizer button in its header. It launches the
static Silk Current visualizer from `tools/SilkCurrentVisualizer` through a
localhost-only PowerShell server:

```powershell
.\tools\SilkCurrentVisualizer\start-visualizer.ps1
```

Silk Current analyzes audio in the browser with the Web Audio API. To listen to
system playback, click `Listen` in the page, choose a screen or browser tab in
Edge/Chrome, and enable audio sharing in the browser picker. No audio leaves the
browser tab.

## Sound Effects

Music refresh preserves remembered playlist/track selection without writing during construction. Scanning skips inaccessible or linked subtrees and retains healthy tracks. Narrow bars keep transport actions in **Dock actions**; decorative header branding yields space to the dock name.

Silk Current is included with portable builds. The loopback server serves only its three static assets and accepts GET/HEAD, not project files or launcher scripts. Cancel/Stop and closing the page release acquired capture tracks, including an audio context still starting. Freeze preserves the frame until resumed. Audio remains browser-local; actual system-audio availability depends on the browser and sharing selection.

Sound effects are looked up by name in `%APPDATA%\Pandora\Audio\Sfx`. Missing files are ignored.

Suggested filenames:

- `search-open.wav`
- `search-close.wav`
- `search-typing.wav`
- `dock-bloom.wav`
- `dock-close.wav`
- `item-open.wav`
- `music-play.wav`
- `music-next.wav`
- `music-previous.wav`
- `music-mute.wav`

## Suno Prompt Packages

**Name:** Pandora Drift Loop
**Style of Music:** light space ambient, soft warm pads, subtle pulsing arpeggio, minimal percussion, seamless loop, 70 bpm, no lyrics, unobtrusive desktop focus music.

**Name:** Pandora Deep Focus
**Style of Music:** airy sci-fi ambient, gentle analog synth bed, slow evolving harmonics, faint starfield texture, no drums, seamless loop, no lyrics.

**Name:** Dock Bloom  
**Sound:** one-shot UI expand sound, soft glassy chime, short airy rise, clean futuristic desktop interface, under one second.

**Name:** Dock Close  
**Sound:** one-shot UI collapse sound, gentle downward synth pluck, soft tail, non-intrusive, under one second.

**Name:** Search Tick  
**Sound:** one-shot quiet typing/search tick, tiny rounded digital blip, soft attack, very low volume, under half a second.
