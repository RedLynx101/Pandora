# Agent Control

Pandora is designed to be local-agent friendly without exposing a network service. Agents should use the shared JSON workspace plus `pandoractl`; the store writes atomically and uses a simple `.lock` file next to the workspace.

Default workspace:

```text
%APPDATA%\OrbitDock\workspace.json
```

Run from the repository root:

```powershell
.\scripts\pandoractl.ps1 workspace validate
```

## Layouts

```powershell
.\scripts\pandoractl.ps1 layout list
.\scripts\pandoractl.ps1 layout variants
.\scripts\pandoractl.ps1 layout use-variant default
.\scripts\pandoractl.ps1 layout save Research
.\scripts\pandoractl.ps1 layout switch Main
.\scripts\pandoractl.ps1 layout duplicate Main Travel
.\scripts\pandoractl.ps1 layout delete Travel
```

## Docks

Dock identifiers are stable IDs such as `launchpad`, `build`, `create`, `play`, and `desktop-inbox`.

```powershell
.\scripts\pandoractl.ps1 dock list
.\scripts\pandoractl.ps1 dock hide play
.\scripts\pandoractl.ps1 dock show build
.\scripts\pandoractl.ps1 dock set-bounds build 980 455 420 360
.\scripts\pandoractl.ps1 dock set-expansion build bottom
```

## Items

Item commands are virtual unless they target a real folder dock through the running UI. These commands do not move or delete the underlying file.

```powershell
.\scripts\pandoractl.ps1 item pin "$env:USERPROFILE\Desktop\Visual Studio Code.lnk" --dock build
.\scripts\pandoractl.ps1 item unpin "$env:USERPROFILE\Desktop\Steam.lnk" --dock play
.\scripts\pandoractl.ps1 item move "$env:USERPROFILE\Desktop\Postman.lnk" --from launchpad --to build
.\scripts\pandoractl.ps1 item order build "$env:USERPROFILE\Desktop\Postman.lnk" "$env:USERPROFILE\Desktop\Docker Desktop.lnk"
```

## Desktop Pins

Desktop pins are Pandora overlay icons. They are useful when clean desktop mode is enabled but a few items should remain visible.

```powershell
.\scripts\pandoractl.ps1 desktop-pin add "$env:USERPROFILE\Desktop\Steam.lnk" --x 120 --y 220
.\scripts\pandoractl.ps1 desktop-pin list
.\scripts\pandoractl.ps1 desktop-pin remove "$env:USERPROFILE\Desktop\Steam.lnk"
```

## Audio

```powershell
.\scripts\pandoractl.ps1 audio sfx on
.\scripts\pandoractl.ps1 audio music on
.\scripts\pandoractl.ps1 audio set-music-folder "$env:USERPROFILE\Music\OrbitDock"
```

## Validation and Backups

```powershell
.\scripts\pandoractl.ps1 workspace validate
.\scripts\pandoractl.ps1 workspace backup
```

The running app watches the workspace file and reloads after external changes, so CLI edits should appear without restarting Pandora.

## Agent Feeds

Agent feeds are for frequently changing briefs, checklists, and status cards. They are stored outside the workspace so agents can update them often without rewriting dock layout state.

Default feed folder:

```text
%APPDATA%\OrbitDock\AgentFeeds
```

Publish a small update:

```powershell
.\scripts\pandoractl.ps1 agent-feed publish morning-brief --title "Morning Brief" --summary "Two items need attention." --status attention
```

Publish a brief with checklist items from a JSON or line-based file:

```powershell
.\scripts\pandoractl.ps1 agent-feed publish morning-brief --title "Morning Brief" --summary "Calendar is heavy today." --checklist-file .\brief-checklist.json --status actionNeeded
```

Write a full feed document:

```powershell
.\scripts\pandoractl.ps1 agent-feed write morning-brief --file .\morning-brief.feed.json
```

Manage read and local checklist state:

```powershell
.\scripts\pandoractl.ps1 agent-feed list
.\scripts\pandoractl.ps1 agent-feed show morning-brief
.\scripts\pandoractl.ps1 agent-feed mark-read morning-brief
.\scripts\pandoractl.ps1 agent-feed complete morning-brief email-1
.\scripts\pandoractl.ps1 agent-feed reopen morning-brief email-1
```

See [agent-feeds.md](agent-feeds.md) for the schema and morning brief integration pattern.
