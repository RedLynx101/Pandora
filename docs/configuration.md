# Configuration

Workspace path:

```text
%APPDATA%\OrbitDock\workspace.json
```

If `%APPDATA%\OrbitDock\workspace.json` does not exist, Pandora imports `%APPDATA%\CustomFences\workspace.json` once and migrates it to the current schema.

Starter workspace shape:

```json
{
  "schemaVersion": 5,
  "settings": {
    "attachWindowsToDesktop": false,
    "startWithWindows": false,
    "peekHotkey": "Ctrl+Alt+Space",
    "theme": "LunarGlass",
    "glassOpacity": 0.88,
    "reduceMotion": false,
    "iconStyle": "Aperture",
    "defaultDropAction": "copy",
    "enableRuleAutomation": false,
    "audio": {
      "enableSoundEffects": false,
      "soundEffectsVolume": 0.35,
      "enableMusicDock": false,
      "musicRootPath": "%USERPROFILE%\\Music\\OrbitDock",
      "soundEffectsPath": "%APPDATA%\\OrbitDock\\Audio\\Sfx"
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

Settings offer `LunarGlass` (default), `Midnight`, `Limestone`, and `System`. System follows the Windows app theme. The legacy `Graphite` value resolves to Lunar Glass. Saved custom dock colors remain custom; selecting a theme does not erase them.

`glassOpacity` is a 0.55–1.0 surface-opacity preference, not window/text opacity. `reduceMotion` suppresses optional motion. Windows high contrast overrides the palette and forces opaque surfaces; Windows reduced-animation preferences are respected.

`iconStyle` selects `Aperture`, `Selene`, or `Aster` for product surfaces. The shipped executable uses Aperture; desktop shortcuts can be refreshed with `install-test-shortcut.ps1 -IconStyle Selene`.

## Compatibility

Pandora deliberately retains `%APPDATA%\OrbitDock`, the existing music root, managed VirtualTabs, and feed IDs. This is not an incomplete migration: it keeps existing tools and user data pointing to a single workspace. New script integrations should use `pandoractl.ps1`; `orbitdockctl.ps1` remains a forwarder.

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
