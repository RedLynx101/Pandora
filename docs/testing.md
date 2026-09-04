# Desktop Testing

Use these scripts from the repository root.

This is a test checklist, not a claim that every scenario has passed on every Windows configuration. Record the exact commit, display setup, commands, and untested cases in a release review.

```powershell
dotnet build Pandora.sln --configuration Release
dotnet run --project tests\OrbitDock.Tests --configuration Release --no-build
```

## Publish a Test Build

```powershell
.\scripts\publish-portable.ps1
```

The app is published to:

```text
artifacts\Pandora-win-x64\Pandora.App.exe
```

The CLI is published into the same folder as `Pandora.Cli.exe`.

## Launch

```powershell
.\scripts\start-pandora.ps1 -FromPublish
```

Open settings on launch:

```powershell
.\scripts\start-pandora.ps1 -FromPublish -Settings
```

Restart a running copy:

```powershell
.\scripts\start-pandora.ps1 -FromPublish -Restart
```

## Stop

```powershell
.\scripts\stop-pandora.ps1
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

Preview shortcut changes without writing:

```powershell
.\scripts\install-test-shortcut.ps1 -SettingsShortcut -StartupShortcut -WhatIf
```

`-StartupShortcut` repairs recognized existing registration while preserving Windows approval state, including disabled state. It does not opt an unregistered app into startup. Backups are under `artifacts\shortcut-backups`. Unknown shortcut targets are left alone.

## Pandora review matrix

- Inspect Lunar Glass, Midnight, and Limestone on light and dark wallpapers; confirm labels, menus, focus indicators, and disabled controls are legible.
- Select System and change the Windows app theme; test Windows high contrast separately.
- Change transparency and reduced motion, save, restart, and verify the preference persists without changing intentional dock colors.
- Open each settings section using keyboard navigation; confirm window resizing and 100%, 150%, and 200% DPI do not clip required controls.
- Switch icons among Aperture, Selene, and Aster; verify product surfaces and refresh shortcuts as needed.
- Register two independent Metis sources; verify ownership, current phase, weighted buckets, verified counts, budgets, and timestamps against the JSON.
- Test missing, unsupported, malformed, stale, and updated project sources. Ensure no source plan file is changed.
- Keep a project in a long wait: old source activity must not be displayed as proof the agent stopped running.
- Test a legacy workspace and both startup-enabled and startup-disabled registrations. Confirm no second workspace is created and disabled startup remains disabled.
- Run both `pandoractl.ps1` and the `orbitdockctl.ps1` compatibility wrapper on the same read-only command.
- Test real monitor disconnect/reconnect and Explorer recovery separately from static screenshot review.

## What to Test First

- Drag the zone headers around the desktop.
- Resize the zones from the window edge.
- Double-click a zone header to roll it up and down.
- Drag a small test file into a zone and confirm it copies into the underlying folder.
- Drag an item within a smart dock and confirm the order persists after restart.
- Drag an item between smart docks and confirm it moves virtually while the real shortcut remains in place.
- Click the search icon in a dock header, type a partial app name, and confirm results filter and `Esc` clears the search.
- Set a dock to bottom expansion with `.\scripts\pandoractl.ps1 dock set-expansion build bottom`, reload, and confirm it expands upward.
- Right-click an item, remove it from the dock, and confirm the real file still exists.
- Pin an item to the desktop overlay, move the pin, restart, and confirm its position persists.
- Run `.\scripts\pandoractl.ps1 layout list`, `layout variants`, and `workspace validate` while the app is running.
- Enable the music dock with `.\scripts\pandoractl.ps1 audio music on`, add a test `.mp3` or `.wav` under `%USERPROFILE%\Music\OrbitDock`, and confirm playlist scanning.
- Use the tray icon to open settings, reload, set docks behind windows, and exit.
- Press `Ctrl+Alt+Space` to send docks back behind active windows.
- Run `.\scripts\show-desktop-icons.ps1` if you ever need to restore the raw Windows desktop icon grid after a crash.
