# Architecture

## Projects

- `src/CustomFences.Core`
  - Workspace models
  - JSON storage
  - Path expansion/compression
  - Rule matching
- `src/CustomFences.App`
  - WPF zone windows
  - Settings window
  - Tray icon
  - Global hotkey
  - Desktop shell attachment
  - File/folder portal UI
- `tests/CustomFences.Tests`
  - Dependency-light console tests for the core library

## Runtime Flow

1. `App` enforces single-instance startup.
2. `WorkspaceStore.ForCurrentUser()` loads or creates `%APPDATA%\CustomFences\workspace.json`.
3. `DesktopZoneManager` creates a `ZoneWindow` for each visible zone.
4. Each `ZoneWindow` owns a `ZoneViewModel` that enumerates the active folder portal and watches it with `FileSystemWatcher`.
5. If enabled, `DesktopHost.TryAttach` attempts to parent the zone to the Windows desktop shell surface.
6. If shell attachment fails, zones remain normal borderless WPF windows.
7. Tray menu and `Ctrl+Alt+Space` operate through `DesktopZoneManager`.

## Persistence

Workspace writes use a temporary file and `File.Replace` where possible, which prevents partial writes from corrupting an existing config.

## Shell Attachment

Desktop attachment is intentionally isolated in `DesktopHost`. This is a brittle Windows shell integration point, so it returns `false` instead of throwing. Users can disable the attempt with:

```json
"attachWindowsToDesktop": false
```

## Rules

The first rule engine is matching-only. It supports extension, file-name, and parent-path conditions with equals, contains, starts-with, ends-with, and regex matches. Execution is intentionally not automatic yet.

Next step: add a dry-run planner that returns proposed copy/move operations before any filesystem mutation.
