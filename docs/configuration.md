# Configuration

Workspace path:

```text
%APPDATA%\Pandora\workspace.json
```

## Data preservation and recovery

Existing malformed, unreadable, null-shaped, or future-schema workspaces are never automatically replaced with defaults. Startup reports the problem; a failed reload leaves existing docks open. Fix the file or explicitly restore a known backup, then reload. Supported migrations back up the original bytes before replacement.

Writes use an exclusive lock and a content fingerprint attached to the loaded snapshot. A stale writer fails instead of overwriting another agent's or editor's changes—even if the file timestamp did not change. Reload and reapply the intended change after a conflict. Non-cooperating editors can still race a pathname-based check; use the CLI for coordinated writes.

`pandoractl workspace validate` is read-only: it does not create a missing workspace, migrate on disk, create locks, or make backups. A missing/invalid/unsupported file produces a nonzero exit code. Relative explicit workspace paths resolve against the current directory, including their companion feed store.

Folder drops never overwrite existing items and reject symbolic links/junctions or other reparse points in source trees and destination ancestors. Preflight is bounded to 20,000 entries and 128 levels. A failed copy may leave partial destination files, but it retains the source and its pin. These checks are not a race-proof filesystem sandbox.

Automatic startup/reload rewriting of AI virtual-tab shortcuts has been removed. Explicit shortcut installation and the user's startup preference remain separate operations. Repairing oversized docks affects only the active display variant.

Starter workspace shape:

```json
{
  "schemaVersion": 7,
  "settings": {
    "attachWindowsToDesktop": false,
    "startWithWindows": false,
    "peekHotkey": "Ctrl+Alt+Space",
    "theme": "LunarGlass",
    "dockTheme": "Classic",
    "dockBarSize": "Standard",
    "customAccentColor": null,
    "customSurfaceColor": null,
    "glassOpacity": 0.88,
    "reduceMotion": false,
    "iconStyle": "Aperture",
    "defaultDropAction": "copy",
    "enableRuleAutomation": false,
    "audio": {
      "enableSoundEffects": false,
      "soundEffectsVolume": 0.35,
      "enableMusicDock": false,
      "musicRootPath": "%USERPROFILE%\\Music\\Pandora",
      "soundEffectsPath": "%APPDATA%\\Pandora\\Audio\\Sfx"
    }
  },
  "zones": [],
  "activeLayoutName": "Main",
  "layouts": [
    {
      "name": "Main",
      "hideDesktopIconsWhenRunning": true,
      "itemOverrides": [],
      "displayVariants": [
        {
          "key": "default",
          "displaySignature": "default",
          "isDefault": true,
          "dockStates": [],
          "desktopPins": [],
          "music": {}
        }
      ]
    }
  ]
}
```

## Appearance

Appearance has separate structure and color choices:

| Settings choice | JSON field | Values |
| --- | --- | --- |
| Dock theme / structure | `dockTheme` | `Classic` (default), `Halo`, `Meridian` |
| Dock bar size | `dockBarSize` | `Compact`, `Standard` (default), `Large` |
| Palette | `theme` | `LunarGlass` (displayed as Lunar, default), `Midnight`, `Limestone`, `Aegean`, `System` |
| Optional accent | `customAccentColor` | `#RRGGBB` or `null` |
| Optional base surface | `customSurfaceColor` | `#RRGGBB` or `null` |

The C# `AppSettings.Theme` property and JSON `theme` field represent the **palette**. `DockTheme` selects geometry independently. Classic keeps the existing dock structure. Halo separates a floating rounded header and body with pill controls and an airy grid. Meridian uses a crisp frame, accent rail, separate tab strip, and compact horizontal file tiles. Changing the palette does not switch structure.

Classic honors a dock's saved corner radius. Halo and Meridian use their structural profile's radius; the saved per-dock value is retained for switching back to Classic.

**Dock bar size** scales the name, brand icon and header buttons together without resizing file icons or changing the stored expanded dock size. Compact uses 36px bars (44px for Halo); Standard uses the theme's normal 44px/60px height; Large uses 56px/72px. Outer borders add their thickness. Title fonts scale with the bar. All themes keep the name on one line: multi-tab navigation is a separate horizontal strip visible only while expanded. Older Classic docks no longer stack tabs inside the name bar. Tab contents, order and active selection are preserved.

System follows the Windows app theme. The legacy `Graphite` value resolves to Lunar. Aegean pairs deep blue-green surfaces with a sea-glass accent.

