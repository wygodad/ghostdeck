# Rendering guide — how the UI is drawn (and why)

This document explains how GhostDeck draws its UI, with a focus on the two hand-painted,
DPI-sensitive surfaces — the **Status tab** and the **gaming overlay** — so future work has a
reference to copy from instead of re-discovering the pitfalls. It also covers how the other tabs
render and the rules that keep everything sharp and smooth at any display scaling (100 % … 150 %+).

- [1. For non-programmers (the gist)](#1-for-non-programmers-the-gist)
- [2. The core problem: sharp text at high DPI](#2-the-core-problem-sharp-text-at-high-dpi)
- [3. Gaming overlay — per-pixel layered window](#3-gaming-overlay--per-pixel-layered-window)
- [4. Status tab — DPI-aware buffered canvas](#4-status-tab--dpi-aware-buffered-canvas)
- [5. The other tabs](#5-the-other-tabs)
- [6. Rules of thumb (do / don't)](#6-rules-of-thumb-do--dont)

---

## 1. For non-programmers (the gist)

The app draws its screens *by hand* — like a painter filling a canvas — instead of using stock
Windows controls, so it can look modern and themed. That's great for looks, but it creates two
practical problems that we solved:

- **Sharpness on hi-res laptops.** On a 4K/17" panel Windows is usually set to 140 % zoom. If you
  paint text onto an "off-screen sheet" and then stretch that sheet to 140 %, the text goes blurry
  and doubled. We avoid the stretch entirely (see below).
- **Smooth scrolling of a busy page.** The Status page has rings, bars, tables and a byte matrix.
  Re-painting *all* of that on every tiny scroll step stutters. We paint it **once** onto an
  off-screen copy and then just *slide that copy* while scrolling — fast and smooth.

Two different tricks, one goal (**sharp + smooth**):

- The **overlay** paints itself onto a transparent "sticker" and hands the whole sticker to Windows
  to place on screen (this is how it can have soft rounded corners, see-through background, and
  crisp text over any game).
- The **Status tab** paints onto an off-screen copy that is made *at the same zoom level as the
  screen*, so sliding it around never blurs anything.

The rest of the tabs are built from ordinary building blocks (buttons, toggles, cards), which
Windows already scrolls smoothly — so they need none of this special handling.

---

## 2. The core problem: sharp text at high DPI

Both hand-painted surfaces render **off-screen first**, then blit to the screen. The trap is DPI:

- A plain `new Bitmap(w, h)` is **96 DPI**. If the screen is at 140 % (~134 DPI), drawing that
  bitmap scales it up by ~1.4× → **blurry, "bold/doubled", jagged** text and shapes.
- Windows has **two text APIs** that behave differently off-screen:
  - **GDI+ `Graphics.DrawString`** honours the *bitmap's* resolution. Give the bitmap the real DPI
    (`bitmap.SetResolution(dpi, dpi)`) and it renders at the correct size **and** stays crisp.
  - **GDI `TextRenderer.DrawText`** (ClearType) uses the *device context's* pixel density, **not**
    the bitmap resolution. On a memory DC created from a plain bitmap it renders at 96 DPI, so
    `SetResolution` alone does **not** fix it.

So there are two valid recipes, and the two surfaces each pick one:

| Surface | Off-screen target | Text API | DPI fix |
|--------|-------------------|----------|---------|
| **Overlay** | `Bitmap(32bppArgb)` | GDI+ `DrawString` | `bitmap.SetResolution(dpi)` |
| **Status** | `BufferedGraphics` from the control's own `Graphics` | GDI `TextRenderer` (unchanged) | the buffer's DC inherits the control's DPI |

Both end with a **1 : 1 device-pixel blit** (no scaling), which is what keeps them sharp.

---

## 3. Gaming overlay — per-pixel layered window

File: [`OverlayForm.cs`](../OverlayForm.cs) (`RenderLayered`, ~line 168).

The overlay is a borderless, always-on-top, **per-pixel alpha** window. It is **not** painted via the
normal `OnPaint`; instead it builds a 32-bit ARGB bitmap and hands it to Windows with
`UpdateLayeredWindow`. That gives true per-pixel transparency (soft rounded corners, independent
background vs. text opacity, natural click-through on empty pixels).

Pipeline per frame (on the 1 s timer, on settings change, and on move):

1. **Measure & size.** `AutoScaleMode = None`; layout is fully measurement-driven, scaled only by the
   user's size setting `U`. Real DPI is read once via `CreateGraphics().DpiY`.
2. **Content layer** — `Bitmap(w, h, Format32bppArgb)` + **`SetResolution(dpi, dpi)`**,
   `TextRenderingHint = AntiAlias` (grayscale AA → correct alpha edges), cleared to
   `Color.Transparent`. Header, metric cells and **vector icons** are drawn with GDI+
   (`DrawString`, `FillPath`, stroked paths) — no icon font, so icons scale cleanly.
3. **Compose final** — `Bitmap(w, h, Format32bppPArgb)` + `SetResolution(dpi)`:
   - background rounded rect at **`OverlayBgOpacity`** alpha (or forced faintly visible while
     unlocked so it stays grabbable);
   - the content drawn **twice** via `DrawLayer` — first as a black silhouette offset by 1 px at half
     alpha (a soft **drop-shadow** for readability on any game), then the real content at
     **`OverlayOpacity`** alpha. Background and content alpha are therefore **independent**;
   - accent frame + drag grip when unlocked, or a faint hairline frame when locked with a background.
4. **Push** — `PushLayered` selects the bitmap into a memory DC and calls `UpdateLayeredWindow` with
   `AC_SRC_ALPHA` (per-pixel), placing it at the window's screen position at **1 : 1** pixels.

Interaction: `WS_EX_LAYERED` is permanent; click-through toggles `WS_EX_TRANSPARENT`; dragging is
handled while unlocked and the position is saved. Card vs. bar layout and which metrics show are
driven by settings flags.

Why it looks great: correct-DPI bitmap + GDI+ text + 1 : 1 layered blit + a drop-shadow.

---

## 4. Status tab — DPI-aware buffered canvas

File: [`MainPages.cs`](../MainPages.cs) — `StatusPage` and its inner `Canvas`.

Status is a tall, heavy page (5 gauge rings, a RAM bar, RPM/battery/GPU/VRAM tiles, a details table,
the EC **byte matrix**, a legend, live fan-curve tables, a recent-changes log). It scrolls, so we
cannot afford to repaint all of it on every scroll step.

Structure:

- A child **`Canvas`** control is sized to the **full content height**; the page (`ThemedPage`) has
  `AutoScroll`, so Windows scrolls that child natively (no manual scroll translate → no ghosting).
- The Canvas keeps a persistent **`BufferedGraphics`** — crucially **allocated from the control's own
  `Graphics`** (`BufferedGraphicsManager.Current.Allocate(CreateGraphics(), rect)`). That backing DC
  is *compatible with the control's DPI-aware DC*, so `TextRenderer.DrawText` draws off-screen exactly
  as it does on-screen (right size, crisp) — **without rewriting any drawing code**.
- `Render(g, width)` paints everything into the buffer. `Rebuild()` re-renders the buffer, and is
  called only when something actually changes: entering the tab, a resize, a theme change, the
  1.5 s live-values timer, or a change-log update.
- `OnPaint` just does `buffer.Render(e.Graphics)` — a **BitBlt** of the buffer. During a scroll only
  the newly exposed strip is blitted, so scrolling is smooth. `OnPaintBackground` is suppressed
  (the buffer covers everything) and `OptimizedDoubleBuffer` is **off** (we do our own buffering).

Why the earlier attempt failed: it rendered into a plain 96-DPI `Bitmap` and blitted with
`DrawImageUnscaled`, which rescaled to 134 DPI → the blurry/doubled text the buffered approach fixes.

**Sub-tabs on the canvas.** Status is split into three sub-pages — **Charts**, **EC bytes** and
**Change log** — via a `SubTabs` child control placed on the canvas (like the "Full log…" button). The
canvas is sized to the **active section only** (`SectionHeight(width, sub)`), and `Render` branches to
`RenderBytes` / `RenderLog` (charts is the default); content starts at a fixed `SecTop` below the title
and the sub-tab bar. This keeps each view short (so scrolling is minimal) while reusing the same
buffered-canvas machinery — only the active section is ever rendered into the buffer.

Key point to reuse: **to cache a `TextRenderer`-based render off-screen, allocate the buffer from the
control's Graphics** (don't use a bare `Bitmap`). If you *must* use a bare `Bitmap`, switch the text to
GDI+ `DrawString` and call `SetResolution(dpi)` — the overlay's recipe.

---

## 5. The other tabs

Base class [`ThemedPage`](../MainForm.cs): a scrollable `UserControl` (`AutoScroll`, double-buffered,
`BackColor = Theme.Surface`). Helpers: `ApplyScroll(g)` translates painting by the scroll offset;
`OnScroll`/`OnMouseWheel` force a full repaint; and `ScrollToControl` is overridden to return the
current position so focusing a child does **not** yank the page to the top.

- **Settings** ([`SettingsPage`](../MainPages.cs)) — **child-controls only**: the title is a `Label`,
  the groups are `CardSection` controls, the gaming-overlay block is a `Panel`. There is **no custom
  `OnPaint`**, so Windows scrolls it natively and smoothly. (It previously hand-painted the title while
  the cards were child controls; the two scrolled by different mechanisms and diverged — flicker + a
  phantom gap. The fix was to make the title a child too.)
- **Scenarios** ([`ScenariosPage`](../MainPages.cs)) — a hybrid: the header/labels and the settings
  card are hand-painted in `OnPaint (ApplyScroll)`, while the profile **tiles** and the feature
  **bricks** (Fan Boost, overlay) are child `Control`s. Fine because it barely scrolls.
- **Fan curve** ([`FanCurvePage.cs`](../FanCurvePage.cs)) — a custom-painted editable curve (drag
  points) plus a few child controls; short, no special caching needed.
- **Models** ([`ModelsPage.cs`](../ModelsPage.cs)) / **Report** ([`ReportPage.cs`](../ReportPage.cs))
  / **Updates** — mostly standard child controls (lists, buttons, labels) with light custom painting;
  they scroll natively.
- **Chrome** ([`MainForm`](../MainForm.cs)) — the tab strip, theme button and the announcement
  **banner** are custom-drawn controls; the banner is a top-docked `Panel` shown on demand.
- **Overlay OSD** ([`OsdForm.cs`](../OsdForm.cs)) — the small "MSI · PROFILE" toast on profile change
  is a separate rounded, fading, non-activating window (simpler than the gaming overlay).

Shared primitives live in the `Ui`, `Theme` and `IconPainter` helpers (rounded rects, pills, cards,
gauge rings, scenario icons), so the look stays consistent across tabs.

- **Sub-tabs** ([`SubTabs.cs`](../SubTabs.cs)) — a reusable themed segmented control (a child `Control`
  raising `Changed(int)`) that splits a page into a few sub-pages without adding top-level tabs. Used on
  **Status** (Charts / EC bytes / Change log) and **Report** (Profiles / Fan curve). It paints its own
  rounded container + segments; hosts just position it and re-lay-out on `Changed`.
- **Icon glyph buttons** (the theme toggle, plus the Report `⚑` and Updates `⟳` buttons that replaced
  those top-level tabs) use `TextRenderer` with `NoPadding` and per-glyph `GlyphDx/GlyphDy` nudges —
  `TextRenderer` centres the glyph *cell*, not its ink, and symbol glyphs have uneven side bearings, so
  each icon needs a small optical tweak to line up.
- **Dropdowns** ([`ThemedComboBox`](../MainPages.cs)) — the stock `ComboBox` keeps a white field, drop
  button and list in dark mode, and its themed button *flashes light on hover/press* before any overpaint
  can cover it. `ThemedComboBox` fixes this by **owning the paint**: `DrawMode = OwnerDrawFixed` draws the
  list rows (`OnDrawItem`, accent for the selected row), and `WndProc` intercepts `WM_PAINT` to paint the
  **closed field itself** via its own `BeginPaint`/`EndPaint` and **never calls `base`** for that message —
  so there is no light frame at all (an overpaint *after* `base` still flashes; that was the failed first
  attempt). It also calls `SetWindowTheme(handle, "", "")` so the drop-down list's scrollbar/border follow
  the flat dark look. Colours are read from `Theme` at paint time, so a light/dark switch only needs
  `Invalidate` (done in `CardSection.ApplyTheme` / `OverlaySettingsPanel.ApplyThemeColors`). **Use
  `ThemedComboBox`, never a bare `ComboBox`, for any new select** (language, AC/battery profile, overlay
  position all use it).

---

## 6. Rules of thumb (do / don't)

- **Never blit a 96-DPI bitmap onto a high-DPI surface.** Either `SetResolution(dpi)` + GDI+
  `DrawString`, or a `BufferedGraphics` allocated from the control's own `Graphics`.
- **Match the text API to the target.** GDI+ `DrawString` respects bitmap DPI; GDI `TextRenderer`
  respects the DC's DPI. Don't expect `SetResolution` to fix `TextRenderer` on a bare bitmap.
- **Cache heavy scrolling pages.** Render once into an off-screen buffer and BitBlt it while
  scrolling; re-render only on data/size/theme change — not per scroll frame.
- **Don't mix hand-painted (`OnPaint`+`ApplyScroll`) elements with child controls on a page that
  scrolls a lot.** Make it all child controls (smooth, native) or all painted. The Settings title
  bug came from mixing the two.
- **For overlays/transparency, prefer per-pixel `UpdateLayeredWindow`** over `TransparencyKey`
  (chroma-key), which fringes anti-aliased edges and can't do partial background alpha.
- **Layout from measured metrics, scaled by DPI** (or a user-scale factor), never from hard-coded
  pixel steps — so it stays correct at every scaling level.
- **For themed native inputs, own `WM_PAINT` — don't overpaint after `base`.** A stock control paints
  itself (light) first, so painting over it afterwards leaves a visible flash on hover/press. Intercept
  `WM_PAINT`, `BeginPaint`/`EndPaint` yourself and skip `base` (see `ThemedComboBox`). New selects must use
  `ThemedComboBox`, not a bare `ComboBox`.
