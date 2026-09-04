# Pandora appearance

`PandoraControls.xaml` provides shared WPF control templates and a fallback Lunar Glass palette. `ThemeService` replaces palette resources on the UI dispatcher; consumers use `DynamicResource`, not hard-coded colors. The service watches Windows theme, high-contrast, and animation preferences without writing system settings.

## Integration

- Call `ThemeService.Initialize(workspace.Settings)` after the workspace loads. Call `Apply(settings)` after a saved-settings reload.
- `ThemeChanged` notifies code-rendered consumers. Subscribe for the window lifetime and unsubscribe when closed.
- `ReduceMotion` combines the saved opt-out with Windows animation and high-contrast preferences. Do not animate when true.
- Resources: `Pandora.WindowBrush`, `SurfaceBrush`, `ElevatedBrush`, `BorderBrush`, `TextBrush`, `MutedBrush`, `AccentBrush`, `AccentTextBrush`, `SelectionBrush`, `SelectionTextBrush`, `HoverBrush`, `DangerBrush`, `SuccessBrush`, and `GlassBrush` (each with the `Pandora.` prefix). Glass opacity applies to a background brush, never a whole window containing text.
- `GetDockBackground`, `GetDockAccent`, and `GetDockText` preserve deliberate custom dock colors and maintain readable text. Known legacy factory colors follow the new palette without rewriting stored colors. Known factory opacities follow the global slider; other saved opacity values remain per-dock overrides. An old override numerically identical to a factory value cannot be distinguished from that default.

## Persistence and preview

Theme IDs are `LunarGlass`, `Midnight`, `Limestone`, and `System`. Legacy `Graphite` and unknown IDs safely resolve to Lunar Glass. `System` follows Windows app appearance, not wallpaper. High contrast temporarily overrides any selected theme with system colors and opaque backgrounds; the saved selection remains intact.

Settings preview uses `Apply(theme, opacity, reduceMotion)` without changing the workspace. Apply writes only through `DesktopZoneManager.SaveAppearanceSettings()` and then notifies consumers. Revert or closing restores the saved appearance. Icon previews are local to Settings until Apply. Saving a dock or audio preferences never changes the Windows startup registration.

All popup controls use instant transitions. Keyboard focus visuals, access-key presenters, named inputs, clear disabled state, and scrollable categories are maintained in the shared templates. Text blocks inherit their containing control's font and foreground; do not add global setters that override accent-button labels or symbol glyphs. Test actual WPF rendering through `tests/OrbitDock.App.Tests`; avoid launching the live desktop manager for fixture verification.
