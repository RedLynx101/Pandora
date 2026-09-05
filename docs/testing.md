# Desktop Testing

Use these scripts from the repository root.

This is a test checklist, not a claim that every scenario has passed on every Windows configuration. Record the exact commit, display setup, commands, and untested cases in a release review.

```powershell
dotnet build Pandora.sln --configuration Release
dotnet run --project tests\Pandora.Tests --configuration Release --no-build
node --test tools/SilkCurrentVisualizer/tests/visualizer.test.cjs
.\tools\SilkCurrentVisualizer\tests\server.test.ps1
```

## Isolated WPF verification

Safety regressions cover workspace conflicts and byte preservation, read-only CLI validation, feed identity/state limits, project registry isolation, bounded file transfer, saved music selection, watcher disposal, and recoverable lifecycle errors. Windows directory junction fixtures run without requiring file-symlink privileges; file-symlink cases print explicit `SKIP` when unavailable. The visualizer tests use mock media APIs and a temporary loopback-only server that closes after testing; no real capture permission is requested. Windows must permit HttpListener initialization for the server test.

After the solution build, choose a writable **absolute** task directory for render evidence:

```powershell
dotnet run --project tests\Pandora.App.Tests --configuration Release --no-build -- --output C:\absolute\task\work\pandora-ui-evidence
```

Each run creates a unique child directory containing fixture-local data, offscreen PNG renders, and a JSON report. The harness uses the actual WPF resources and controls without instantiating the product `App`, showing desktop windows, loading the user's workspace, or changing startup registration. See [harness boundaries](../tests/Pandora.App.Tests/README.md).

These checks do not establish GPU/compositor transparency, live tray/taskbar behavior, Explorer layering, mixed-DPI monitor movement, or keyboard focus across native popups. Review those on real Windows hardware separately. Offscreen renders are not desktop screenshots. Record actual suite results and CI state for the tested commit; this document does not claim a new pass count or successful run.

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

This stops the app, backs up `%APPDATA%\Pandora\workspace.json`, and removes it. The app creates a fresh default workspace on next launch.

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

## Structure, color, and migration review

- Compare Classic, Halo, and Meridian using the same palette. Confirm that header/body structure, controls, tab treatment, spacing, and file-tile layout differ as intended—not only colors.
- Inspect every structure with Lunar, Midnight, Limestone, and Aegean on light and dark wallpapers. Confirm labels, menus, focus indicators, selected states, and disabled controls remain legible.
- Select System and change the Windows app theme; test Windows high contrast separately.
- Use the accent/surface color pickers and valid `#RRGGBB` input, including very light and dark surfaces. Check derived text, borders, focus, and status roles. Invalid drafts must not silently apply; clearing overrides should return to palette-derived colors.
- Change transparency and reduced motion, save, restart, and verify the preference persists without changing intentional dock colors.
- Load a schema v5 workspace with a nondefault palette and icon; migrate to v6 and confirm those choices, per-dock overrides, layouts, and item data are preserved. Verify `theme` still stores the palette and `dockTheme` stores structure independently.
- Open each settings section using keyboard navigation; confirm window resizing and 100%, 150%, and 200% DPI do not clip required controls.
- Confirm Aperture is the default; switch to Selene or Aster, save/reopen, and verify the choice is preserved. Refresh shortcuts as needed.

## Rolled-up movement regression

Automated reliability checks cover 100 coalesced placement updates during one
gesture, final collapsed-state persistence, a paused worker save with a newer UI
edit, and synchronous settings save ordering. Core fixtures cover detached-snapshot
conflicts, five-slot recovery rotation, explicit restore rollback, and oversized
workspace rejection. These checks do not simulate Windows compositor behavior.

- For each structure, record a dock's expanded size, collapse it, drag the visible header, and confirm it stays closed. Reopen it and verify the remembered expanded size and new location.
- Repeat with top and bottom expansion. Bottom-anchored docks must retain the lower edge and expand upward without jumping or replacing saved height with header height.
- Repeat movement through coordinate normalization and layout save/reload. Confirm `isCollapsed` is preserved and saved expanded bounds remain distinct from the visible collapsed projection.
- Change structure while collapsed, then reopen. Different header heights must not corrupt the remembered geometry.
- Test actual display transitions and mixed-DPI movement separately from fixture-level bounds checks.

## Projects and existing behavior

The offscreen harness also constructs a Projects control that never receives
`Loaded`, verifies its automatic initial read, injects a registry failure, and
checks recovery and concurrent-refresh completion. No live user registry is used.

- Register two independent Metis sources; verify ownership, current phase, weighted buckets, verified counts, budgets, and timestamps against the JSON.
- Test missing, unsupported, malformed, stale, and updated project sources. Ensure no source plan file is changed.
- Keep a project in a long wait: old source activity must not be displayed as proof the agent stopped running.
- Test a supported older-schema workspace and both startup-enabled and startup-disabled registrations. Confirm no second workspace is created and disabled startup remains disabled.
- Run `project list`, `project add`, and `project remove` against an explicit fixture workspace; verify dashboard bytes remain unchanged.
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
- Enable the music dock with `.\scripts\pandoractl.ps1 audio music on`, add a test `.mp3` or `.wav` under `%USERPROFILE%\Music\Pandora`, and confirm playlist scanning.
- Use the tray icon to open settings, reload, set docks behind windows, and exit.
- Press `Ctrl+Alt+Space` to send docks back behind active windows.
- Run `.\scripts\show-desktop-icons.ps1` if you ever need to restore the raw Windows desktop icon grid after a crash.
