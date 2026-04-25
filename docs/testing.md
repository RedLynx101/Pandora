# Desktop Testing

Use these scripts from the repository root.

## Publish a Test Build

```powershell
.\scripts\publish-portable.ps1
```

The app is published to:

```text
artifacts\CustomFences-win-x64\CustomFences.App.exe
```

The CLI is published into the same folder as `OrbitDock.Cli.exe`.

## Launch

```powershell
.\scripts\start-customfences.ps1 -FromPublish
```

Open settings on launch:

```powershell
.\scripts\start-customfences.ps1 -FromPublish -Settings
```

Restart a running copy:

```powershell
.\scripts\start-customfences.ps1 -FromPublish -Restart
```

## Stop

```powershell
.\scripts\stop-customfences.ps1
```

## Reset Test Workspace

This stops the app, backs up `%APPDATA%\OrbitDock\workspace.json`, and removes it. The app creates a fresh default workspace on next launch.

```powershell
.\scripts\reset-workspace.ps1
```

## Desktop Shortcut

```powershell
.\scripts\install-test-shortcut.ps1
```

Optional settings shortcut:

```powershell
.\scripts\install-test-shortcut.ps1 -SettingsShortcut
```

## What to Test First

- Drag the zone headers around the desktop.
- Resize the zones from the window edge.
- Double-click a zone header to roll it up and down.
- Drag a small test file into a zone and confirm it copies into the underlying folder.
- Drag an item within a smart dock and confirm the order persists after restart.
- Drag an item between smart docks and confirm it moves virtually while the real shortcut remains in place.
- Click the search icon in a dock header, type a partial app name, and confirm results filter and `Esc` clears the search.
- Set a dock to bottom expansion with `.\scripts\orbitdockctl.ps1 dock set-expansion build bottom`, reload, and confirm it expands upward.
- Right-click an item, remove it from the dock, and confirm the real file still exists.
- Pin an item to the desktop overlay, move the pin, restart, and confirm its position persists.
- Run `.\scripts\orbitdockctl.ps1 layout list`, `layout variants`, and `workspace validate` while the app is running.
- Enable the music dock with `.\scripts\orbitdockctl.ps1 audio music on`, add a test `.mp3` or `.wav` under `%USERPROFILE%\Music\OrbitDock`, and confirm playlist scanning.
- Use the tray icon to open settings, reload, set docks behind windows, and exit.
- Press `Ctrl+Alt+Space` to send docks back behind active windows.
- Run `.\scripts\show-desktop-icons.ps1` if you ever need to restore the raw Windows desktop icon grid after a crash.
