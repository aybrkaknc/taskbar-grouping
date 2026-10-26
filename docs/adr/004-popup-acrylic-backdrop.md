# ADR-004: Acrylic backdrop behind the popup

## Status

Accepted. Reverses the "fully transparent popup" choice made in v0.3 (commit `e81a1df`), which was never recorded as an ADR.

## Context

Since v0.3 the popup has been a layered window (`AllowsTransparency=True`) with nothing painted behind the tiles, so the icons and their labels sit directly on whatever is underneath. That was a deliberate look: only the icons, no panel.

In use it reads poorly. The tiles float over arbitrary application content, so the group is hard to pick out as one surface, and the labels' legibility depends on what happens to be behind them. A per-label halo (v0.4.7) helps the text but not the grouping.

Two facts shape any fix:

- **DWM never draws a system backdrop behind a layered window.** `DWMWA_SYSTEMBACKDROP_TYPE` is ignored for a window created with `WS_EX_LAYERED`, which is what `AllowsTransparency=True` produces. The v0.2 popup did call `TryApply(Acrylic)` on its layered window; the panel people saw was the 95 %-opaque brush, not acrylic.
- **WPF cannot blur what is behind its own window.** A brush or `BlurEffect` only affects the window's own content.

So a genuinely blurred background has to come from DWM, and DWM needs a non-layered window.

## Decision

On Windows 11 22H2 and later (build 22621+), the popup is a **non-layered** window: `AllowsTransparency=false`, a `WindowChrome` whose glass frame covers the whole client area, a transparent composition background, and `DWMWA_SYSTEMBACKDROP_TYPE = Acrylic`. The immersive dark-mode flag follows the popup's theme, which selects the dark or light acrylic tint, and the corners are rounded through `DWMWA_WINDOW_CORNER_PREFERENCE`.

The mode is chosen in the constructor, **before the HWND exists**, from `WindowBackdrop.IsSupported`. A layered window cannot be turned into a non-layered one afterwards.

Everywhere else the popup stays layered, and `ChromeRoot` paints the theme's translucent `PopupFallbackBackground` panel. The same brush is used if DWM rejects the backdrop at run time; the window is opaque in that case, so the panel keeps it from rendering black.

## Alternatives considered

**Keep the transparent popup.** Rejected: the readability complaint is the reason for this record.

**Translucent brush only, everywhere.** Simple and safe with the existing animation, but it is a tint, not glass. On the systems where acrylic is available it looks clearly worse than the native flyouts next to it.

**`SetWindowCompositionAttribute` with `ACCENT_ENABLE_ACRYLICBLURBEHIND`.** It blurs behind a layered window, so the window mode would not have to change. Rejected because it is undocumented, its behaviour has changed between Windows releases, and the documented DWM attribute does the same job on every version this project recommends.

## Consequences

- The popup has two window modes, and both have to keep working. The `AllowsTransparency=True`-specific notes in `PopupWindow.xaml` (the Win11 24H2 paint-skip around `Window.Opacity`) now apply only to the fallback mode.
- In backdrop mode the acrylic rectangle appears at full size immediately, while the open animation scales and fades the content inside it. Animating the window itself would need a layered window again.
- The whole popup rectangle is now hit-testable. Before, clicks between tiles fell through to whatever was behind the transparent window.
- When the user turns off Windows' transparency effects, DWM shows a solid tint instead of acrylic. No code is involved.
- The labels' halo stays. On acrylic it is barely needed, but on the fallback panel over older Windows it still earns its place.
