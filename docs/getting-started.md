# Getting started

## Build and run

Use Windows 10 or 11 and the .NET 8 SDK with Windows Desktop support. Clone the repository, then run:

```powershell
dotnet build Pandora.sln
.\scripts\start-pandora.ps1 -NoBuild -Settings
```

The starter workspace appears on first launch. Open Settings from the tray to customize docks, layouts, appearance, and optional audio. Startup is opt-in. Double-click a dock header to roll it up; use its menu for dock actions.

## Portable output

```powershell
.\scripts\publish-portable.ps1
.\scripts\start-pandora.ps1 -FromPublish
```

The output is `artifacts\Pandora-win-x64`, containing the app and CLI. Keep the whole folder together. The default publish needs the .NET 8 Windows Desktop Runtime on the destination machine; see the publish script's self-contained option if you need to bundle it. This is not a signed installer.

Desktop shortcuts are optional:

```powershell
.\scripts\install-test-shortcut.ps1 -SettingsShortcut
```

The shortcut installer's startup-repair option preserves an existing registration's Windows approval state; it does not opt an unregistered app into startup.

## Your data

Pandora stores its workspace, feeds, virtual-tab contents, and project registry under `%USERPROFILE%\.pandora`. Configured folder docks refer to your existing files. The default local music directory is `%USERPROFILE%\Music\Pandora`.

Back up the entire data folder before testing upgrades. For a workspace-only snapshot, use `.\scripts\pandoractl.ps1 workspace backup`. Back up configured music and folder contents separately. [Configuration details](configuration.md)

## Stop and recover

Exit from the tray or run `.\scripts\stop-pandora.ps1`. If Windows desktop icons remain hidden after a crash, run `.\scripts\show-desktop-icons.ps1`.

A malformed or unsupported existing workspace is not silently replaced. Stop Pandora, preserve the failing file, restore a known backup, and restart. Avoid the reset script unless you intentionally want a fresh workspace.

While the app is running, **Settings → Desktop → Recovery & diagnostics** can
restore a selected workspace backup after confirmation. The current bytes are
backed up first. Five automatic snapshots live in `workspace.json.recovery`, with
at most one new snapshot per five minutes of changes; manual and migration backups
are retained separately. These are workspace snapshots, not backups of folder
contents, music, feeds, or the project registry.

If docks seem to have reset, check **Layouts** for the selected monitor arrangement
and **Docks** for hidden docks before restoring anything. A different display
configuration can select a different saved arrangement without deleting docks.

For a failure report, inspect `%USERPROFILE%\.pandora\Diagnostics\runtime.json` and
`errors.log` (plus its bounded `.previous` file). They record the build, selected
layout, save status, and project-refresh counts/errors. They remain local and may
contain paths or error details; review before sharing. A missing crash entry is
not proof that the app did not crash, especially during system resource exhaustion.

To remove Pandora, exit, disable its startup entry if enabled, and remove only the shortcuts and portable program folder you installed. Keep or separately archive your data folder and music. Do not delete folder-dock source directories as part of uninstalling.
