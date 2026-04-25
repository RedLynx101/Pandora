# OrbitDock Audio

OrbitDock audio is optional and local-only. Sound effects and the music dock are disabled by default.

## Settings

Workspace defaults:

```json
{
  "audio": {
    "enableSoundEffects": false,
    "soundEffectsVolume": 0.35,
    "enableMusicDock": false,
    "musicRootPath": "%USERPROFILE%\\Music\\OrbitDock",
    "soundEffectsPath": "%APPDATA%\\OrbitDock\\Audio\\Sfx"
  }
}
```

Use settings or the CLI:

```powershell
.\scripts\orbitdockctl.ps1 audio sfx on
.\scripts\orbitdockctl.ps1 audio music on
.\scripts\orbitdockctl.ps1 audio set-music-folder "$env:USERPROFILE\Music\OrbitDock"
```

## Music Folder

The music dock scans `%USERPROFILE%\Music\OrbitDock` by default.

- Files directly in the root appear in `All Tracks`.
- Subfolders appear as playlists.
- Nested subfolders appear by relative path, such as `Focus/Deep`.
- Supported files: `.mp3`, `.wav`, `.wma`, `.m4a`, `.flac`.
- Unsupported files are ignored.

The music dock stores selected playlist, selected track, shuffle, repeat, volume, and collapsed state per layout display variant.

## Sound Effects

Sound effects are looked up by name in `%APPDATA%\OrbitDock\Audio\Sfx`. Missing files are ignored.

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

**Name:** OrbitDock Drift Loop  
**Style of Music:** light space ambient, soft warm pads, subtle pulsing arpeggio, minimal percussion, seamless loop, 70 bpm, no lyrics, unobtrusive desktop focus music.

**Name:** OrbitDock Deep Focus  
**Style of Music:** airy sci-fi ambient, gentle analog synth bed, slow evolving harmonics, faint starfield texture, no drums, seamless loop, no lyrics.

**Name:** Dock Bloom  
**Sound:** one-shot UI expand sound, soft glassy chime, short airy rise, clean futuristic desktop interface, under one second.

**Name:** Dock Close  
**Sound:** one-shot UI collapse sound, gentle downward synth pluck, soft tail, non-intrusive, under one second.

**Name:** Search Tick  
**Sound:** one-shot quiet typing/search tick, tiny rounded digital blip, soft attack, very low volume, under half a second.