Use the color pickers or enter six-digit `#RRGGBB` values. Blank fields clear the optional override. The global accent and surface choices generate related text, muted text, focus, border, selection, and status roles for readability; they are not a collection of unrelated raw brush overrides. Invalid drafts are identified before applying. Intentional per-dock accent/background overrides remain intact when the global theme, palette, or custom colors change.

**Dock surface opacity** (`glassOpacity`, 0.55–1.0) affects the header/name background, body and footer across all structures, including rolled-up docks. Text, icons and controls remain solid; this is not window opacity. Intentional per-dock opacity overrides apply to these surfaces together. Menus and Settings retain opaque backgrounds. `reduceMotion` suppresses optional motion. Windows high contrast overrides the palette and forces opaque surfaces; Windows reduced-animation preferences are respected.

`iconStyle` selects `Aperture`, `Selene`, or `Aster` for product surfaces. The shipped executable uses Aperture; desktop shortcuts can be refreshed with `install-test-shortcut.ps1 -IconStyle Selene`.

Each dock also has **Header icon** in Settings → Docks: `Pandora` (default), `Custom`, or `None`. These are stored in its `appearance.headerIcon` and optional `appearance.headerIconPath`. Choose a local PNG, ICO, or JPEG for Custom; the file is read in place, not copied. Limits are 8 MiB and 2048 pixels per side (4 megapixels); ICO frames are limited to 256 pixels and 64 frames. Missing, invalid, linked, or network files fall back to Pandora with a status message. None removes the icon and its spacing. The global icon choice still controls the app, tray, and docks using Pandora.

Aperture remains the selected default. Schema v6 introduced structure/custom-color settings; v7 adds Standard bar sizing for existing workspaces. Migration preserves existing palette, icon, layout and dock content choices; it does not reset a user's Selene or Aster choice.

## Zone Fields

- `id`: Stable identifier used by rules.
- `name`: Header text.
- `isVisible`: Whether the zone window opens.
- `isLocked`: Prevents dragging from the zone header.
- `isCollapsed`: Starts in roll-up state.
- `bounds`: Desktop position and size.
- `appearance`: Colors, opacity, corner radius, icon size, tab style.
- `tabs`: Folder portals shown in that zone.

## Layout Profiles

`layouts` contains named profiles. Each named profile can have multiple display variants for different monitor combinations:

- `displayVariants`: screen-specific dock bounds, visible/collapsed/locked state, active tab, expansion edge, desktop pin positions, and music dock state.
- `itemOverrides`: virtual dock membership, per-dock hidden state, and saved item order.
- `hideDesktopIconsWhenRunning`: clean desktop setting for that layout.

Use settings or `pandoractl layout switch <name>` to change profiles.

Pandora automatically creates a display variant when it sees a new monitor combination. Unknown monitor combinations clone the profile's default variant and clamp docks into the visible work area.

### Rolled-up geometry

Saved bounds describe the dock's remembered **expanded** geometry. A rolled-up window is a shorter visible projection of those bounds, with `isCollapsed` stored separately. Moving or normalizing that projection must not replace the expanded height or clear the collapsed state. For bottom expansion, the visible header is anchored to the saved bottom edge so reopening grows upward without a jump. This separation addresses closed docks reopening or losing their size when moved.

## Paths

Paths can use environment variables such as `%USERPROFILE%\Downloads`. The app expands paths at runtime and compresses user-profile paths when settings are saved.

## Mouse Organization

Smart desktop docks are virtual. Dragging within a smart dock saves order. Dragging between smart docks changes dock membership in the active layout. Dragging an external file or shortcut into a smart dock creates a virtual entry pointing at the existing real item.

Explicit folder docks keep the filesystem behavior: dropped files are copied by default into the folder portal. Remove-from-dock only hides an item from that dock; deleting the real file is a separate confirmation action.

## Drop Actions

`defaultDropAction` supports:

- `copy`: default and safest.
- `move`: moves dropped files/folders into the active portal folder.
- `shortcut`: reserved for a future shell-link implementation.

Until shortcut support exists, keep the default as `copy`.

## Rule Templates

Rule automation is not active until `enableRuleAutomation` is true and an executor is implemented. Current rules are useful as saved intent and as test data for matching behavior.

## Audio

Audio is opt-in. `enableSoundEffects` turns on local UI sounds from `soundEffectsPath`; `enableMusicDock` shows the local music dock backed by `musicRootPath`.
