# Pandora appearance

Theme is structure; palette is color. `DockThemeCatalog` defines geometry independently of `ThemeService` color selection. `PandoraControls.xaml` supplies shared WPF templates and fallback Classic/Lunar resources. Consumers use `DynamicResource` or read the current immutable profile rather than duplicating design values. Windows theme, high-contrast, and animation preferences are observed without changing system settings.

## Structural designs

| Design | Frame and header | Controls and content |
| --- | --- | --- |
| Classic | Continuous 18px-radius frame, 44px base header | Familiar 6px controls, moderate item spacing |
| Halo | 24px-radius body, separate 60px header with an 8px gap | Pill controls, rounded items, generous padding |
| Meridian | Precise 4px-radius frame, compact header, 3px accent rail | Squared controls and items, denser architectural rhythm |

`DockThemeCatalog.Get(id)` returns `DockThemeProfile`; `.All` is read-only. Unknown and missing IDs safely resolve to Classic. The profile defines radius, header/content padding, footer height, item padding/gap, borders, header separation, accent rail, title scale, and shadow. The host still owns collapse/expand, placement, and tab behavior; changing structure must not rewrite saved expanded bounds.

`DockBarSizing.Get(profile, size)` derives Compact/Standard/Large bar height, title font, controls and brand dimensions independently of structure/palette. `EffectiveDockBarSize` exposes the normalized choice. All structures use one name row and a separate expanded-only tab strip; its auto height and overflow minimum keep larger labels clear of the horizontal scrollbar. These resources affect dock bars, not Settings/menu typography or file icons. Header/footer backgrounds use `GetDockChrome` to share body opacity without mutating opaque UI brushes.

## Integration

- Call `ThemeService.Initialize(workspace.Settings)` after the workspace loads. Call `Apply(settings)` after a saved-settings reload. This reads the full structural/palette/custom-color configuration.
- `EffectiveDockTheme` is the structural ID. `EffectivePalette` is the resolved palette ID; `EffectiveTheme` remains a compatibility alias for that palette. `System` resolves to Lunar or Limestone using Windows app appearance.
- `ThemeChanged` notifies code-rendered consumers. Subscribe for the window lifetime and unsubscribe when closed.
- `ReduceMotion` combines the saved opt-out with Windows animation and high-contrast preferences. Do not animate when true.
- Color resources: `Pandora.WindowBrush`, `SurfaceBrush`, `ElevatedBrush`, `BorderBrush`, `TextBrush`, `MutedBrush`, `AccentBrush`, `AccentTextBrush`, `FocusBrush`, `SelectionBrush`, `SelectionTextBrush`, `HoverBrush`, `DangerBrush`, `SuccessBrush`, and `GlassBrush` (each with the `Pandora.` prefix). Glass opacity applies to a background brush, never a whole window containing text.
- Structural resources use the same prefix: `DockCornerRadius`, `HeaderHeight`, `HeaderPadding`, `ContentPadding`, `FooterHeight`, `ControlCornerRadius`, `ItemCornerRadius`, `ItemPadding`, `ItemGap`, `ItemMargin`, `FrameBorderThickness`, `HeaderGap`, `AccentRailWidth`, `SeparatedHeader`, `TitleFontSize`, `ShadowBlur`, and `ShadowOpacity`. Radius resources are `CornerRadius`; padding/margin/border resources are `Thickness`. Other dimensions are doubles; `SeparatedHeader` is a bool.
- Shared controls additionally consume `ControlPadding`, `MenuPadding`, and `ExpanderHeaderPadding`. Project containers can use the `Pandora.ProjectCard` border style or bind `ProjectCardCornerRadius`, `ProjectCardPadding`, and `ProjectCardMargin`. These tokens change control geometry, not only decoration.
- `GetDockBackground`, `GetDockAccent`, and `GetDockText` preserve deliberate custom dock colors and maintain readable text. Known legacy factory colors follow the new palette without rewriting stored colors. Known factory opacities follow the global slider; other saved opacity values remain per-dock overrides. An old override numerically identical to a factory value cannot be distinguished from that default.

## Persistence and preview

The existing JSON `Theme` field remains the palette ID for compatibility: `LunarGlass` (display name Lunar), `Midnight`, `Limestone`, `Aegean`, or `System`. `Graphite` and unknown palette IDs resolve to Lunar. `DockTheme` is the separate structural ID. `CustomAccentColor` and `CustomSurfaceColor` are optional. High contrast overrides every palette/custom color with system colors, opaque backgrounds, and no shadow; it does not erase saved choices.

`TryNormalizeCustomColor(value, out normalized)` accepts a trimmed opaque `#RRGGBB`, normalizes uppercase, and accepts blank as null/default. It never accepts color names, alpha, short hex, or malformed values. Preview UIs should reject invalid drafts; the service independently falls back to the selected palette for invalid persisted values. A custom surface derives coherent opaque layers and text, muted, border, hover, selection, and status colors. Custom accent fill is preserved exactly, while its text and keyboard focus colors are contrast-corrected. Deliberate per-dock overrides remain higher priority than global custom colors.

Full preview uses `Apply(palette, opacity, reduceMotion, dockTheme, accent, surface)` without changing the workspace. The legacy three-argument overload deliberately resets structure to Classic and custom colors to null, retaining its old standalone behavior. Apply writes only through `DesktopZoneManager.SaveAppearanceSettings()` and then notifies consumers. Revert or closing restores the saved appearance. Icon previews are local to Settings until Apply. Saving a dock or audio preferences never changes the Windows startup registration.

All popup controls use instant transitions. Keyboard focus visuals, access-key presenters, named inputs, clear disabled state, and scrollable categories are maintained in the shared templates. Text blocks inherit their containing control's font and foreground; do not add global setters that override accent-button labels or symbol glyphs. Test actual WPF rendering through `tests/Pandora.App.Tests`; avoid launching the live desktop manager for fixture verification.
