# One shared user-data location

Pandora now uses `%USERPROFILE%\.pandora` for the default workspace, Projects registry,
agent feeds, virtual tabs, and runtime diagnostics. Explicit `--workspace` paths remain
isolated and unchanged. Music remains in its configured folder.

## Why this changed

Windows can redirect AppData reads and writes made by an MSIX-packaged terminal or
agent into that package's private `LocalCache\Roaming` directory. An unpackaged
scheduled desktop app can therefore read a different file despite the same nominal
`%APPDATA%\Pandora\projects.json` path. Refresh cannot repair two separate registries.
The profile-root location is shared across both launch contexts.
It uses the inherited absolute `USERPROFILE` value (with the OS known folder as a
fallback) so restricted tools with a temporary OS account retain the intended desktop
profile. Filesystem permissions still govern access; path resolution grants no access.

## Upgrade an existing installation

1. Stop Pandora and its supervisor with `scripts/startup-pandora.ps1 -Mode Stop`.
2. Open a normal Windows PowerShell from Start, outside a packaged terminal.
3. Run `scripts/migrate-user-data.ps1 -BackupDirectory C:\path\to\new-backup-folder`.
4. Publish/install the updated app and CLI together, then start Pandora.

The migration refuses a packaged process, linked source entries, a running app, an
existing destination, or an existing backup directory. It validates source JSON,
hash-verifies copies, preserves an original backup, and retains the entire legacy
directory. Only internal workspace paths that point to copied files/directories are
rebased. External dashboard and music paths, layouts, and playback preferences stay
unchanged. A failed preparation stays in the chosen backup directory for inspection.
No broad directory cleanup is performed.

If different legacy views have different Projects registrations, preserve both,
choose the actual desktop workspace as authority, and use the updated CLI's
`project add <exact-dashboard.html>` for each verified missing source. Do not replace
the active desktop workspace with a stale package-private workspace.

The app fails closed with migration instructions when it finds a legacy workspace
but no shared workspace. Read-only commands never import, merge, or create data.

## Verification and rollback

Compare the CLI's default workspace/registry paths with the scheduled app's
`.pandora\Diagnostics\runtime.json` `projects` and `projects-view` entries.
Check the actual source count and rendered summary, not just process readiness.
The Projects empty state also displays its registry path.

Before installation, retain the prior portable build. To roll back, stop Pandora,
restore that build, and launch it against the retained legacy directory. Changes made
in `.pandora` after migration will not be present in the old directory; reconcile them
explicitly if needed. Never overwrite either workspace to force agreement.
