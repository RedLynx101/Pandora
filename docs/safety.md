# Safety

CustomFences should earn trust before it touches a user's desktop layout.

## Current Defaults

- Drag-and-drop copies files instead of moving them.
- Deleting a zone removes only zone metadata, not files.
- Rule automation is disabled.
- Clean desktop mode hides the raw Windows icon grid while the app is running and restores it on normal exit.
- `scripts/show-desktop-icons.ps1` is provided as a manual restore fallback.
- Shell attachment is best effort and can be disabled.
- Missing or offline portal folders show status text instead of blocking startup.
- The app does not require administrator privileges.

## Filesystem Rules

- Never delete user files as part of zone management.
- Prefer copy over move unless the user explicitly opts in.
- Use unique destination names to avoid overwriting existing files.
- Treat network folders, removable drives, and cloud-sync folders as unreliable.
- Show recoverable errors in the zone status line instead of crashing.

## Windows Shell Rules

- Keep shell interop isolated.
- Fail closed when shell handles cannot be found.
- Avoid replacing Explorer behavior.
- Avoid writing registry settings until a user-facing setting and rollback path exist.
- If hiding shell icons, always preserve a script-level fallback that shows them again without requiring the app to be running.

## Future Risk Gates

Before adding these features, require dry-run preview and tests:

- Automated file sorting.
- Desktop icon capture/hide/restore.
- Startup registration.
- Layout restore after monitor changes.
- Shortcut creation.
