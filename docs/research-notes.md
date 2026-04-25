# Research Notes

Research date: 2026-04-25

## Sources Crawled

- Stardock Fences 6 product page: https://www.stardock.com/products/fences/
- Stardock Fences changelog: https://www.stardock.com/products/fences/history.cshtml
- Stardock archived Fences feature page: https://archive.stardock.com/products/fencesupgrade/features.asp
- Stardock tabs article: https://www.stardock.com/blog/535507/getting-started-with-tabs-in-fences-6
- Stardock Fences 6 beta announcement: https://www.stardock.com/news/535050/now-announcing-fences-6-beta-available-now
- Existing OSS references:
  - https://github.com/limbo666/DesktopFences
  - https://github.com/Xstoudi/Palisades

## Product Category Observations

The core user problem is desktop sprawl: files, shortcuts, folders, and project artifacts spread across a flat Windows desktop with little structure. The winning interaction is direct manipulation: desktop zones should feel like native objects that can be moved, resized, colored, collapsed, and used without opening a separate organizer app.

Feature families observed:

- Shaded desktop groups for icons, files, and folders.
- Folder portals that mirror real folders on the desktop.
- Automation rules based on file type, name, time, and target location.
- Customization for color, transparency, tab appearance, icon size/tint, and sorting.
- Peek behavior that brings desktop groups above open windows.
- Roll-up behavior to hide group contents while keeping a compact header.
- Multi-tab grouping.
- Quick hide / distraction-free workflows.
- Enterprise deployment and templated configuration.
- Multi-monitor, DPI, and monitor sleep resilience.
- Accessibility-oriented larger icons, colors, and stable positioning.

## Changelog-Derived Risk Notes

The changelog suggests a mature desktop organizer spends significant engineering effort on edge cases rather than only on first-run visuals:

- Multi-monitor transitions can move or shuffle groups if monitor identity is not tracked carefully.
- Network folder portals can slow startup or hang when offline.
- Peek can conflict with other Windows/taskbar behavior.
- DPI support above 200 percent needs explicit testing.
- Folder portal navigation state needs to avoid corrupting original configured paths.
- Scrollbars and roll-up state interact in subtle ways.
- Desktop folder relocation and removable drives are realistic user states.

## OSS Landscape Notes

Desktop Fences+ and Palisades validate WPF/.NET as a pragmatic implementation path for an open-source Windows desktop organizer. The useful gap for this repo is a safer, more modular version: core config/rules separated from WPF, destructive behavior disabled by default, and documented fallbacks for shell attachment.

## Positioning for CustomFences

CustomFences should not be a visual clone. The strongest open-source angle is:

- Transparent safety posture.
- Human-readable workspace config.
- Portal-first organization before icon mutation.
- Scriptable rules with dry-run/preview before action.
- Modern but calm visuals.
- Explicit compatibility fallbacks.
