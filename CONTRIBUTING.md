# Contributing

CustomFences is a Windows desktop utility, so small defects can become annoying quickly. Keep changes focused and favor explicit fallbacks over clever shell integration.

## Development Rules

- Keep core behavior in `src/CustomFences.Core` when it can be tested without WPF.
- Keep shell, tray, hotkey, and WPF code in `src/CustomFences.App`.
- Do not make destructive filesystem behavior the default.
- Treat network folders and missing drives as normal states.
- Preserve readable JSON configuration and migration paths.
- Add tests in `tests/CustomFences.Tests` for path, rule, config, and safety behavior.

## Verification

Before opening a PR:

```powershell
dotnet build
dotnet run --project tests\CustomFences.Tests
```

For UI changes, manually launch the app and inspect the zone surface at common DPI settings if possible.
