# Icon sources (SVG)

Vector sources of the icons drawn in code. The app renders them with GDI+ (no icon font
or SVG runtime dependency); these files are the canonical geometry so the icons can be
edited, previewed and reused (website, docs) without reverse-engineering the C#.

| File | Used for | Drawn in code by |
|---|---|---|
| `silent-feather.svg` | Silent profile | `IconPainter.Scenario` (ProfileId.Silent) |
| `balanced-scales.svg` | Balanced profile | `IconPainter.Scenario` (ProfileId.Balanced) |
| `extreme-bolt.svg` | Extreme profile | `IconPainter.Scenario` (ProfileId.Extreme) |
| `super-battery.svg` | Super Battery profile | `IconPainter.Scenario` (ProfileId.SuperBattery) |
| `ghost-mark.svg` | Tray icon, header wordmark | `TrayIconFactory.DrawGhost` |

Conventions:
- 32-unit design grid (`viewBox="0 0 32 32"`); profile icons use centre 16,16 and radius s=13.
- Stroke 2.4, round caps/joins, `currentColor` so the consumer picks the colour
  (the app uses the per-profile colour from Settings).
- When changing an icon, update BOTH the SVG and the matching GDI+ code path.
