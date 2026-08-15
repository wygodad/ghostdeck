"""Guards the translation invariant in Core/Lang.cs, mechanically, on every push.

The project rule is that EVERY Lang.T key ships in all 15 languages (en/pl/de/fr/es/zh/pt/ru/ja/ko/zh-TW/tr/vi/id/it) -
never an English-only fallback. That rule used to rely on whoever edited the file noticing;
this script checks it instead. It also catches duplicate keys, which the dictionary's indexer
syntax accepts silently (the later entry wins and the earlier translations become dead code -
that is exactly how `set_check_updates` ended up defined twice).

Exit codes: 0 = fine, 1 = a problem worth failing the build for.
Run: python tools/lang-check.py
"""
import io
import os
import re
import sys

LANGS = 15
BACKSLASH = chr(92)
QUOTE = chr(34)
PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Core", "Lang.cs")


def entries(text):
    """Yield (key, value-literal) for every `["key"] = ...` / `m["key"] = ...` assignment."""
    pat = re.compile(r'\["([a-z0-9_]+)"\]\s*=\s*')
    i, n = 0, len(text)
    while True:
        m = pat.search(text, i)
        if not m:
            return
        j, depth, in_str, esc = m.end(), 0, False, False
        while j < n:
            c = text[j]
            if in_str:
                if esc:
                    esc = False
                elif c == BACKSLASH:
                    esc = True
                elif c == QUOTE:
                    in_str = False
            else:
                if c == QUOTE:
                    in_str = True
                elif c in "{[(":
                    depth += 1
                elif c in "}])":
                    depth -= 1
                elif c in ",;" and depth == 0:
                    break
            j += 1
        yield m.group(1), text[m.end():j]
        i = j + 1


def strings_in(value):
    """The string literals of one entry, in order."""
    out, cur, i, in_str, esc = [], [], 0, False, False
    while i < len(value):
        c = value[i]
        if in_str:
            if esc:
                cur.append(c)
                esc = False
            elif c == BACKSLASH:
                cur.append(c)
                esc = True
            elif c == QUOTE:
                out.append("".join(cur))
                cur, in_str = [], False
            else:
                cur.append(c)
        elif c == QUOTE:
            in_str = True
        i += 1
    return out


def main():
    text = io.open(PATH, encoding="utf-8").read()
    seen, problems = {}, []
    total = 0
    for key, value in entries(text):
        total += 1
        vals = strings_in(value)
        if len(vals) != LANGS:
            problems.append("%s: %d translations, expected %d" % (key, len(vals), LANGS))
        if any(not v.strip() for v in vals):
            problems.append("%s: an empty translation" % key)
        if key in seen:
            problems.append("%s: duplicate key - the later entry silently wins and the earlier "
                            "translations are dead" % key)
        seen[key] = value

    print("Lang.cs: %d keys x %d languages" % (total, LANGS))
    if problems:
        print("FAILED:")
        for p in problems:
            print("  -", p)
        return 1
    print("OK - every key is translated in all %d languages, no duplicates" % LANGS)
    return 0


if __name__ == "__main__":
    sys.exit(main())
