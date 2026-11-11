# Dashboard files

Since 1.45 the Dashboard can write two things to a file and read them back: the **style of one widget** (its settings) and a **whole layout** (every widget with its size and settings, and the column count). Both are the same small JSON file. This page describes the format and what GhostDeck checks before it uses a file.

The format is a promise: a file written by GhostDeck 1.45 loads in every later version.

## The file

```json
{
  "ghostdeck": "dashboard",
  "v": 1,
  "kind": "layout",
  "cols": 6,
  "items": [
    { "Type": "core.profiles", "W": 4, "H": 1 },
    { "Type": "core.cpuTemp", "W": 1, "H": 1, "Opt": { "color": "teal", "dial": "arc", "warn1": 65, "nowarn": false } },
    { "Type": "core.toggles", "W": 1, "H": 1, "Opt": { "show": ["boost"] } }
  ]
}
```

| Field | Meaning |
|---|---|
| `ghostdeck` | Always `"dashboard"`. Anything else is not a Dashboard file. |
| `v` | Format version, `1`. A later GhostDeck that has to change the shape raises it; an older GhostDeck then says the file is from a newer version and loads nothing. |
| `kind` | `"widget"` (one widget's style, exactly one item) or `"layout"` (1 to 200 items that cover at most 480 grid cells together). |
| `cols` | Layouts only, optional: `4`, `5`, `6` or `8`. Absent or `0` means automatic (the window decides); GhostDeck writes the field only when a column count is set. |
| `items` | The widgets, in their order on the Dashboard. |
| `Type` | The widget type, e.g. `core.cpuTemp`: 1 to 16 lower-case letters, a dot, then 1 to 40 letters, digits and underscores. A type this GhostDeck does not know shows as a grey field and is kept. |
| `W`, `H` | Size in grid cells, 1 to 6 wide and 1 to 4 high. A widget takes the nearest size it allows. |
| `Opt` | Optional settings of the widget: at most 24 of them, see the limits below. |

A setting has a plain key (a letter, then letters, digits and underscores, at most 24 characters) and one of four kinds of value:

- a text of at most 64 characters,
- a whole number,
- `true` or `false`,
- a list of 1 to 16 such texts, none of them twice.

The settings of one widget take at most 2048 characters together. They are counted as the keys plus the values written as JSON with every letter outside ASCII as a `\uXXXX` escape, so a letter such as `ż` or `漢` counts as six characters and an emoji as twelve, however the file itself spells it.

## What is checked

A file can come from anywhere, so GhostDeck trusts nothing in it.

- **All or nothing.** One thing out of place and the file is refused whole; the Dashboard stays exactly as it was.
- **Small, shallow, short, of a bounded area.** At most 256 KB, at most six levels of nesting, at most 200 widgets, and at most 480 grid cells covered by all widgets together (width times height, summed: 80 rows of a six-column grid). The area bound keeps a file from making the Dashboard taller than the app is built for. The same two bounds hold for widgets added by hand and for `settings.json`.
- **Only the fields above are read.** Anything else in the file is ignored and never reaches `settings.json`. Every widget is built anew from checked values; nothing of the file is carried over as it stands.
- **No name twice.** A field that appears twice in an object that is read (the file itself, an item, its settings) refuses the file. A field that is ignored is ignored with whatever it holds.
- **Texts are plain.** A text is refused when it is empty, has a space at either end, or holds a control character, half of a surrogate pair, a character that changes the direction text is shown in (U+202A to U+202E, U+2066 to U+2069, U+200E, U+200F, U+061C), an invisible character that carries no text (zero-width space U+200B, word joiner U+2060, byte order mark U+FEFF, U+FFFE, U+FFFF), or a letter that is not in its composed form (NFC). The zero-width joiner U+200D is allowed, because emoji are built with it. It is the same rule GhostDeck applies to a text typed in the app and to texts in `settings.json`, so what the app stores can always be written to a file and read back the same.
- **Data only.** No text from a file is ever used as a path, a command, an address or code. A text is painted as a label, or compared with what the app already knows (a profile, a scene id, a colour name).

Loading a layout replaces the layout on the Dashboard, and the one before can be brought back once with "Undo the load". A widget's style is loaded into its settings card, where it can be looked at and saved or cancelled like any other change.

## How a file is written

- Plain UTF-8. Letters of every script and emoji are written as they are; only the quote, the backslash and control characters are escaped. The file is indented while that fits 256 KB and compact when it does not.
- Whatever loaded from a file can be written to a file again. A layout built in the app can be larger than a file holds (many widgets with many long texts); it is then not written, and the card says the layout is too large for a file. An empty Dashboard is not written either.
- The file is checked by reading it back before it is handed over, written to a temporary file beside the target, and put in the target's place only when it is whole. A full disk or a file that cannot be replaced leaves the old file as it was.

## What a file does not hold

No paths, no machine or user data, no hardware readings. A layout holds the labels you typed, because they are part of it.

Scenes are stored by their id on your machine. When a file is loaded and a Scenes widget in it picked scenes of which none exists on this machine, that pick is left out and the widget shows all scenes. A pick with at least one scene of this machine is kept as it is.

## Saved layouts

"Layouts" in edit mode also keeps up to 12 layouts under a name inside `settings.json` (`DashboardSaved`), to bring back from a list. They follow the same limits. A name has at most 40 characters and follows the text rule above; two names that differ only in case, or only in how a letter is composed, are one name. GhostDeck 1.44 does not know the field and keeps it untouched.

## A damaged settings.json

The Dashboard's part of `settings.json` is cleaned when the app starts, value by value. A widget whose type is not an id of the shape above is dropped. A setting that cannot be read, has a bad key or a value of another shape is dropped alone; the widget's other settings stay. When one widget has more than 24 settings or more than 2048 characters of them, the ones that do not fit are left out from the end, never all of them. Widgets past the 200th, or past 480 cells, are dropped from the end.
