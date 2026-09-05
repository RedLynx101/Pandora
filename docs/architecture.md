# Architecture

## Projects

- `src/Pandora.Core`
  - Workspace models
  - JSON storage
  - Layout migration and profile manipulation
  - Display-variant layout state
  - Music library scanning
  - Path expansion/compression
  - Rule matching
  - Metis document reading and local project registry
- `src/Pandora.App`
  - WPF zone windows
  - Settings window
  - Tray icon
  - Global hotkey
  - Desktop shell attachment
  - File/folder portal UI
  - Shared theme resources and read-only Projects UI
- `src/Pandora.Cli`
  - Local-agent command surface backed by the same workspace schema
- `tests/Pandora.Tests`
  - Dependency-light console tests for the core library

## Runtime Flow

1. `App` enforces single-instance startup.
2. `WorkspaceStore.ForCurrentUser()` loads or creates `%APPDATA%\Pandora\workspace.json`.
3. `WorkspaceMigrator` upgrades earlier data to the current schema while retaining supported layout profiles and settings.
4. `DesktopZoneManager` computes the current display signature, applies the matching layout variant, creates a `ZoneWindow` for each visible dock, and creates `DesktopPinWindow` overlays for active desktop pins.
5. Each `ZoneWindow` owns a `ZoneViewModel` that enumerates the active folder or smart desktop tab, applies virtual item overrides, and watches underlying folders with `FileSystemWatcher`.
6. If enabled, `DesktopHost.TryAttach` attempts to parent the zone to the Windows desktop shell surface.
7. If shell attachment fails, zones remain normal borderless WPF windows that are sent behind normal app windows.
8. Tray menu, workspace file watching, settings, optional audio, `pandoractl`, and `Ctrl+Alt+Space` operate through `DesktopZoneManager`.

## Persistence

Workspace writes use a temporary file, a sibling `.lock` file, and `File.Replace` where possible, which prevents partial writes from corrupting an existing config. The WPF app watches the workspace and reloads after external CLI changes.

Canonical binaries are `Pandora.App.exe` and `Pandora.Cli.exe`. `Pandora.sln` contains the app, core, CLI, and isolated test projects.

## Appearance

`ThemeService` supplies shared dynamic resources for app surfaces. It resolves Lunar, Midnight, Limestone, Aegean, and the Windows-following System preference, and handles high contrast and reduced motion. Theme selection does not rewrite deliberate per-dock color overrides. `BrandIdentity` resolves the selected product icon independently from dock layout.

## Project portfolio boundary

`ProjectRegistryStore` holds explicitly registered local dashboard paths; `MetisReader` reads the supported versioned JSON without running HTML. `ProjectPortfolioService` turns those sources into read-only project summaries for `ProjectsControl`.

Each project retains its own source plan, revision, director, and evidence. Pandora does not combine them into a mutable master plan or give a local checkbox authority to accept work. Source freshness, file-read success, and actual agent liveness are separate facts. See [Metis projects](metis-projects.md) for the data contract.

## Shell Attachment

Desktop attachment is intentionally isolated in `DesktopHost`. This is a brittle Windows shell integration point, so it returns `false` instead of throwing. Users can disable the attempt with:

```json
"attachWindowsToDesktop": false
```

## Rules

The first rule engine is matching-only. It supports extension, file-name, and parent-path conditions with equals, contains, starts-with, ends-with, and regex matches. Execution is intentionally not automatic yet.

Next step: add a dry-run planner that returns proposed copy/move operations before any filesystem mutation.
