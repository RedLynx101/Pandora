# Product Brief

## Goal

Build OrbitDock, a native Windows desktop organizer that gives users flexible docks for files, folders, shortcuts, and project work without locking them into a proprietary desktop customization stack.

## Non-Goals

- Do not copy proprietary assets, screenshots, branding, product names, or UI layouts from commercial software.
- Do not hide, delete, or move desktop icons by default.
- Do not require administrator privileges for normal use.
- Do not require cloud services or accounts.

## Primary Users

- Students and researchers who keep active class/project files on the desktop.
- Developers who want per-project launch and document zones.
- Power users who want a visually clean desktop without losing fast access.
- Accessibility-focused users who need larger, color-coded, stable desktop groups.

## MVP

- Create docks on the desktop.
- Group existing desktop shortcuts into smart app categories.
- Mirror folders as portal zones.
- Move and resize zones.
- Customize zone name, colors, opacity, icon size, and collapsed state.
- Copy files into a zone by drag-and-drop.
- Open files/folders directly from the zone.
- Persist config locally.
- Provide tray controls and a peek hotkey.

## Differentiators

- Safe by default: copy, preview, and fallback before move, mutate, or hide.
- Open configuration: readable JSON in `%APPDATA%`.
- Testable core: rules and path behavior work without the WPF UI.
- Resilient portals: offline folders show status instead of blocking startup.
- Desktop integration is best effort, not a hard dependency.

## Naming

The repository and product identity are `OrbitDock`. Public copy should describe the product as "desktop docks", "launchpads", or "desktop organizer" wherever possible to avoid leaning on another product's marks.
