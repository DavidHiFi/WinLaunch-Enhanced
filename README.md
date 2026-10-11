# WinLaunch Enhanced

A ground-up rewrite of [WinLaunch](https://github.com/jensroth-git/WinLaunch)
(MIT, (c) 2022 MrC0rrupted), modernized for Windows 11: .NET 8 WPF instead of
.NET Framework 4.8, async cached icons, real acrylic, full keyboard operation,
and a light theme next to the classic dark glass.

Screenshots below are rendered by `tools/PreviewRender` from the actual UI code
and themes (sample entries, real extracted icons):

![WinLaunch Enhanced, dark theme](docs/preview-dark.png)
![WinLaunch Enhanced, light theme](docs/preview-light.png)

## Compatibility promise

Enhanced reads and writes the **exact same files** as WinLaunch 0.7.x, in the
same place (`%APPDATA%\WinLaunch`, or beside the exe for portable installs):

- `Items.xml`, `Settings.xml`, `LinkCache\`, `ICBackup\` are shared. Both apps
  can open the same dataset; your pinned apps and folders carry over untouched.
- Your `theme.xml` backdrop preference (blur on/off, transparency) is honored.
- Every save first backs up the previous `Items.xml` into `ICBackup\` (10 kept).
- Opening Enhanced never reorders your layout: stored positions are preserved
  byte-for-byte until you move something yourself.

## What works in 1.0.0

Fullscreen overlay with acrylic blur (solid scrim fallback), paged tile grid,
folders with miniature composites, instant search across names, keywords and
folder contents, global hotkey (your existing combo is picked up automatically),
tray icon, add by file picker or drag-and-drop, rename, delete with confirm,
drag reorder and drop-into-folder in Edit mode, move-to-page menu, settings
window (grid size, hotkey, window placement, sorting, theme), dark and light
themes, empty/loading/error states everywhere, full keyboard operation
(arrows, Enter, F2, Delete, Ctrl+N, Ctrl+E, Esc with visible focus ring).

## What is intentionally not ported yet

Block-on-fullscreen, hot corners, double-tap and middle-mouse activation,
gamepad and voice activation, desktop-link watcher, custom background images,
the update checker, languages other than English, autostart setup, and the
wiggle animation in Edit mode (rearranging itself works). See DESIGN.md for
the visual direction; contributions welcome.

## Build and run

```powershell
dotnet build src/WinLaunch.Enhanced/WinLaunch.Enhanced.csproj
dotnet run --project src/WinLaunch.Enhanced
```

Use your live data by default. Point at any dataset copy with the first argument:

```powershell
dotnet run --project src/WinLaunch.Enhanced -- "D:\some\WinLaunch-copy"
```

## Verify before you switch

```powershell
# Dataset check against your real files: counts every tile, resolves every
# link, round-trips the XML. Exit 0 here was 20 top-level tiles
# (10 folders, 10 apps), 70 nested, 0 broken, round-trip OK.
dotnet run --project tools/VerifyData

# Headless startup check: builds the real window offscreen, settles icons,
# prints counts. No window shown, nothing registered, nothing saved.
dotnet run --project src/WinLaunch.Enhanced -- "<dataset-copy>" --smoke

# Re-render the screenshots above after UI changes.
dotnet run --project tools/PreviewRender
```

Measured on the reference machine: the verifier counts all 90 tiles with 0 broken
links and a clean XML round-trip; smoke builds the real window offscreen over the
full dataset with no load error; contrast pairs in both themes check at 4.70:1
or better (most above 7:1, see DESIGN.md).

## Switching from original WinLaunch

1. Back up `%APPDATA%\WinLaunch` somewhere safe (Belt and suspenders; Enhanced
   also backs up on every save).
2. Quit original WinLaunch (it holds your global hotkey).
3. Start Enhanced. It opens the same pins. Press your hotkey to toggle.
4. Rollback is quitting Enhanced and starting the original. Nothing to migrate.

## Layout

- `src/WinLaunch.Legacy` — byte-compatible `Items.xml`/`Settings.xml`/`theme.xml`
  models, store paths, link resolution, pre-save backups.
- `src/WinLaunch.Enhanced` — the app (views, view-models, services, themes).
- `tools/VerifyData` — dataset verifier. `tools/PreviewRender` — screenshot renderer.
- `DESIGN.md` — visual direction, palette with measured contrast, motion spec.
- `upstream/` (local reference only, not committed) — pristine upstream clone used
  to mirror the file formats.

## License

MIT. Original copyright (c) 2022 MrC0rrupted retained; see LICENSE.
Upstream: https://github.com/jensroth-git/WinLaunch
