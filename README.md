# OrbitDock

OrbitDock is a free, open-source Windows desktop organizer. It creates customizable desktop docks that group app shortcuts, mirror folders, roll up out of the way, and stay behind normal app windows.

This project is an independent implementation. It is not affiliated with Stardock and does not copy Stardock assets, branding, or proprietary behavior. The goal is to build a safe, customizable desktop organization tool in the same broad product category.

## What Works Now

- Native WPF desktop dock windows with translucent styling.
- Smart desktop categories for apps, dev tools, creative apps, games, utilities, folders, and files.
- Clean desktop mode that hides the raw Windows desktop icon grid while OrbitDock is running.
- Generated OrbitDock app/tray branding under `src\CustomFences.App\Assets\Brand`.
- Per-user JSON workspace at `%APPDATA%\CustomFences\workspace.json`.
- Starter docks for launchers, dev tools, creative apps, games, and desktop inbox files.
- Folder portals that refresh when the underlying folder changes.
- Drag-and-drop into a zone. The default action is copy, not move.
- Roll-up/collapse per zone.
- Multi-tab zone support in the workspace model and zone UI.
- Tray menu for settings, layer reset, reload, config access, and exit.
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

Important defaults:

- `defaultDropAction` is `copy`.
- `enableRuleAutomation` is `false`.
- `attachWindowsToDesktop` can be set to `false` if shell attachment misbehaves on a machine.

## Safety Model

OrbitDock starts as a portal-first organizer. It hides the raw Windows desktop icon grid while running so the dock layer can replace the visual clutter, but it does not delete or rearrange desktop items. Run `.\scripts\show-desktop-icons.ps1` if you need to restore the raw icon grid manually after a crash. Riskier actions, such as moving files or automated rule execution, are explicit workspace settings.

See [docs/safety.md](docs/safety.md) for the full posture.

## Documentation

- [Research notes](docs/research-notes.md)
- [Product brief](docs/product-brief.md)
- [Architecture](docs/architecture.md)
- [Configuration](docs/configuration.md)
- [Safety](docs/safety.md)
- [Desktop testing](docs/testing.md)
- [Roadmap](docs/roadmap.md)
- [Visual direction](docs/visual-direction.md)

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Keep changes small, testable, and conservative around filesystem and shell behavior.
