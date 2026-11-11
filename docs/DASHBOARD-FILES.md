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
| `kind` | `"widget"` (one widget's style, exactly one item) or `"layout"` (1 to 200 items). |
| `cols` | Layouts only, optional: `0` (automatic), `4`, `5`, `6` or `8`. |
| `items` | The widgets, in their order on the Dashboard. |
| `Type` | The widget type, e.g. `core.cpuTemp`: lower-case letters, a dot, then letters, digits and underscores. A type this GhostDeck does not know shows as a grey field and is kept. |
| `W`, `H` | Size in grid cells, 1 to 6 wide and 1 to 4 high. A widget takes the nearest size it allows. |
| `Opt` | Optional settings of the widget: at most 24 of them. |

A setting has a plain key (a letter, then letters, digits and underscores, at most 24 characters) and one of four kinds of value:

- a text of at most 64 characters,
- a whole number,
- `true` or `false`,
- a list of at most 16 such texts.

## What is checked

A file can come from anywhere, so GhostDeck trusts nothing in it.

- **All or nothing.** One thing out of place and the file is refused whole; the Dashboard stays exactly as it was.
- **Small, shallow, short.** At most 256 KB, at most six levels of nesting, at most 200 widgets.
- **Only the fields above are read.** Anything else in the file is ignored and never reaches `settings.json`. Every widget is built anew from checked values; nothing of the file is carried over as it stands.
- **No name twice.** A field or a setting that appears twice in one object refuses the file.
- **Texts are plain.** No control characters, no half of a surrogate pair, and none of the characters that change the direction text is shown in.
- **Data only.** No text from a file is ever used as a path, a command, an address or code. A text is painted as a label, or compared with what the app already knows (a profile, a scene id, a colour name).

Loading a layout replaces the layout on the Dashboard, and the one before can be brought back once with "Undo the load". A widget's style is loaded into its settings card, where it can be looked at and saved or cancelled like any other change.

## What a file does not hold

No paths, no machine or user data, no hardware readings. A layout holds the labels you typed, because they are part of it. Scenes are stored by their id on your machine, so a Scenes widget that picked some scenes shows all scenes on another machine.

## Saved layouts

"Layouts" in edit mode also keeps up to 12 layouts under a name inside `settings.json` (`DashboardSaved`), to bring back from a list. They follow the same limits. GhostDeck 1.44 does not know the field and keeps it untouched.
