# Configuration

Workspace path:

```text
%APPDATA%\CustomFences\workspace.json
```

Starter workspace:

```json
{
  "schemaVersion": 1,
  "settings": {
    "attachWindowsToDesktop": true,
    "startWithWindows": false,
    "peekHotkey": "Ctrl+Alt+Space",
    "theme": "Graphite",
    "defaultDropAction": "copy",
    "enableRuleAutomation": false
  },
  "zones": []
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

## Paths

Paths can use environment variables such as `%USERPROFILE%\Downloads`. The app expands paths at runtime and compresses user-profile paths when settings are saved.

## Drop Actions

`defaultDropAction` supports:

- `copy`: default and safest.
- `move`: moves dropped files/folders into the active portal folder.
- `shortcut`: reserved for a future shell-link implementation.

Until shortcut support exists, keep the default as `copy`.

## Rule Templates

Rule automation is not active until `enableRuleAutomation` is true and an executor is implemented. Current rules are useful as saved intent and as test data for matching behavior.
