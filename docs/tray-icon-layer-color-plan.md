# Tray icon layer-color indicator — plan

Short plan. Local-only (gitignored). Not started — picked up after the
cross-OS smoke pass on the HID-only refactor finishes.

## Motivation

The Moergo GO60 / Glove80 ship with per-key RGB *disabled by default*, so
users can't see at a glance which layer is active without looking at the
visualizer window. When the window is hidden (tray-only), there's
currently no visible cue. Tinting the tray icon to the active layer's
color closes that gap without forcing the window open.

## Approach

Keep the existing icon outline monochrome / template-style and overlay a
small colored badge (dot or bar) tinted to the active layer's color.
Reasons:

- **macOS** menu-bar icons are template images by convention — full-color
  retints look out of place and don't follow light/dark mode. A small
  colored dot on an otherwise monochrome base reads as "app + status."
- **Windows** taskbar tray and **Linux** StatusNotifier-based trays
  accept full color without complaint; the badge approach is fine on
  both.

Reuse `LayerColorPalette.GetColor(profileId, layerIndex)` as the color
source — already the single source of truth for layer colors elsewhere
in the UI.

## Steps

1. Carve a monochrome base icon out of `Assets/icon.png` (or add a new
   `Assets/icon_tray_base.png`). Keep dimensions matching the existing
   tray icon. Source SVG would be nicer if we have one — recoloring an
   SVG is cleaner than PNG composition.
2. Add a small renderer: `TrayIconRenderer.Render(baseImage, Color
   badge)` → `WindowIcon`. Use `RenderTargetBitmap` to composite the
   colored badge onto the base. Cache by (color, dpi) to avoid
   re-rendering on every layer change for the same color.
3. Wire it in `App.axaml.cs` where the tray icon is constructed:
   subscribe to `viewModel.PropertyChanged` for
   `nameof(MainWindowViewModel.ActiveLayerIndex)` and rebuild the icon.
   Also re-render on `SelectedKeyboard` change (layer indices and color
   palette reset).
4. Mirror the same swap for `MainWindow.Icon` so the Windows taskbar
   entry tracks too. Skip on macOS where the Dock icon is bundle-bound
   and not dynamically swappable through Avalonia.
5. Per-OS smoke:
   - macOS: confirm the badge reads cleanly against both light and dark
     menu bars. If the template-image autoinvert eats the badge, set
     `IsTemplate = false` (Avalonia may not expose this — fall back to a
     non-template image).
   - Windows: confirm the icon updates promptly on layer change and
     survives DPI scaling (16px and 32px variants).
   - Linux: confirm on at least one StatusNotifier DE (KDE/GNOME w/
     extension) and one legacy XEmbed tray. Behavior may differ; ok to
     ship best-effort.
6. Settings toggle ("Color tray icon by active layer", default on). Lets
   users who dislike the badge turn it off without losing the
   indicator-less original.

## Out of scope

- Animating the badge on layer change (just snap).
- Showing layer *name* in the icon (too small to read at tray sizes).
- macOS Dock icon recoloring (bundle-bound; not worth the complexity).

## Session log

- 2026-05-15 — plan drafted, not yet started. Waiting on cross-OS smoke
  of the HID-only refactor before picking this up.
