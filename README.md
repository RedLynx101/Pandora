# Pandora

<p align="center">
  <img src="src/OrbitDock.App/Assets/Brand/Pandora-128.png" alt="Pandora aperture icon" width="96" height="96">
</p>

A native Windows desktop organizer for your files, tools, music, and long-running projects. Pandora pairs quiet celestial glass with local, readable data. No cloud account or agent-control server is required.

Pandora is the new name for OrbitDock. Existing workspaces, layouts, music folders, feed IDs, and automation entrypoints remain compatible; see [migration](#upgrading-from-orbitdock).

## What it does

- Desktop docks for app shortcuts, virtual categories, and folder portals.
- Named layouts with monitor-specific positions, roll-up state, tabs, ordering, and desktop pins.
- Classic, Halo, and Meridian dock structures with independent color palettes, optional custom colors, adjustable glass opacity, and reduced motion.
- Categorized settings, consistent menus, and three icon options.
- A read-only **Projects** dock for multiple Metis plans: current phase, owners, verified progress, attention, and source freshness.
- Local music and optional sounds, plus the Silk Current browser visualizer.
- Agent feed docks for briefs and checklists, separate from project verification.
- Local CLI, atomic workspace writes, tray controls, and recovery scripts.

## Appearance

Choose a **dock theme** for structure, then a **palette** for color. Every palette works with every structure:

| Dock theme | Structure |
| --- | --- |
| Classic | The familiar Pandora dock: an integrated frame and compact header |
| Halo | Floating rounded header and body, pill controls, and an airy tile grid |
| Meridian | Crisp frame with an accent rail, separate tab strip, and compact horizontal file tiles |

**Classic + Lunar** is the default. Palettes are **Lunar** (stored as `LunarGlass`), **Midnight**, **Limestone**, **Aegean**, and Windows-following **System**. Optional accent and surface color pickers accept `#RRGGBB`; Pandora derives readable text, focus, border, and status colors around those choices. Intentional per-dock overrides remain intact. High contrast uses Windows colors and opaque surfaces.

Choose **Compact**, **Standard**, or **Large** dock bars to scale names and controls together. Every theme has a single-line name bar, with multi-tab navigation below it only when expanded. **Dock surface opacity** includes the name/header background while keeping text and controls solid.

![Classic dock theme](screenshots/pandora-theme-classic.png)
![Halo dock theme](screenshots/pandora-theme-halo.png)
![Meridian dock theme](screenshots/pandora-theme-meridian.png)

![Pandora appearance settings](screenshots/pandora-lunar-glass.png)

These are offscreen renders of actual WPF controls, not desktop screenshots. See [testing](docs/testing.md) for live desktop/compositor checks. **Aperture** is the selected default icon; **Selene** and **Aster** remain available in settings.

Rolled-up docks retain their expanded size separately from the visible header. Moving or normalizing a closed dock keeps it closed; bottom-expanding docks keep their bottom anchor when reopened.

See [visual direction](docs/visual-direction.md) and [icon options](docs/pandora-icons.md).

See the [quality-pass record](docs/quality-pass.md) for persistence/recovery changes, safety regressions, and verification limits.

## Requirements and quick start

Windows 10 or 11 and the .NET 8 SDK with Windows Desktop support:

```powershell
dotnet restore Pandora.sln
dotnet build Pandora.sln
.\scripts\start-pandora.ps1 -NoBuild
```

Run core verification:

```powershell
dotnet run --project tests\OrbitDock.Tests
```

Run isolated WPF verification with an explicit absolute evidence directory:

```powershell
dotnet run --project tests\OrbitDock.App.Tests --configuration Release -- --output C:\absolute\task\work\pandora-ui-evidence
```

Choose a writable task directory for `--output`. The harness renders controls offscreen using fixture-local data; it does not start the desktop app or change your workspace. It cannot establish live compositor, monitor/DPI, or native popup behavior.

Publish and launch a local build:

```powershell
.\scripts\publish-portable.ps1
.\scripts\start-pandora.ps1 -FromPublish
.\scripts\start-pandora.ps1 -Settings
.\scripts\stop-pandora.ps1
```

Published executables are `artifacts\Pandora-win-x64\Pandora.App.exe` and `Pandora.Cli.exe`. Publishing does not install the app or enable startup.

Create desktop shortcuts, or repair an existing startup registration:

```powershell
.\scripts\install-test-shortcut.ps1 -SettingsShortcut
.\scripts\install-test-shortcut.ps1 -SettingsShortcut -StartupShortcut
```

The startup switch preserves the existing Windows enabled/disabled state; it does not opt you into startup. Enable that explicitly in Pandora settings. Recognized replaced shortcuts are backed up under `artifacts\shortcut-backups`; unrelated shortcut targets are left alone.

## Metis projects

Use **Add dashboard** in the Projects dock to register local Metis dashboard HTML files. Pandora extracts the embedded `codex-director-dashboard/v1` JSON document, without executing the HTML or modifying the source plan.

![Pandora Projects with synthetic example plans](screenshots/pandora-projects.png)

The project view separates implementation from verification and source freshness from agent liveness. Phase widths follow item counts. Named primary sessions and explicitly declared subagent budgets describe responsibility and planned capacity, not a count of live workers.

Pandora is a display companion, not a director. Source plans remain authoritative. Local reading or acknowledgement never approves work, verifies a phase, grants permissions, or sends instructions to agents. Existing agent-feed checklists remain independent.

See [Metis projects](docs/metis-projects.md) for the source format and integration boundaries.

## Local agent control

```powershell
.\scripts\pandoractl.ps1 workspace validate
.\scripts\pandoractl.ps1 workspace backup
.\scripts\pandoractl.ps1 layout list
.\scripts\pandoractl.ps1 dock set-bounds build 980 455 420 360
.\scripts\pandoractl.ps1 audio music on
.\scripts\pandoractl.ps1 agent-feed publish morning-brief --title "Morning Brief" --summary "Two items need attention." --status attention
```

The running app watches workspace and feed changes. Agents should use the CLI for writes, not rewrite a live workspace without its locking and validation rules. See [agent control](docs/agent-control.md) and [agent feeds](docs/agent-feeds.md).

## Upgrading from OrbitDock

The product and new executable names are Pandora. A deliberate compatibility layer avoids splitting your existing data:

| Kept stable | Reason |
| --- | --- |
| `%APPDATA%\OrbitDock\workspace.json` | Existing settings and layouts |
| `%APPDATA%\OrbitDock\AgentFeeds` and existing feed IDs | Existing automation and local read state |
| `%APPDATA%\OrbitDock\VirtualTabs` | Managed shortcut locations |
| `%USERPROFILE%\Music\OrbitDock` and configured audio paths | Existing playlists and files |
| `OrbitDock.sln`, source project folders, namespaces | Existing developer tooling and serialized compatibility |
| `start-orbitdock.ps1`, `stop-orbitdock.ps1`, `orbitdockctl.ps1` | Forwarders to the canonical Pandora scripts |

Use `Pandora.sln`, `start-pandora.ps1`, `stop-pandora.ps1`, and `pandoractl.ps1` for new integrations. Schema v6 introduced independent structure/custom colors; v7 adds bar sizing with Standard as the migration default. The legacy `theme` key remains the palette; `dockTheme` selects structure and `dockBarSize` selects bar scale. Existing `Graphite` values resolve to Lunar, and intentional custom dock colors remain intact. Legacy CustomFences workspace import is retained.

Do not rename or delete the compatibility folders manually. Back up your workspace before testing upgrades. The shortcut installer recognizes exact application paths from this checkout; if a shortcut points elsewhere, it refuses to replace it. No legacy artifact directory is recursively deleted.

## Safety

Smart-dock organization is virtual. Folder portals copy dropped files by default. Removing a dock or hiding an item does not delete its underlying files; moving a real file to the Recycle Bin is a separate confirmed action. Rule automation stays disabled.

Clean desktop mode hides the Windows icon grid while Pandora runs and restores it on normal exit. If recovery is needed:

```powershell
.\scripts\show-desktop-icons.ps1
```

Missing folders, audio files, and shell integration should produce recoverable status, not destructive fallback behavior. See [safety](docs/safety.md).

## Repository map

```text
src/OrbitDock.App/   WPF app, settings, tray, shell integration, audio, Projects
src/OrbitDock.Core/  Models, migration, layouts, scanners, local storage
src/OrbitDock.Cli/   Local CLI
tests/OrbitDock.Tests/  Dependency-light console verification
tests/OrbitDock.App.Tests/  Isolated WPF checks and offscreen evidence
docs/               Architecture, configuration, safety, testing, integration
scripts/            Publish, start/stop, shortcuts, recovery, CLI
tools/              Optional Silk Current visualizer
screenshots/        Product screenshots
```

Further reading: [architecture](docs/architecture.md), [configuration](docs/configuration.md), [desktop testing](docs/testing.md), [audio](docs/audio.md), [roadmap](docs/roadmap.md).

Pandora is alpha software. Windows shell recovery, mixed-DPI behavior, and monitor changes require continued real-world testing. This project is independent and is not affiliated with Stardock or other similarly named products.

Contributions: [CONTRIBUTING.md](CONTRIBUTING.md). Security: [SECURITY.md](SECURITY.md). License: [MIT](LICENSE).
