# Visual Direction

Pandora should feel like quiet celestial glass pinned to the desktop: translucent surfaces, legible text, restrained color, and no ornamental clutter. The Greek inspiration informs the name and simple icon geometry; it should not make controls harder to understand.

## Structure first, palette second

Themes change the dock's structure, not just its colors. The same palette and custom-color system works across all three.

| Dock theme | Structural identity |
| --- | --- |
| Classic | Familiar integrated frame, compact header, and existing layout proportions |
| Halo | Floating rounded header and body, pill controls, and an airy tile grid |
| Meridian | Crisp frame, accent rail, distinct tab strip, and compact horizontal file tiles |

Classic is the default and continuity option. Halo emphasizes softer separation and space; Meridian favors alignment and information density. They keep the same underlying dock data, actions, and safety boundaries.

| Palette | Surface and use |
| --- | --- |
| Lunar | Default midnight glass with silver text and a restrained lavender accent; stored as `LunarGlass` |
| Midnight | Deeper dark surfaces with stronger separation for busy desktops |
| Limestone | Warm light surfaces with dark text and a muted violet accent |
| Aegean | Deep blue-green surfaces and a sea-glass accent |
| System | Follows the Windows app theme using Limestone or Lunar |

Optional accent and surface pickers accept `#RRGGBB` values. Derive related text, focus, border, selection, and status roles so color customization remains readable. Keep deliberate per-dock overrides; changing structure or palette must not overwrite them. The legacy `theme` setting remains the palette key, while `dockTheme` chooses structure.

Keep content more opaque than the surrounding glass. Adjust background opacity, never the entire text/control layer. Windows high contrast overrides decorative colors and transparency. Honor reduced motion.

## Information hierarchy

- Docks remain the primary surface, with compact headers and stable positions.
- Projects show the current phase, verified progress, owner, attention, and freshness before detailed evidence.
- Expand details on demand; do not fill a glanceable project row with raw JSON or event logs.
- Settings organize appearance, workspace, projects, audio, and system behavior into clear destinations.
- Menus use consistent grouping and labels, placing destructive or recovery actions away from routine navigation.

## Interaction rules

- Roll-up is immediate and calm, preserving spatial memory. Keep saved expanded bounds separate from the visible closed header: dragging or normalizing a rolled-up dock must keep it closed, remember its size, and preserve bottom anchoring where selected.
- Hover and focus clarify interaction without lighting up the entire surface.
- Status describes observed facts: missing, invalid, unread, blocked, implemented, or verified.
- A source update is not a live-agent heartbeat. A declared budget is not active worker usage.
- Use named owners or an explicit unassigned state; never silently invent ownership.
- Never treat opening, reading, or acknowledging a project as approval.
- Keep the icon recognizable at tray size. Aperture is the selected default; Selene and Aster are icon alternatives, not separate product identities.

Metis retains its own restrained bronze identity. Pandora may display Metis content without duplicating the entire dashboard or changing the director's canonical plan.
