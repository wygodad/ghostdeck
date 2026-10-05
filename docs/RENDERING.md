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

File: [`OverlayForm.cs`](../Forms/OverlayForm.cs) (`RenderLayered`, ~line 168).

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

File: [`StatusPage.cs`](../UI/StatusPage.cs) — `StatusPage` and its inner `Canvas`.

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

**The buffer is released when the page is hidden** (v1.28). It is sized to the whole scrollable
content, roughly 6 MB at 1600x980, and pages are never disposed, so leaving it allocated meant
carrying that for the rest of the session after one visit to Status. `VisibleChanged` calls
`Canvas.ReleaseBuffer()` alongside stopping the live timer; `OnPaint` re-allocates when the buffer
is null, so coming back costs exactly one render.

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

Base class [`ThemedPage`](../UI/MainForm.cs): a scrollable `UserControl` (`AutoScroll`, double-buffered,
`BackColor = Theme.Surface`). Helpers: `ApplyScroll(g)` translates painting by the scroll offset;
`OnScroll`/`OnMouseWheel` force a full repaint; and `ScrollToControl` is overridden to return the
current position so focusing a child does **not** yank the page to the top.

- **Settings** ([`SettingsPage`](../UI/SettingsPage.cs)) — **child-controls only**: the title is a `Label`,
  the groups are `CardSection` controls, the gaming-overlay block is a `Panel`. There is **no custom
  `OnPaint`**, so Windows scrolls it natively and smoothly. (It previously hand-painted the title while
  the cards were child controls; the two scrolled by different mechanisms and diverged — flicker + a
  phantom gap. The fix was to make the title a child too.)
- **Scenarios** ([`ScenariosPage`](../UI/ScenariosPage.cs)) — a hybrid: the header/labels and the settings
  card are hand-painted in `OnPaint (ApplyScroll)`, while the profile **tiles** and the feature
  **bricks** (Fan Boost, overlay) are child `Control`s. This hybrid is exactly the mix the rules
  below warn about, and it is what made the scroll-coordinate bug (§5.1) possible: painted parts
  and child controls do not consume the scroll offset the same way.
- **Fan curve** ([`FanCurvePage.cs`](../UI/FanCurvePage.cs)) — four sub-views over one curve, mostly
  hand-painted with a few child controls. `AutoScroll` is **off**: the In-action view scrolls itself
  by offsetting its geometry (§5.1a), paints its header last and places child controls through
  `Place()`. Full description in [FAN-CURVE.md](FAN-CURVE.md).
- **Models** ([`ModelsPage.cs`](../UI/ModelsPage.cs)) / **Updates** — the page itself does not
  scroll; an inner `AutoScroll` panel scrolls a child table, so WinForms moves the content.
- **Report** ([`ReportPage.cs`](../UI/ReportPage.cs)) — hand-painted content on an `AutoScroll` page:
  it calls `ApplyScroll` and therefore draws every label through `Ui.DrawText` (§5.1a).
- **Chrome** ([`MainForm`](../UI/MainForm.cs)) — the tab strip, theme button and the announcement
  **banner** are custom-drawn controls; the banner is a top-docked `Panel` shown on demand.
- **Overlay OSD** ([`OsdForm.cs`](../Forms/OsdForm.cs)) — the small "MSI · PROFILE" toast on profile change.
  Width is capped at 720 px (less on narrow screens); longer titles and messages wrap inside that
  width via GDI+ `DrawString` into a `RectangleF` and the toast grows downwards, so a full-sentence
  notification in a wordy language never spans the display. Single-line toasts keep the fixed
  440x104 geometry.
  is a separate rounded, fading, non-activating window (simpler than the gaming overlay).

Shared primitives live in the `Ui`, `Theme` and `IconPainter` helpers (rounded rects, pills, cards,
gauge rings, scenario icons), so the look stays consistent across tabs.

