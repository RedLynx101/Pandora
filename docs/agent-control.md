# Agent Control

OrbitDock is designed to be local-agent friendly without exposing a network service. Agents should use the shared JSON workspace plus `orbitdockctl`; the store writes atomically and uses a simple `.lock` file next to the workspace.

Default workspace:

```text
%APPDATA%\OrbitDock\workspace.json
```

Run from the repository root:

```powershell
.\scripts\orbitdockctl.ps1 workspace validate
```

## Layouts

```powershell
.\scripts\orbitdockctl.ps1 layout list
.\scripts\orbitdockctl.ps1 layout save Research
.\scripts\orbitdockctl.ps1 layout switch Main
.\scripts\orbitdockctl.ps1 layout duplicate Main Travel
.\scripts\orbitdockctl.ps1 layout delete Travel
```

## Docks

Dock identifiers are stable IDs such as `launchpad`, `build`, `create`, `play`, and `desktop-inbox`.

```powershell
.\scripts\orbitdockctl.ps1 dock list
.\scripts\orbitdockctl.ps1 dock hide play
.\scripts\orbitdockctl.ps1 dock show build
.\scripts\orbitdockctl.ps1 dock set-bounds build 980 455 420 360
```

## Items

Item commands are virtual unless they target a real folder dock through the running UI. These commands do not move or delete the underlying file.

```powershell
.\scripts\orbitdockctl.ps1 item pin "$env:USERPROFILE\Desktop\Visual Studio Code.lnk" --dock build
.\scripts\orbitdockctl.ps1 item unpin "$env:USERPROFILE\Desktop\Steam.lnk" --dock play
.\scripts\orbitdockctl.ps1 item move "$env:USERPROFILE\Desktop\Postman.lnk" --from launchpad --to build
.\scripts\orbitdockctl.ps1 item order build "$env:USERPROFILE\Desktop\Postman.lnk" "$env:USERPROFILE\Desktop\Docker Desktop.lnk"
```

## Desktop Pins

Desktop pins are OrbitDock overlay icons. They are useful when clean desktop mode is enabled but a few items should remain visible.

```powershell
.\scripts\orbitdockctl.ps1 desktop-pin add "$env:USERPROFILE\Desktop\Steam.lnk" --x 120 --y 220
.\scripts\orbitdockctl.ps1 desktop-pin list
.\scripts\orbitdockctl.ps1 desktop-pin remove "$env:USERPROFILE\Desktop\Steam.lnk"
```

## Validation and Backups

```powershell
.\scripts\orbitdockctl.ps1 workspace validate
.\scripts\orbitdockctl.ps1 workspace backup
```

The running app watches the workspace file and reloads after external changes, so CLI edits should appear without restarting OrbitDock.
