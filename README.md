# OrbitDock

OrbitDock is a free, open-source Windows desktop organizer. It creates customizable desktop docks that group app shortcuts, mirror folders, roll up out of the way, and stay behind normal app windows.

This project is an independent implementation. It is not affiliated with Stardock and does not copy Stardock assets, branding, or proprietary behavior. The goal is to build a safe, customizable desktop organization tool in the same broad product category.

## What Works Now

- Native WPF desktop dock windows with translucent styling.
- Smart desktop categories for apps, dev tools, creative apps, games, utilities, folders, and files.
- Clean desktop mode that hides the raw Windows desktop icon grid while OrbitDock is running.
- OrbitDock-managed desktop pins for items that should remain visible while the raw icon grid is hidden.
- Generated OrbitDock app/tray branding under `src\CustomFences.App\Assets\Brand`.
- Per-user JSON workspace at `%APPDATA%\OrbitDock\workspace.json`, with one-time import from `%APPDATA%\CustomFences\workspace.json`.
- Named layout profiles with saved dock positions, active tabs, collapsed/visible state, item ordering, dock membership, and desktop pins.
- Screen-combination-aware layout variants so a named layout can remember different positions for different monitor setups.
- Starter docks for launchers, dev tools, creative apps, games, and desktop inbox files.
- Folder portals that refresh when the underlying folder changes.
- Per-dock search that expands from the header and filters by name, extension, and path.
- Dock bounds are guarded against Windows snap/full-screen states, with Settings actions to restore a selected dock or repair oversized dock sizes.
- Drag-and-drop organization. Smart docks use virtual membership and ordering; explicit folder docks still copy by default.
- Optional sound effects and an optional local music dock for `%USERPROFILE%\Music\OrbitDock`.
- Context menu actions for remove-from-dock, pin-to-desktop, reveal, and confirmed real deletion.
- Roll-up/collapse per zone.
- Multi-tab zone support in the workspace model and zone UI.
- `orbitdockctl` CLI for local agents and scripts to validate workspaces, switch layouts, move dock items, and manage desktop pins.
- Tray menu for settings, layer reset, reload, config access, and exit.
- Settings can add or remove OrbitDock from Windows startup apps.
- `Ctrl+Alt+Space` layer-reset hotkey that sends docks behind active windows.
- Best-effort shell attachment remains available, but the default test layout uses normal windows plus clean-desktop mode for better reliability.
- Core rule matching library with starter rule templates. Rule automation is disabled by default.

## Build

Requirements:

- Windows 10 or 11
- .NET 8 SDK with Windows Desktop runtime

```powershell
dotnet restore
dotnet build
dotnet run --project src\CustomFences.App
```

Run the lightweight core verification suite:

```powershell
dotnet run --project tests\CustomFences.Tests
```

## Desktop Test Build

For normal testing, publish a framework-dependent Windows build and launch it from `artifacts`:

```powershell
.\scripts\publish-portable.ps1
.\scripts\start-customfences.ps1 -FromPublish
```

Open settings immediately:

```powershell
.\scripts\start-customfences.ps1 -FromPublish -Settings
```

Stop the app:

```powershell
.\scripts\stop-customfences.ps1
```

Create a desktop shortcut for the published test build:

```powershell
.\scripts\install-test-shortcut.ps1
```

See [docs/testing.md](docs/testing.md) for the full testing loop and reset command.

## Configure

Open settings from the tray icon or run:

```powershell
dotnet run --project src\CustomFences.App -- --settings
```

The workspace JSON is intentionally human-readable. You can add zones, tabs, colors, paths, and rule templates directly.

Use the CLI for agent-safe changes:

```powershell
.\scripts\orbitdockctl.ps1 layout list
.\scripts\orbitdockctl.ps1 layout variants
.\scripts\orbitdockctl.ps1 dock set-bounds build 980 455 420 360
.\scripts\orbitdockctl.ps1 dock set-expansion build bottom
.\scripts\orbitdockctl.ps1 item pin "$env:USERPROFILE\Desktop\Visual Studio Code.lnk" --dock build
.\scripts\orbitdockctl.ps1 desktop-pin add "$env:USERPROFILE\Desktop\Steam.lnk" --x 120 --y 220
.\scripts\orbitdockctl.ps1 audio music on
.\scripts\orbitdockctl.ps1 workspace validate
```

Important defaults:

- `defaultDropAction` is `copy`.
- `enableRuleAutomation` is `false`.
- `attachWindowsToDesktop` can be set to `false` if shell attachment misbehaves on a machine.
- Smart-dock item moves are virtual. Real files and shortcuts remain where they are unless you explicitly use a folder dock drop or confirm a real delete action.

## Safety Model

OrbitDock starts as a portal-first organizer. It hides the raw Windows desktop icon grid while running so the dock layer can replace the visual clutter, but it does not delete or rearrange desktop items. Run `.\scripts\show-desktop-icons.ps1` if you need to restore the raw icon grid manually after a crash. Riskier actions, such as moving files or automated rule execution, are explicit workspace settings.

See [docs/safety.md](docs/safety.md) for the full posture.

## Documentation

- [Research notes](docs/research-notes.md)
- [Product brief](docs/product-brief.md)
- [Architecture](docs/architecture.md)
- [Configuration](docs/configuration.md)
- [Agent control](docs/agent-control.md)
- [Audio](docs/audio.md)
- [Safety](docs/safety.md)
- [Desktop testing](docs/testing.md)
- [Roadmap](docs/roadmap.md)
- [Visual direction](docs/visual-direction.md)

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Keep changes small, testable, and conservative around filesystem and shell behavior.
