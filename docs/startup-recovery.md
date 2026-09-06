# Sign-in startup and crash recovery

Publish Pandora, then run this once in PowerShell as your normal Windows desktop user:

```powershell
.\scripts\startup-pandora.ps1 -Mode Install
.\scripts\startup-pandora.ps1 -Mode Start
```

If Pandora is already running from an older build, exit it from the tray before
installing the updated portable build. Install does not kill or reboot anything.
Windows may require administrator approval to register a task under local policy;
the resulting app still uses your interactive, least-privileged desktop token.
Do not install as SYSTEM or as a different administrator account.

The single task is named `Pandora-<your Windows SID>`. It starts 15 seconds after
your sign-in, including after reboot. It does not run before sign-in. It works on
battery, has no runtime limit, does not require a network, and ignores duplicate
task starts. The task runs a headless Pandora parent plus one desktop child. The
parent watches only its own child handle: a nonzero child exit retries after one
minute, at most three times. It enforces this explicitly because Windows' native
RestartOnFailure setting did not relaunch the force-ended app in local testing.
Exhausting retries ends the parent successfully so there is no nested retry loop.
This is app crash recovery, not a hung-process or supervisor watchdog. If the
headless parent itself is terminated, run Start again. Repeated startup failures
stop retrying; investigate `%USERPROFILE%\.pandora\Diagnostics` before restarting.
The portable build's `Diagnostics/startup-recovery.json` also reports the current
child, retries used, last exit code, and whether the desktop app signaled `ready`
after loading its workspace, docks, tray, and hotkey. `startup-not-ready` is an
observation after 30 seconds, not a forced-kill policy. This bounded local file
contains no dashboard contents and is never uploaded.

The installer backs up and removes only this installation's `Pandora.lnk` and Run
entry. A same-name registration with a different target is rejected. Existing
task definitions are backed up before update in `artifacts/startup-backups`.
No workspace or project data is changed by the installer.

Pandora's **Start with Windows** setting enables/disables this task after it is
installed. A disabled task is not automatically re-enabled or replaced with a
shortcut. Ordinary manual launches also use the enabled task, so recovery remains
active. Without a managed task, the existing opt-in Startup shortcut still works.

```powershell
.\scripts\startup-pandora.ps1 -Mode Status
.\scripts\startup-pandora.ps1 -Mode Stop     # graceful exit + cancel pending retry
.\scripts\startup-pandora.ps1 -Mode Start    # resume now
.\scripts\startup-pandora.ps1 -Mode Disable  # no future sign-in/recovery; app stays open
.\scripts\startup-pandora.ps1 -Mode Enable   # enable future starts, does not launch
```

Tray **Exit** is intentional and returns success: Pandora stays closed until you
launch it again or sign in again. `scripts/stop-pandora.ps1` uses the same managed
stop path. Task Manager's force-end is a failure and may trigger recovery; use
the tray or Stop command for maintenance. Disable startup before long maintenance.
To revert to the old binary, disable/stop this task first; backups are not restored
automatically. Do not restore the old Startup shortcut while the task is enabled.

Scheduler semantics: [Microsoft RestartOnFailure documentation](https://learn.microsoft.com/en-us/windows/win32/taskschd/taskschedulerschema-restartonfailure-settingstype-element).
