# DESIGN.md — WinLaunch Enhanced

Direction for the WinLaunch Enhanced fork. This file is the visual source of truth;
`antislop.md` (core) plus `antislop-ui` and `antislop-human` filter on top of it.

## Design read

Reading this as: a fullscreen launcher overlay for a daily-driver power user,
in a dark frosted-glass desktop-utility language. Dials: **ENERGY 2 / RHYTHM 2 / MOTION 2**.

- ENERGY 2: the overlay says hello firmly (wallpaper dims, tiles fade up once) then gets out of the way.
- RHYTHM 2: one consistent tile grid, broken by the folder sheet and the search state.
- MOTION 2: open/close, page slide, folder expand, wiggle edit mode. Nothing loops forever.

## Palette (measured, not eyeballed)

| Token | Value | Use | Contrast evidence |
|---|---|---|---|
| `--bg-scrim` | `#0B0F14` | overlay scrim over dimmed wallpaper | base |
| `--tile` | `#1A222C` | app/folder tiles | — |
| `--text` | `#FFFFFF` | tile labels, headings | 19.22:1 on scrim, 16.04:1 on tile (PASS) |
| `--text-muted` | `#9AA7B4` | counts, hints, search placeholder | 7.83:1 on scrim (PASS) |
| `--accent` | `#14B8A6` | one deliberate accent: selection ring, search focus, active page dot | 7.72:1 on scrim (PASS normal text) |
| `--accent-ink` | `#0B0F14` | text on accent fills | 7.72:1 (PASS) |
| `--warn` | `#EA580C` dark / `#C2410C` light | delete/remove confirm fill only, never body text | black on dark-fill 5.90:1, white on light-fill 5.18:1 (both PASS) |

Accent appears in at most three places per screen (ring, focus, dot). Tiles stay matte;
no page-wide gradient, no glass-everywhere (glass is the scrim only).

Light theme mirrors the set (paper `#F4F6F8`, ink `#10161C`, same teal darkened to `#0B7C6F`
for 4.5:1+ on paper). Both themes ship, both get rendered in the preview step.

## Typography

Segoe UI Variable (system font). Reason: a native Windows utility should speak the OS voice,
add zero download payload, and render crisply at small tile-label sizes. Monospace nowhere
except the version string in Settings. No uppercase wide-tracked section labels.

## Tiles and iconography

- Tile: 88px icon, 12px label, 8px radius. Radius is a hierarchy tool: tiles 8px,
  folder sheet 12px, buttons 6px, search field 8px. Nothing pill-shaped except the page dots.
- Icons are the app's real icons (extracted, cached). Missing icon: initial-letter tile
  in `--tile` with `--text-muted` letter, clearly a stand-in, never a fake logo.
- Folders show up to 4 miniatures; more than 4 shows "＋n" as muted text, not invented art.

## Motion spec

- Overlay open: fade 120ms ease-out + 4px rise. Close: fade 100ms. Purpose: confirm the hotkey landed.
- Page change: 160ms horizontal slide. Purpose: spatial continuity across pages.
- Folder open: sheet expands 180ms ease-out. Purpose: show containment.
- Edit (wiggle) mode: ±1.2deg rotation on tiles, user-triggered only, stops on Esc/done.
  Purpose: signal rearrangement is armed. No endless pulses anywhere.

## Resilience (R-27)

Every data view ships three states: loading (skeleton tiles + "Loading icons…" text),
empty ("Nothing pinned yet. Drag apps here or press Add."), error (what failed, the file
involved, Retry and Open backup-folder actions). Search with no hits is its own empty state
naming the query.

## Keyboard (R-32)

Tab order follows the visual grid. Arrows move, Enter launches/opens, F2 renames,
Delete removes (with confirm), Ctrl+N new folder, Esc closes sheet then overlay.
Focus ring is 2px `--accent`, 7.72:1 against the scrim. Dialogs close on Esc.

## Performance budget

Cold open under 600 ms to interactive tiles on the reference machine; 0% CPU at idle
(no polling loops, event-driven hotkey); working set under 120 MB; icons decoded once,
frozen, and cached on disk; saves debounced 1 s with a pre-save backup, pruned to 10.
