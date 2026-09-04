# Pandora WPF verification

An isolated Windows-only executable test harness for the actual WPF resources and controls. No test framework or browser is required.

```powershell
dotnet run --project tests/OrbitDock.App.Tests -c Release -- --output C:\absolute\task\work\ui-evidence
```

`--output` must be an explicit absolute directory. Every run creates a unique child directory containing PNG render evidence, fixture-local storage, and a JSON result report. It never deletes an existing run.

The harness loads the same compiled `Themes/PandoraControls.xaml` resource dictionary as `App.xaml` into a plain test `Application`. The product `App` is never instantiated, so normal startup/exit cannot execute. It never calls the desktop manager's `Start` or `Reload`, never shows a window, never invokes startup-registration or desktop-icon changes, and never loads the current user's workspace. All mutable stores use the run's fixture directory. Display/theme preference reads are read-only. Appearance event handlers are exercised only where they do not invoke shell behavior.

PNG files are deterministic offscreen renders of the actual WPF content using `Measure`, `Arrange`, and `RenderTargetBitmap`, not screenshots of the user's desktop or HTML approximations. These tests do **not** demonstrate live taskbar/tray behavior, Explorer layering, mixed-DPI monitor movement, keyboard focus across native popups, or GPU/compositor transparency. Those remain separate manual Windows acceptance checks.
