# WinLaunch Enhanced

A ground-up rewrite of [WinLaunch](https://github.com/jensroth-git/WinLaunch)
(MIT, (c) 2022 MrC0rrupted), modernized for Windows 11. It keeps the original
Launchpad look: your wallpaper blurred behind floating icons, glass folders
with a 3x3 preview of their apps, a search pill at the top and page dots at
the bottom. Underneath it is .NET 10 WPF instead of .NET Framework 4.8, with
256 px shell icons, async icon caching and seven house themes
(Catppuccin Mocha default). Labels default to FiraCode Nerd Font; any installed
font works, set it in Appearance along with text scale and label shadow.

## Install

Grab `WinLaunchEnhanced-2.0.1-Setup.exe` from the
[latest release](https://github.com/DavidHiFi/WinLaunch-Enhanced/releases/latest).
The installer sets up Start-menu and desktop shortcuts, adds run-at-sign-in,
checks the .NET 10 Desktop Runtime for you, and offers to launch the launcher
right away. Uninstall any time from Windows Settings; your WinLaunch data is
never touched.

Screenshots below are rendered by `tools/PreviewRender` from the actual UI code
(sample entries, real extracted icons, a generated stand-in wallpaper):

![WinLaunch Enhanced, dark theme](docs/preview-dark.png)
![An open folder](docs/preview-dark-folder.png)
![WinLaunch Enhanced, light theme](docs/preview-light.png)

Settings are grouped cards in the active house theme, one tab per concern:

![General settings tab](docs/settings-dark-general.png)
![Appearance settings tab](docs/settings-dark-appearance.png)
![Layout settings tab](docs/settings-dark-layout.png)

## Compatibility promise

Enhanced reads and writes the **exact same files** as WinLaunch 0.7.x, in the
same place (`%APPDATA%\WinLaunch`, or beside the exe for portable installs):

- `Items.xml`, `Settings.xml`, `LinkCache\`, `ICBackup\` are shared. Both apps
  can open the same dataset; your pinned apps and folders carry over untouched.
- Your `theme.xml` backdrop preference (blur on/off, transparency) is honored.
- Every save first backs up the previous `Items.xml` into `ICBackup\` (10 kept).
- Opening Enhanced never reorders your layout: stored positions are preserved
  byte-for-byte until you move something yourself.

## What works

- Fullscreen overlay over your blurred wallpaper (the original look), or
  Acrylic, Mica or Solid backgrounds.
- Paged grid sized by your rows, columns and icon size. Change pages with the
  mouse wheel, a horizontal swipe, the page dots, PageUp/PageDown or
  Ctrl+Left/Right.
- Glass folders showing their first nine apps. A folder opens as a band over
  the blurred board; click its title to rename it.
- Search from the pill or by just typing. Enter opens the top result.
- Edit mode (hold a tile, Ctrl+E or the menu): tiles wiggle, a badge removes
  them, drag swaps tiles or drops an app into a folder. Removing shows an Undo
  button for a few seconds (Ctrl+Z works too). Esc or Done ends editing.
- Right-click a tile: open, run as administrator, open file location, rename,
  move to page, move out of folder, remove. Right-click empty space or the
  menu button for add apps, new folder, edit layout and settings.
- Global hotkey (your existing combo is picked up), tray icon, add by file
  picker or drag-and-drop, dead links shown dimmed.
- Search does quick arithmetic (type `12*4.5`) and falls back to your web
  search engine, both toggleable. Result ranking can follow your launch counts.
- Seven house themes: T3 Dark/Light, Catppuccin Mocha/Latte, Tokyo Night,
  Nord, Rose Pine. Animations follow the Windows "animation effects" setting.
- Stays out of fullscreen apps and games, and can open from a hot corner
  (top-left, top-right, bottom-left, bottom-right, any combination).

## What is not ported yet

Double-tap and middle-mouse activation, gamepad and voice activation,
desktop-link watcher, custom background images, the update checker and
languages other than English. Contributions welcome; see DESIGN.md for the
visual direction.

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
