# Architecture

## Projects

- `src/CustomFences.Core`
  - Workspace models
  - JSON storage
  - Layout migration and profile manipulation
  - Path expansion/compression
  - Rule matching
- `src/CustomFences.App`
  - WPF zone windows
  - Settings window
  - Tray icon
  - Global hotkey
  - Desktop shell attachment
  - File/folder portal UI
- `src/OrbitDock.Cli`
  - Local-agent command surface backed by the same workspace schema
- `tests/CustomFences.Tests`
  - Dependency-light console tests for the core library

## Runtime Flow

1. `App` enforces single-instance startup.
2. `WorkspaceStore.ForCurrentUser()` loads or creates `%APPDATA%\OrbitDock\workspace.json` and imports the legacy CustomFences path once if needed.
3. `WorkspaceMigrator` upgrades v1 data into schema v2 with a `Main` layout profile.
4. `DesktopZoneManager` applies the active layout, creates a `ZoneWindow` for each visible dock, and creates `DesktopPinWindow` overlays for active desktop pins.
5. Each `ZoneWindow` owns a `ZoneViewModel` that enumerates the active folder or smart desktop tab, applies virtual item overrides, and watches underlying folders with `FileSystemWatcher`.
6. If enabled, `DesktopHost.TryAttach` attempts to parent the zone to the Windows desktop shell surface.
7. If shell attachment fails, zones remain normal borderless WPF windows that are sent behind normal app windows.
8. Tray menu, workspace file watching, settings, `orbitdockctl`, and `Ctrl+Alt+Space` operate through `DesktopZoneManager`.

## Persistence

Workspace writes use a temporary file, a sibling `.lock` file, and `File.Replace` where possible, which prevents partial writes from corrupting an existing config. The WPF app watches the workspace and reloads after external CLI changes.

## Shell Attachment

Desktop attachment is intentionally isolated in `DesktopHost`. This is a brittle Windows shell integration point, so it returns `false` instead of throwing. Users can disable the attempt with:

```json
"attachWindowsToDesktop": false
```

## Rules

The first rule engine is matching-only. It supports extension, file-name, and parent-path conditions with equals, contains, starts-with, ends-with, and regex matches. Execution is intentionally not automatic yet.

Next step: add a dry-run planner that returns proposed copy/move operations before any filesystem mutation.