- **Sub-tabs** ([`SubTabs.cs`](../UI/SubTabs.cs)) — a reusable themed segmented control (a child `Control`
  raising `Changed(int)`) that splits a page into a few sub-pages without adding top-level tabs. Used on
  **Status** (Charts / EC bytes / Change log) and **Report** (Profiles / Fan curve). It paints its own
  rounded container + segments; hosts just position it and re-lay-out on `Changed`.
- **Icon glyph buttons** (the theme toggle, plus the Report `⚑` and Updates `⟳` buttons that replaced
  those top-level tabs) use `TextRenderer` with `NoPadding` and per-glyph `GlyphDx/GlyphDy` nudges —
  `TextRenderer` centres the glyph *cell*, not its ink, and symbol glyphs have uneven side bearings, so
  each icon needs a small optical tweak to line up.
- **Dropdowns** ([`ThemedComboBox`](../UI/Controls/ThemedComboBox.cs)) — drawn entirely by the app
  (v1.37): the closed field is a plain `Control` painted in `OnPaint`, the open list is a small borderless
  window of its own (`ListPopup`). It is **not** the system `COMBOBOX`, for two reasons. The stock control
  keeps a white field, drop button and list in dark mode and flashes light on hover/press. And it is
  expensive wherever other programs listen for window events: every list created and every item inserted
  raises an event that Windows hands to each listener (window managers, launchers, accessibility tools) —
  see §8.1 for the measured cost. The class mirrors the `ComboBox` members the app uses (`Items`,
  `SelectedIndex`, `SelectedItem`, `SelectedIndexChanged`, `DroppedDown`, `BeginUpdate`/`EndUpdate`) and
  keeps the system control's fixed height (`ItemHeight + 6`), so layouts did not change. Behaviour:
  - the list never takes activation or focus (`WS_EX_NOACTIVATE`, `ShowWithoutActivation`,
    `WM_MOUSEACTIVATE` → `MA_NOACTIVATE`); keys keep going to the field, which forwards them;
  - because the list cannot see a Deactivate of its own, an `IMessageFilter` closes it on any mouse press
    outside it (a press on the field itself only closes; anywhere else the press also reaches its
    target), and the owner window closes it on move, resize, hide and deactivation;
  - closed: arrows / Home / End / PgUp / PgDn change the value, a letter jumps to the next item starting
    with it, F4 or Alt+Down opens; open: the same keys move the highlight only, Enter picks, Esc cancels
    (both taken in `ProcessCmdKey`, ahead of the window's own shortcuts);
  - the mouse wheel never changes the value — closed, the event is left unhandled and reaches the page,
    which scrolls; open, it scrolls the list (discussion #9);
  - up to `MaxDropDownItems` (8) rows, then a thin scroll thumb that can be dragged; the list opens
    above the field when there is no room below;
  - colours are read from `Theme` at paint time, so a light/dark switch only needs `Invalidate`
    (`CardSection.ApplyTheme` / `OverlaySettingsPanel.ApplyThemeColors`);
  - a screen reader gets role *combo box* with the selected text as its value.

  **Use `ThemedComboBox`, never a bare `ComboBox`, for any new select.**

### 5.1 Scrolling: what carries the offset, and what silently does not (v1.28, corrected v1.34)

Three different things on a scrolling page consume the scroll offset in three different ways. Every
scroll bug this project has had came from assuming any two of them behave alike.

**(a) `TextRenderer` does NOT follow `ApplyScroll` — and cannot be clipped either.**

`TextRenderer.DrawText` hands its string to GDI through a raw HDC. By default it reads neither
`Graphics.TranslateTransform` nor `Graphics.Clip`. Measured, not deduced:

| what was drawn | result |
|---|---|
| `TranslateTransform(0,150)` + `TextRenderer.DrawText` at y=0 | text landed at y≈**6** |
| `TranslateTransform(0,150)` + GDI+ `DrawString` at y=0 | text landed at y≈**156** |
| `SetClip(0,150,300,150)` + `TextRenderer.DrawText` at y=10 | drawn anyway, **outside the clip** |
| the same two calls **with the flags below** | y≈156, and clipped away |

Since every label in this app goes through `TextRenderer`, a page that scrolls by `ApplyScroll`
moves its cards, curves and dots while every caption stays where it was, and no `SetClip` can stop
content from painting over the page header. There are exactly two correct ways out:

1. **Keep the transform and tell `TextRenderer` to honour it.** Draw text through `Ui.DrawText`,
   which adds `TextFormatFlags.PreserveGraphicsTranslateTransform | PreserveGraphicsClipping`.
   `Ui.Scrolled` is that flag pair. **Any page that calls `ApplyScroll` must use `Ui.DrawText` for
   every label** — `ReportPage` and `ScenariosPage` do. Adding the flags on an unscrolled `Graphics`
   is harmless: the transform is then the identity, and honouring the paint clip is correct anyway.
2. **Offset the geometry instead of the `Graphics`.** Bake the scroll into the rectangles, so
   painting, child controls and hit tests share one coordinate space. `FanCurvePage`'s In-action
   view does this (`PlayArea`), and it also paints its header **last**, over a repainted band,
   because with no usable clip the drawing order is what protects the header. See
   [FAN-CURVE.md](FAN-CURVE.md) §10.

Pages whose content is entirely child controls (`SettingsPage`, `ModelsPage`'s inner scroll host)
sidestep all of this: WinForms moves children itself. That remains the smoothest option.

**(b) Child positions on an `AutoScroll` page are CLIENT coordinates.**

- `OnPaint` + `ApplyScroll(g)` draws in **content** coordinates: the translate by
  `AutoScrollPosition` is applied for you.
- A child control's `Location` / `SetBounds` is in **client** coordinates: WinForms has ALREADY
  shifted the child by the scroll delta, so writing a content coordinate there shifts it a second
  time. Scrolled down 200 px, a card lands 200 px off and can overlap whatever is painted.

So a `Relayout()` that runs while the page is scrolled must add the offset back:

```csharp
int ox = AutoScrollPosition.X, oy = AutoScrollPosition.Y;   // both <= 0
...
child.Location = new Point(x + ox, y + oy);                 // content coord -> client coord
```

Two more traps in the same family:

- Read `AutoScrollPosition` ONCE at the top of the layout pass. It changes as controls move.
- Never derive a content coordinate from a child's own bounds afterwards
  (`_subTabs.Bottom` is already shifted); keep the content-coordinate cursor in a local.

`SettingsPage.Layout2`, `ScenariosPage.Relayout` and `ReportPage.Relayout` all follow this.

**(c) A child control is never clipped by the page's paint clip.**

Child controls own their own window, so they paint over the parent and no `SetClip` in the parent's
`OnPaint` touches them. A page that scrolls its own painting therefore has to HIDE the children
that scrolled out of view, or they float over whatever the header is. `FanCurvePage.Place()` does
exactly that, and it is also why a child cannot be over-painted by scrolled content: the sub-tab
strip stays legible for free.

One more, learned the same way: **never toggle a child's `Visible` from inside `OnPaint`**. Hiding
a child adds its rectangle back to the parent's update region, so the paint that hid it schedules
the next paint, which hides it again — a repaint loop that looks exactly like flicker. Write the
value once per frame from the layout pass, or write the same value every frame (WinForms turns an
unchanged `Visible`/`SetBounds` write into a no-op).

**(d) Resizing children does not re-evaluate the page's scrollbars (v1.34).**

Clicking **maximize and then restore** left a horizontal scrollbar on Status, Scenarios, Settings
and Report, and it vanished as soon as you switched tabs and came back.

The content was not too wide. Measured on a reproduction: at the moment the bar was showing, the
child overhung the client area by **0 px**. A `ScrollableControl` re-evaluates its scrollbars in
`AdjustFormScrollbars`, which runs as part of a **layout**, and simply assigning a child's
`Width`/`Height` from a resize handler does not schedule one. So the extent computed while the
window was maximized survived the restore, and the tab switch only helped because that path forces
a full layout.

The fix is one call, `PerformLayout()`, after the layout pass - wrapped in
**`ThemedPage.LayoutAndSyncScroll(pass)`**, which also drops the re-entrant `Resize` that the
layout itself raises. `StatusPage`, `ScenariosPage`, `ReportPage` and `SettingsPage` all go through
it. (`SettingsPage.SelectSub` had been calling `PerformLayout()` by hand for the same reason since
v1.28; what was missing was doing it on **resize** as well.)

What was measured, so nobody re-derives it from a plausible-sounding theory:

| after maximize → restore | horizontal scrollbar |
|---|---|
| plain layout pass | **shown** (child overhang: 0 px) |
| pass repeated until `ClientSize` settles | still shown |
| pass + `AutoScrollMinSize` reassigned | still shown |
| pass + `HorizontalScroll.Visible = false` | still shown |
| **pass + `PerformLayout()`** | **gone**, and stays gone across repeated cycles |
| pass + `AutoScroll` off/on | gone, but heavier and it resets the scroll position |

### 5.2 A sub-tab strip that does not fit shrinks, it does not scroll (v1.28)

`SubTabs.FitTo(available)` is given the width the host can spare. If the full strip is wider, it
drops to icons only, keeping the label on the ACTIVE segment and expanding the HOVERED one in
place. Without it the strip simply overflowed and WinForms put a horizontal scrollbar under the
whole page. Hovering changes the strip's width, so `SyncWidth()` re-clamps to the available width,
otherwise expanding could push the scrollbar back. All three hosts (Settings, Status, Report) size
their strip through `FitTo`; none uses `PreferredWidth` any more.

Tooltips were the first attempt and were dropped: a tooltip is a separate window with a delay that
covers the content, an inline label answers the same question immediately.

---

## 6. Rules of thumb (do / don't)

- **Never blit a 96-DPI bitmap onto a high-DPI surface.** Either `SetResolution(dpi)` + GDI+
  `DrawString`, or a `BufferedGraphics` allocated from the control's own `Graphics`.
- **Match the text API to the target.** GDI+ `DrawString` respects bitmap DPI; GDI `TextRenderer`
  respects the DC's DPI. Don't expect `SetResolution` to fix `TextRenderer` on a bare bitmap.
- **`TextRenderer` ignores the `Graphics` transform and clip** unless it is passed
  `PreserveGraphicsTranslateTransform | PreserveGraphicsClipping`. On any page that scrolls by
  `ApplyScroll`, draw labels through `Ui.DrawText` (§5.1a), or scroll by offsetting geometry.
- **Cache heavy scrolling pages.** Render once into an off-screen buffer and BitBlt it while
  scrolling; re-render only on data/size/theme change — not per scroll frame.
- **Don't mix hand-painted (`OnPaint`+`ApplyScroll`) elements with child controls on a page that
  scrolls a lot.** Make it all child controls (smooth, native) or all painted. The Settings title
  bug came from mixing the two.
- **For overlays/transparency, prefer per-pixel `UpdateLayeredWindow`** over `TransparencyKey`
  (chroma-key), which fringes anti-aliased edges and can't do partial background alpha.
- **Layout from measured metrics, scaled by DPI** (or a user-scale factor), never from hard-coded
  pixel steps — so it stays correct at every scaling level.
- **Draw inputs yourself instead of theming a system control.** A stock control paints itself (light)
  first, so painting over it afterwards leaves a visible flash on hover/press, and every system window
  has a creation cost that grows with what else runs on the desktop (§8.1). New selects must use
  `ThemedComboBox`, not a bare `ComboBox`; prefer one painted surface with hit zones over a group of
  child controls (`WinPowerBody`, the editor cards of §11).

## 7. Brand drawing (v1.18): ghost mark, gauges, log list

- **Ghost mark** — `TrayIconFactory.DrawGhost(g, x, y, size, body, eyes)` draws the brand ghost
  on a 32-unit design grid (dome arc, straight sides, three feet arcs, two rotated-ellipse eyes
  "punched" in the background colour). Used by the tray icon (`Create`, on the profile-coloured
  squircle) and the header wordmark (`MainForm.DrawWordmark`, "Ghost" in `Theme.Text` +
  "Deck" in `Theme.Accent`; hidden when the strip is too narrow). Windows/taskbar icons use the
  embedded multi-size `app.ico` via `TrayIconFactory.AppIcon()` — `ApplicationIcon` in the csproj
  only brands the exe file, it is NOT reachable at runtime, hence the `EmbeddedResource`.
- **Number icons in the tray** (v1.28) — `TrayIconFactory.TextIcon(text, colour)` draws the CPU/GPU
  temperature readouts. At 16x16 px every pixel counts, so: build the bitmap at
  `GetSystemMetrics(SM_CXSMICON)` (the size the shell asks for, DPI-aware) instead of building
  larger and letting the shell resample; add the digits to a `GraphicsPath`
  (`AddString` + `GenericTypographic`) and fit `GetBounds()` to the icon, because `DrawString`
  centres the font's line box and its ascender/descender/leading room costs about a third of the
  height; and draw the dark edge as ONE stroked `DrawPath` (`LineJoin.Round`, pen `S/12`) rather
  than 8 offset copies of the text. Measured ink height of "71": 10 → 13 px at 16 px, 14 → 19 px
  at 24 px.
- **Ring gauges** — `IconPainter.Ring` draws 40 arc tick segments clockwise from the top;
  lit ticks blend from the base colour toward white (`Mix`), unlit ticks use `Theme.Border`.
  Flat pen caps keep the ticks crisp; don't switch back to a single `DrawArc`.
- **Scenario icons** — vector GDI+ paths in `IconPainter.Scenario` (feather / scales / bolt /
  battery), stroked in the per-profile colour. SVG sources: `assets/icons/` (see its README;
  keep SVG and C# in sync).
- **Change-history list (`LogForm`)** — stock `ListView` grid is glaring white-on-black in dark
  mode, so the list is owner-drawn: `OwnerDraw = true`, empty `DrawItem`, cells painted in
  `DrawSubItem` (alternating `Theme.Card`/`Theme.Surface` rows, `Theme.AccentSoft` selection,
  muted first column), header in `DrawColumnHeader`. The control must be a double-buffered
  subclass or hover repaints flicker. Buttons use `Ui.StyleGhost`.

## 8. Window lifecycle & first-show performance (v1.19)

WinForms creates a control's native window handle lazily — on first `Visible`. Each tab page
holds dozens of custom-painted controls, so **showing a tab for the first time** created that
whole handle storm at once: a one-off white flash and a stutter per tab, repeated every time
the window was reopened (closing the form disposed everything). Three measures remove it:

- **Pre-warm hidden after startup.** `TrayContext` starts a one-shot ~1.8 s timer that calls
  `EnsureMain()` (creates the `MainForm` but does **not** `Show()` it) and `MainForm.EnsureWarm()`.
  `EnsureWarm` builds the remaining pages and calls `ForceHandles(control)` — a recursive walk
  that touches `Control.Handle` on every descendant, which **creates the native window even while
  invisible**. Page creation is spread out with `await Task.Delay(250)` between pages so the UI
  thread stays responsive. Idempotent via a `_warmed` guard; also invoked from `Shown` if the
  user opens the window before the timer fires.
- **Close = hide, not dispose.** `MainForm.FormClosing` cancels a `CloseReason.UserClosing` and
  `Hide()`s instead. The pages (and their warm handles) survive, so reopening from the tray is
  instant. Real teardown (tray → Exit, self-update, OS shutdown) has a different `CloseReason`,
  so it closes normally; `_main`'s `FormClosed` still nulls the tray's reference.
- **Reuse on open.** `OpenMain` goes through `EnsureMain()` and only forces `Normal` when the
  window is `Minimized` (so it doesn't clobber a maximized layout).

### 8.1 What a system window costs (v1.37, measured)

WinForms gives every control its own system window. Creating one is not free, and the cost is not
fixed: Windows reports each window (and, for list controls, each inserted item) to every program
that registered for window events. On a desktop running several such programs the same bare Win32
controls, created through `CreateWindowEx` with no GhostDeck code involved, took:

| Control | Desktop with listeners | Fresh, empty desktop |
|---|---|---|
| one window event | 0.13 ms | 0.007 ms |
| button | 1.2 - 3 ms | 0.3 - 2 ms |
| list box, 15 items | 7 ms + 37 ms | 2 ms + 4 ms |
| drop-down list, 15 items | 17 - 21 ms + 24 - 26 ms | 4 ms + 5 ms |

(The second column is the same process on a desktop created for the test, where no other program
has windows or listeners.) The Settings page holds 392 system windows, 22 of them drop-down lists
until v1.37. Effect of drawing the lists in the app (`ThemedComboBox`, §5), same machine, minutes
apart:

| | system lists | app-drawn lists |
|---|---|---|
| build the Settings page | 2.8 s | 0.4 - 0.5 s |
| create its windows | 1.0 - 1.5 s | 0.7 s |
| build the Fan curve page | 1.6 - 1.9 s | 0.03 - 0.04 s |

Consequences for new UI:

- every child control is a system window; a card that paints its rows itself costs one;
- no system list controls (`ComboBox`, `ListBox`) on pages — their cost is per item;
- a change that affects a few controls updates those controls. A full `BuildForm()` recreates
  every window of the page and is reserved for changes that touch all of it (language, a
  settings import). The profile order re-fills four colour rows and the profile lists in place
  (`SettingsPage.SyncProfileOrder`); a card whose rows come and go (schedule rules, the Custom
  charge slider, travel mode, the display modes) is rebuilt alone (`SettingsPage.RebuildCard`,
  TECHNICAL.md §73).

Measured on the live app after v1.37 (same machine, medians of three runs, action plus the
repaint that follows):

| | time |
|---|---|
| a switch / a list value | 4 ms / 3 ms |
| re-entering the Settings tab | 65 ms |
| changing the Settings sub-tab | 35 - 100 ms |
| full rebuild of the Settings page | 340 - 450 ms |
| rebuild of one card (v1.38) | 50 - 76 ms |
| full rebuild once the cards paint their own captions (v1.38) | 320 - 385 ms |

The card title and the row captions of the Settings page are painted by `CardSection` (v1.38,
TECHNICAL.md §73): 446 -> 323 system windows with the same pixels.

**Do NOT add `WS_EX_COMPOSITED`** to the pages. It was tried against scroll tearing and reverted
(a comment in `ThemedPage.CreateParams`… note marks it): compositing the whole child tree made
every tab paint slowly and flashed white on startup — the exact symptoms this section fixes.
The children are individually `DoubleBuffered` instead.

## 9. Interactive overlays on a buffered canvas (v1.21: history crosshair)

The Status page's canvas renders its (heavy) content once into a persistent `BufferedGraphics`
(§4) and scrolling/painting only blits it. The history charts add a **cursor overlay** - a
tracking line, dots and a "selected · now" value readout that follow the mouse - and that must
NOT trigger the heavy re-render on every mouse move.

The pattern:

1. **Geometry is captured during the buffered render.** `DrawHistoryChart` records each chart's
   plot rectangle, scale and series selectors (`HistPlot`), so the overlay can hit-test and map
   x → time without recomputing anything.
2. **The overlay draws per paint, on top of the blit.** `Canvas.OnPaint` = `_buf.Render(g)` +
   `DrawHistCursor(g)`. Mouse moves only store the point and call `Invalidate()` - the buffer is
   untouched.
3. **`ControlStyles.OptimizedDoubleBuffer` on the canvas is what kills the flicker.** Without it
   the buffer blit hits the screen first and the overlay text lands a moment later - visibly
   flashing on every invalidate. With it, blit + overlay are composed offscreen and presented as
   one frame. (This was a real user-reported bug; don't remove the style.)
4. **The value row is permanent, the cursor parts are conditional.** With no cursor over the
   plots the readout shows `--` for the selected value instead of a stale number.

Same idea applies to any future hover effect on a `BufferedGraphics` surface: bake the static
scene, overlay the dynamic bits per paint, and make the control double-buffered.

## 10. Click-open help bubbles (v1.31: HelpPopup / HelpDot)

`UI/Controls/HelpPopup.cs` replaces the system `ToolTip` wherever a control needs a paragraph of
explanation rather than a label. The system tooltip fails that job three ways: it styles itself
from the OS instead of the theme, it opens on hover so it covers the thing being pointed at, and it
takes itself away after a few seconds. The bubble opens on **click** on a `HelpDot` (the circled
"?"), stays until dismissed (a click anywhere, or the same dot again), and is drawn with the app''s
own card fill, accent border and text colours.

Implementation notes, in case it ever needs touching:

- It is a `ToolStripDropDown` **hosting a control** (`ToolStripControlHost` around a private
  `Card`). The dropdown class is used purely for its window behaviour - a top-level surface that
  shows without stealing focus and closes itself on an outside click. The hosted control is what
  gives it a size and painting: an *itemless* dropdown lays itself out to nothing, which shows up
  as a popup that "opens" invisibly and eats the next click.
- One bubble at a time, tracked statically. Clicking the dot of an open bubble must read as
  "close", but the outside-click close may already have run before that click arrives - so a close
  by the same owner within 250 ms suppresses the reopen instead of toggling twice.
- Placement hangs under the anchor and runs left, clamped to the screen''s working area, flipping
  above when there is no room below - so a dot near the right edge never pushes the bubble
  off-screen.
- `HelpDot` is a 22 px control (hand cursor, no tab stop) with a static `Render(g, rect)` for the
  hand-painted pages: the Status canvas and the Models table draw the same dot into their own
  surface and hit-test it themselves, so the dot looks identical whether it is a control or paint.
- Texts are supplied by `TextProvider` at click time, not captured at construction, so a language
  change (TECHNICAL.md §21a) or a live value is always current.

Used on: Scenarios feature bricks, the Models table''s Super Battery column header, and the Status
GPU-clock tile (TECHNICAL.md §61).

## 11. Branded pop-up cards: session summary and user-facing messages (v1.37)

Two windows share one visual language and one rendering technique - the "GhostDeck card":

- `SessionReportForm` - the game-session summary popup (stats, sparkline, action icons), anchored
  at the tray corner with a speech-bubble tail; never steals focus.
- `GhostCardForm` (#212, born as `FirmwareGuardForm` and generalised for the Apex explainer) -
  the decision card: a scan tag, heading, wrapped body text and two text buttons (accent action /
  "Later"), all passed to the constructor; centred on screen, takes focus, Enter = act,
  Esc / ✕ = later, draggable by the body. Carriers: the firmware guard (`//FIRMWARE-GUARD`) and
  the one-time Apex consent (`//APEX`).

Shared anatomy: a dark card (`#10151F` at 97% alpha) with square left corners and softly rounded
right ones, a 5 px cyan→violet rail flush with the left edge, the ghost + GhostDeck wordmark, and
a Consolas "scan tag" in the top-right corner (`//SESSION-END` in cyan; `//FIRMWARE-GUARD` in
amber - amber marks a warning). Both render per-pixel into a 32bpp ARGB bitmap pushed with
`UpdateLayeredWindow` (the OverlayForm technique): true alpha for the irregular shape, a fake
soft shadow from a few expanded low-alpha strokes (GDI+ has no blur), fonts sized in pixels and
scaled by `dpi / 96`. Buttons are drawn, not controls: rounded rects hit-tested in `OnMouseMove`,
with a hover repaint.

**Rule: user-facing messages and decisions use these cards, not `MessageBox` / `TaskDialog`.**
The system dialogs ignore the app theme entirely (see #212 - the guard originally spoke through
a tray balloon, then briefly a `TaskDialog`, both visually foreign). `MessageBox` remains
acceptable only inside developer/diagnostic tooling (TestDialog) and for file-dialog error paths
where a themed card would be overkill. When a new message window is needed, start from
`GhostCardForm` (text + buttons - often no new class is needed at all, it takes its texts and
action as constructor arguments) or `SessionReportForm` (rich content + icon actions).

**Editor cards.** A small editor is a card too, not a titled dialog with stock controls. A
layered window cannot host child controls, so `GhostCardForm` exposes a content block between
the body text and the buttons: a subclass overrides `MeasureContent` / `PaintContent` (same
bitmap, same `k = dpi / 96` scale), reports its clickable zones through `ContentHit` and reacts
in `ContentClick`; `HotZone` gives the hovered zone for the hover repaint and `CardWidth` widens
the card. `Acknowledge` is the accent action and returns whether the card may close (a
required field left empty keeps it open); `AuxLabel` / `AuxClick` add a third button on the
left of the button row (Delete). Cards are dark in both app themes, so everything on them uses
the fixed card palette (`White`, `Ink`, `Muted`, `Cyan`, `Fill` = the `AccentFill` token,
`FieldBg`), never `Theme.*`.

What cannot be painted rides in small owned windows over the card (`Forms/CardOverlays.cs`):

- `CardTextHost` - a real `TextBox` in a borderless owned window placed exactly over a field
  the card paints. `FieldBg` is opaque so the two match without a seam. The real control is
  what gives selection, clipboard, IME and the emoji panel; a painted text field would have
  none of them. The card re-aligns its hosts in `OnRendered` and `OnMove`, and creates them
  in `OnVisibleChanged` (the same call that shows the card), so a field is never seen empty.
  Enter / Esc / Tab are handed back to the card through `CommandKey`.
- `CardPopupList` - the drop-down of a painted select field: an owned borderless list in the
  card palette (selected item = `Fill`, optional icon per item, wheel scrolling past nine
  rows, ↑/↓ + Enter), closing on a pick, on Esc and on losing activation.

A modal editor sets `HostWindow` before it is shown and the card centres over that window;
`ShowDialog(owner)` alone does not do it, because `Owner` is still null when the handle is
created. Message cards have no host window and stay in the middle of the primary screen.

The base class owns what the editors share, so they cannot drift apart: the text hosts
(`AddTextField` from `CreateTextFields` - placement, the focus ring, Enter / Esc / Tab, the
caret in the first field when the card opens), the painters (`PaintSwitch`, `PaintTrack`,
`PaintCell`, `PaintSelect`, `PaintField`, `PaintCaption`, with `LabelFont` / `CellFont` /
`CaptionFont`) and `ShowOver(owner)` for a modal editor.

Carriers: `PowerMapForm` (profile → Windows power mode, `//WIN-POWER`; four rows of scenario
icon, profile name and a three-way segmented picker), `SceneEditForm` (`//SCENE`; name and
icon fields, then one row per setting with a painted switch and a picker), `ScheduleRuleForm`
(`//SCHEDULE`; scene select, seven weekday cells of which any number can be lit, two time
selects; a rule with no day keeps the card open with the day row marked), `ProfileOrderForm`
(`//PROFILES`; two presets in one frame and four rows with painted up / down arrows) and `InputDialog`
(one text field; an optional validator answers under the field - a taken preset name, a
Fan Boost time outside 1-120 minutes - instead of opening a second window). The picker rule
in the scene editor: two to four choices whose captions fit side by side become segments, all
visible at once; anything longer (profile, fan curve, brightness) becomes a select field with
a `CardPopupList`. Touching the picker of a row that is off switches the row on. Rows shrink
from 40 to 32 logical px when the screen is low, so the whole card always fits.
