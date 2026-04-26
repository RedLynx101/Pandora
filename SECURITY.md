# Security Policy

## Supported Versions

OrbitDock is currently alpha software. Security fixes are accepted against the default branch.

## Reporting a Vulnerability

Please open a private report with enough detail to reproduce the issue, including:

- OrbitDock version or commit.
- Windows version.
- Steps to reproduce.
- Whether the issue can move, delete, expose, or corrupt user files.

Do not include secrets, access tokens, private documents, or full desktop captures unless they are necessary and redacted.

## Security Posture

OrbitDock is designed to avoid destructive defaults:

- Smart-dock organization is virtual.
- Folder docks copy by default.
- Real deletion requires confirmation.
- Rule automation is disabled until an executor and dry-run flow are implemented.
- Workspace writes are atomic and protected by a local lock file.
- No network service is exposed for agent control.

See [docs/safety.md](docs/safety.md) for operational safety details.
