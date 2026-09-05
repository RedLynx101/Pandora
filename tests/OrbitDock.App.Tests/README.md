# Pandora WPF verification

An isolated Windows-only executable test harness for the actual WPF resources and controls. No test framework or browser is required.

```powershell
dotnet run --project tests/OrbitDock.App.Tests -c Release -- --output C:\absolute\task\work\ui-evidence
```

`--output` must be an explicit absolute directory. Every run creates a unique child directory containing PNG render evidence, fixture-local storage, and a JSON result report. It never deletes an existing run.

The harness loads the same compiled `Themes/PandoraControls.xaml` resource dictionary as `App.xaml` into a plain test `Application`. The product `App` is never instantiated, so normal startup/exit cannot execute. It never calls the desktop manager's `Start` or `Reload`, never shows a window, never invokes startup-registration or desktop-icon changes, and never loads the current user's workspace. All mutable stores use the run's fixture directory. Display/theme preference reads are read-only. Appearance event handlers are exercised only where they do not invoke shell behavior.

PNG files are deterministic offscreen renders of the actual WPF content using `Measure`, `Arrange`, and `RenderTargetBitmap`, not screenshots of the user's desktop or HTML approximations. These tests do **not** demonstrate live taskbar/tray behavior, Explorer layering, mixed-DPI monitor movement, keyboard focus across native popups, or GPU/compositor transparency. Those remain separate manual Windows acceptance checks.

Structural-theme coverage includes Classic/Halo/Meridian with all five palettes; custom RGB validation and contrast; independent icon/color/structure persistence; invalid-draft preview; failed-save rollback; and narrow dock renders. All three icons are tested; review images use Aperture. Collapse regression cases use production placement helpers with top/bottom anchors, one/two tabs, initial collapsed/expanded state, forced size normalization, restoration, and fixture-store persistence. Height-clamp cases separately assert that an already-valid bottom stays fixed, while an out-of-work-area bottom moves only to the allowed boundary. They do not synthesize native drag events. `structural-geometry.json`, `structural-custom-contrast.json`, and `collapsed-placement-regression.json` contain the measured evidence.

Header-opacity coverage checks live brush updates, per-dock overrides, nonfinite inputs, opaque foregrounds/shared menu resources and open/rolled-up geometry. Pixel-alpha assertions detect opaque layers behind a supposedly translucent header. Images ending in `synthetic-backdrop` render the same actual control over a fixture gradient for visual comparison; they are not desktop screenshots.
