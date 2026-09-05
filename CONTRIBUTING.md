# Contributing

Pandora is a Windows desktop utility, so small defects can become annoying quickly. Keep changes focused and favor explicit fallbacks over clever shell integration.

## Development Rules

- Keep core behavior in `src/Pandora.Core` when it can be tested without WPF.
- Keep shell, tray, hotkey, and WPF code in `src/Pandora.App`.
- Do not make destructive filesystem behavior the default.
- Treat network folders and missing drives as normal states.
- Preserve readable JSON configuration and migration paths.
- Add tests in `tests/Pandora.Tests` for path, rule, config, and safety behavior.

## Verification

Before opening a PR:

```powershell
dotnet build Pandora.sln
dotnet run --project tests\Pandora.Tests
```

For UI changes, manually launch the app and inspect the dock and settings surfaces in each theme, at common DPI settings, and with keyboard focus visible. Separate automated results from manual coverage; do not claim a mixed-DPI or monitor-transition test from a static screenshot.

Projects integration is read-only: adding a UI affordance must not silently become agent authority or plan acceptance.
