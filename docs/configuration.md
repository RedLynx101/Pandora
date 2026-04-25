# Configuration

Workspace path:

```text
%APPDATA%\OrbitDock\workspace.json
```

If `%APPDATA%\OrbitDock\workspace.json` does not exist, OrbitDock imports `%APPDATA%\CustomFences\workspace.json` once and migrates it to schema v2.

Starter workspace shape:

```json
{
  "schemaVersion": 2,
  "settings": {
    "attachWindowsToDesktop": false,
    "startWithWindows": false,
    "peekHotkey": "Ctrl+Alt+Space",
    "theme": "Graphite",
    "defaultDropAction": "copy",
    "enableRuleAutomation": false
  },
  "zones": [],
  "activeLayoutName": "Main",
  "layouts": [
    {
      "name": "Main",
      "hideDesktopIconsWhenRunning": true,
      "dockStates": [],
      "itemOverrides": [],
      "desktopPins": []
    }
  ]
}
```

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

`layouts` contains named profiles. The active profile stores the parts users expect to change often:

- `dockStates`: dock bounds, visible/collapsed/locked state, and active tab.
- `itemOverrides`: virtual dock membership, per-dock hidden state, and saved item order.
- `desktopPins`: OrbitDock-managed icons shown on the desktop overlay while the raw Windows desktop icon grid is hidden.
- `hideDesktopIconsWhenRunning`: clean desktop setting for that layout.

Use settings or `orbitdockctl layout switch <name>` to change profiles.

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
