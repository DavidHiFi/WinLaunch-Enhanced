# DESIGN.md — WinLaunch Enhanced

> 2026-10-12 direction change: after user review the board returns to the
> original WinLaunch/Launchpad look. Tiles are transparent floating icons
> (70 px x IconSize, 256 px shell images), labels are white with a dark
> shadow, folders are white-glass squares with a 3x3 miniature grid, the
> default background is the blurred desktop wallpaper with a 34% theme tint,
> an open folder is a translucent band over a blurred, dimmed board, and edit
> mode wiggles tiles (about 1.5 degrees, random phase) with remove badges.
> House themes still drive every color; the sections below about slabs and
> solid cards apply to dialogs and settings only.

Direction for the WinLaunch Enhanced fork. This file is the visual source of truth;
`antislop.md` (core) plus `antislop-ui` and `antislop-human` filter on top of it.

## Design read

Reading this as: a fullscreen launcher overlay for a daily-driver power user,
in a dark frosted-glass desktop-utility language. Dials: **ENERGY 2 / RHYTHM 2 / MOTION 2**.

- ENERGY 2: the overlay says hello firmly (wallpaper dims, tiles fade up once) then gets out of the way.
- RHYTHM 2: one consistent tile grid, broken by the folder sheet and the search state.
- MOTION 2: open/close, page slide, folder expand, wiggle edit mode. Nothing loops forever.

## Palette (measured, not eyeballed)

House theme roles shared with FreeDictate (`bg card field hover press text sub
accent on_accent accent_hover warn`), shipped for all seven themes so every
house app speaks one language. Default: **Catppuccin Mocha**.

| Token | Mocha value | Use | Contrast evidence |
|---|---|---|---|
| `bg` | `#1E1E2E` | overlay scrim over blurred wallpaper, 0.75 alpha | base |
| `card` | `#181825` (Mantle) | menus, sheets, settings groups | text 12.14:1, sub 9.91:1 (PASS) |
| `field` | `#11111B` (Crust) | inputs, switch tracks, segment controls | text 12.97:1, sub 10.59:1 (PASS) |
| `hover` / `press` | `#313244` / `#45475A` (Surface0/1) | row and button states, card borders | text on hover 8.69:1 (PASS) |
| `text` | `#CDD6F4` | labels, headings | 12.14:1 on card, 11.34:1 on bg (PASS) |
| `sub` | `#BAC2DE` | counts, hints, placeholders | 9.91:1 on card, 9.26:1 on bg (PASS) |
| `accent` | `#89B4FA` | one deliberate accent: focus ring, active page dot, primary buttons | 7.79:1 on bg (PASS) |
| `on_accent` | `#1E1E2E` | text on accent fills | 7.79:1 (PASS) |
| `warn` | `#F38BA8` | delete confirm text | 7.08:1 on bg (PASS) |

All seven themes check at 4.70:1 or better across every text-bearing pair, with
two documented exceptions: Latte accent `#1E66F5` on bg is 4.34:1, so in Latte
the accent is restricted to non-text roles (ring, dots; 3:1 bar clears easily),
and Nord warn is the yellow `#EBCB8B` because its red fails body text. Tile and
card edges use `CardBorder` (one step above card), never glow. Full pair list
is in the session notes.

## Backdrop and layers

Backdrop choices: Wallpaper (default: the desktop wallpaper blurred once and
cached, with adjustable blur and dim), Blur, Acrylic and Solid. Blur and Acrylic
use the Windows blur, whose strength apps cannot set, so only Wallpaper offers
a blur slider. They use `DWMWA_SYSTEMBACKDROP_TYPE` with the frame extended over the whole
window and a transparent WPF clear color (without both, the opaque WPF surface
hides the blur), legacy acrylic accent as fallback, immersive dark
mode follows the theme, accent re-applied on every show with a forced
recomposite. One translucent layer only: the window scrim carries the tint and
every inner surface (tiles, sheet, dialogs) is solid card. A translucent sheet
over a translucent scrim reads as a hard rectangle, so the folder sheet is
solid with a 1px border instead.

## Radius scale

Tiles 14, folder sheet 18, fields and buttons 10, dropdown items 6. Nothing
pill-shaped except the page dots. The scale is a hierarchy tool: bigger surface,
bigger radius.

Light theme mirrors the set (paper `#F4F6F8`, ink `#10161C`, same teal darkened to `#0B7C6F`
for 4.5:1+ on paper). Both themes ship, both get rendered in the preview step.

## Typography

Default: FiraCode Nerd Font everywhere (board, menus, settings), with Segoe UI
Variable Text and Segoe UI as fallbacks when the font is missing. Settings >
Appearance offers every installed font (system and per-user), a text-size
scale, and a label shadow toggle; all three apply live through the `UiFont`,
`TextScale` and `LabelEffect` resources. No uppercase wide-tracked section labels.

## Tiles and iconography

- Tile: 88px icon, 12px label, 8px radius. Radius is a hierarchy tool: tiles 8px,
  folder sheet 12px, buttons 6px, search field 8px. Nothing pill-shaped except the page dots.
- Icons are the app's real icons (extracted, cached). Missing icon: initial-letter tile
  in `--tile` with `--text-muted` letter, clearly a stand-in, never a fake logo.
- Folders show up to 9 miniatures in a 3x3 grid, using the apps' real icons.

## Motion spec

- Overlay open: fade 140ms plus a Launchpad zoom from 1.06 to 1 over 220ms (cubic ease-out),
  switchable in Settings and skipped when Windows animations are off. Close: fade 110ms.
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
(event-driven hotkey; the only timer is the 120 ms hot-corner check, and it runs only
while hot corners are on); working set target 120 MB (measured 158 MB with 256 px
icons for 90 items, 2026-10-12); icons decoded once,
frozen, and cached on disk; saves debounced 1 s with a pre-save backup, pruned to 10.
