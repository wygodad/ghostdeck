"""Guards the display-scaling rules and the no-long-dash rule, mechanically, on every push.

GhostDeck runs at every Windows display scale (100 % to 250 %+). Issue #172 (Turkish Windows at
about 200 %) showed what goes wrong when a size is a fixed number of pixels: text grows with
the scale, the box around it does not, and the text is cut. The rules this script checks:

  dpi-source   DPI is read in ONE place: Dpi.Of(control) in UI/Dpi.cs. Direct reads of
               DeviceDpi / DpiX / DpiY elsewhere would bypass the preview tool's FAKEDPI
               override, so a page could look fine in the preview and still break for users.
  font         Fonts are created through Dpi.Font(...), never `new Font(...)`, for the same
               reason (the preview tool scales fonts through that one helper).
  text-rect    A painted text rectangle never has a fixed pixel height:
               `TextRenderer.DrawText(..., new Rectangle(x, y, w, 28), ...)` is cut at high
               scaling. Derive the height from the font (font.Height, MeasureText) or scale it
               with the page's S() helper.
  ellipsis     No "..." truncation of interface text: TextFormatFlags.EndEllipsis hides words.
               Make the space fit the text, or wrap it (WordBreak) and let the box grow.
  long-dash    No long dash in interface text: the em dash (U+2014) is not used in any string
               in the code (Core/Lang.cs included) nor in the newest announcements.json entry;
               write " - " or a comma instead. The en dash (U+2013) is allowed only between
               two digits, as a range ("30–40 W").

Exceptions are explicit and carry a reason, so the next reader knows why:
  - one line:   `// dpi-ok: <reason>` at the end of the line or on the line right above it
                (for a statement spread over several lines, anywhere in that statement);
  - whole file: `// dpi-ok-file: <reason>` near the top (e.g. a bitmap drawn at a resolution
                of its own).
Comments are never checked, and the long-dash rule has no exceptions.

Exit codes: 0 = fine, 1 = a problem worth failing the build for.
Run: python tools/dpi-check.py
"""
import io
import json
import os
import re
import sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
DIRS = ("Core", "UI", "Forms")
TOP_FILES = ("Program.cs", "TrayContext.cs")
DPI_FILE = os.path.normpath("UI/Dpi.cs")
EM, EN = "—", "–"
BACKSLASH, QUOTE = chr(92), chr(34)


def sources():
    for d in DIRS:
        for base, _, files in os.walk(os.path.join(ROOT, d)):
            for f in sorted(files):
                if f.endswith(".cs"):
                    yield os.path.normpath(os.path.relpath(os.path.join(base, f), ROOT))
    for f in TOP_FILES:
        if os.path.exists(os.path.join(ROOT, f)):
            yield f


def lex(text):
    """Split C# source into code (strings blanked, comments removed), string literals and
    comments. Returns (code, strings, comments): code keeps every newline so offsets map to
    lines; strings = [(line, literal text)], comments = [(line, comment text)]."""
    code, strings, comments = [], [], []
    i, n, line = 0, len(text), 1
    # stack of interpolation contexts: each entry = brace depth inside the current hole
    holes = []

    def emit(s):
        nonlocal line
        code.append(s)
        line += s.count("\n")

    def blank(s):
        # keep the newlines of a literal/comment so line numbers stay right
        emit("".join("\n" if c == "\n" else " " for c in s))

    def read_string(j, interp, verbatim):
        """j points just past the opening quote. Returns (end index past closing quote, text)."""
        buf, depth = [], 0
        while j < n:
            c = text[j]
            if interp and c == "{":
                if j + 1 < n and text[j + 1] == "{":
                    buf.append("{"); j += 2; continue
                # hole: skip balanced code, nested strings included
                j = skip_hole(j + 1)
                buf.append("{}")
                continue
            if verbatim:
                if c == QUOTE:
                    if j + 1 < n and text[j + 1] == QUOTE:
                        buf.append(QUOTE); j += 2; continue
                    return j + 1, "".join(buf)
                buf.append(c); j += 1; continue
            if c == BACKSLASH and j + 1 < n:
                buf.append(text[j:j + 2]); j += 2; continue
            if c == QUOTE or c == "\n":
                return j + 1, "".join(buf)
            buf.append(c); j += 1
        return j, "".join(buf)

    def skip_hole(j):
        depth = 1
        while j < n and depth:
            c = text[j]
            if c == "{":
                depth += 1
            elif c == "}":
                depth -= 1
            elif c == QUOTE or (c in "$@" and j + 1 < n and text[j + 1] in (QUOTE, "$", "@")):
                k, interp, verb = j, False, False
                while k < n and text[k] in "$@":
                    interp |= text[k] == "$"; verb |= text[k] == "@"; k += 1
                if k < n and text[k] == QUOTE:
                    j, s = read_string(k + 1, interp, verb)
                    strings.append((line, s))
                    continue
            elif c == "'":
                j = skip_char(j)
                continue
            j += 1
        return j

    def skip_char(j):
        # 'x', '\n', '\''
        k = j + 1
        if k < n and text[k] == BACKSLASH:
            k += 2
        else:
            k += 1
        while k < n and text[k] != "'" and text[k] != "\n":
            k += 1
        return k + 1

    while i < n:
        c = text[i]
        if text.startswith("//", i):
            j = text.find("\n", i)
            j = n if j < 0 else j
            comments.append((line, text[i:j]))
            blank(text[i:j])
            i = j
            continue
        if text.startswith("/*", i):
            j = text.find("*/", i + 2)
            j = n if j < 0 else j + 2
            comments.append((line, text[i:j]))
            blank(text[i:j])
            i = j
            continue
        # raw string literal: optional $..., then 3+ quotes
        m = re.match(r'\$*("{3,})', text[i:i + 12])
        if m:
            q = m.group(1)
            start = i + m.end()
            j = text.find(q, start)
            j = n if j < 0 else j
            strings.append((line, text[start:j]))
            blank(text[i:j + len(q)])
            i = j + len(q)
            continue
        if c == QUOTE or (c in "$@" and i + 1 < n and text[i + 1] in (QUOTE, "$", "@")):
            k, interp, verb = i, False, False
            while k < n and text[k] in "$@":
                interp |= text[k] == "$"; verb |= text[k] == "@"; k += 1
            if k < n and text[k] == QUOTE:
                start_line = line
                j, s = read_string(k + 1, interp, verb)
                strings.append((start_line, s))
                # blank the literal but keep it recognisable as a string in the code
                lit = text[i:j]
                emit(QUOTE + "".join("\n" if ch == "\n" else " " for ch in lit[1:-1]) + QUOTE if len(lit) >= 2 else lit)
                i = j
                continue
        if c == "'":
            j = skip_char(i)
            emit(" " * (j - i))
            i = j
            continue
        emit(c)
        i += 1
    return "".join(code), strings, comments


def line_of(code, pos):
    return code.count("\n", 0, pos) + 1


def balanced(code, open_pos):
    """Index just past the parenthesis that closes the one at open_pos."""
    depth = 0
    for j in range(open_pos, len(code)):
        if code[j] == "(":
            depth += 1
        elif code[j] == ")":
            depth -= 1
            if depth == 0:
                return j + 1
    return len(code)


def split_args(s):
    out, depth, cur = [], 0, []
    for c in s:
        if c in "([{":
            depth += 1
        elif c in ")]}":
            depth -= 1
        if c == "," and depth == 0:
            out.append("".join(cur).strip()); cur = []
        else:
            cur.append(c)
    out.append("".join(cur).strip())
    return out


def statement_lines(code, pos):
    """Line range of the statement around pos (from the previous ; { } to the next ;)."""
    a = max(code.rfind(";", 0, pos), code.rfind("{", 0, pos), code.rfind("}", 0, pos))
    b = code.find(";", pos)
    b = len(code) if b < 0 else b
    return line_of(code, a + 1), line_of(code, b)


def main():
    problems = []
    checked = 0
    for rel in sources():
        path = os.path.join(ROOT, rel)
        text = io.open(path, encoding="utf-8-sig").read()
        code, strings, comments = lex(text)
        checked += 1
        ok_lines = set()
        file_ok = False
        for ln, c in comments:
            if "dpi-ok-file:" in c:
                file_ok = True
            if "dpi-ok:" in c:
                ok_lines.add(ln)

        def excused(pos):
            if file_ok:
                return True
            first, last = statement_lines(code, pos)
            ln = line_of(code, pos)
            return any(l in ok_lines for l in range(min(first, ln) - 1, max(last, ln) + 1))

        def report(pos_or_line, rule, msg, by_line=False):
            ln = pos_or_line if by_line else line_of(code, pos_or_line)
            problems.append("%s:%d: [%s] %s" % (rel.replace(os.sep, "/"), ln, rule, msg))

        if rel != DPI_FILE:
            for m in re.finditer(r"\b(DeviceDpi|DpiX|DpiY)\b", code):
                if not excused(m.start()):
                    report(m.start(), "dpi-source", "%s read directly - use Dpi.Of(control) (UI/Dpi.cs)" % m.group(1))
            for m in re.finditer(r"\bnew\s+Font\s*\(|\bFont\s+\w+\s*(=|=>)\s*new\s*\(", code):
                if not excused(m.start()):
                    report(m.start(), "font", "font created directly - use Dpi.Font(...)")

        for m in re.finditer(r"\b(?:TextRenderer|Ui)\.DrawText\s*\(", code):
            end = balanced(code, m.end() - 1)
            call = code[m.end():end - 1]
            for r in re.finditer(r"\bnew\s+Rectangle\s*\(", call):
                rend = balanced(call, r.end() - 1)
                args = split_args(call[r.end():rend - 1])
                if len(args) == 4 and re.fullmatch(r"\d+", args[3]) and int(args[3]) > 0:
                    pos = m.end() + r.start()
                    if not excused(pos):
                        report(pos, "text-rect", "text rectangle %s px high - derive it from the font "
                               "(font.Height / MeasureText) or scale it with S()" % args[3])

        for m in re.finditer(r"\bEndEllipsis\b", code):
            if not excused(m.start()):
                report(m.start(), "ellipsis", "EndEllipsis cuts interface text - fit or wrap it instead "
                       "(or say why with // dpi-ok: <reason>)")

        for ln, s in strings:
            if EM in s:
                report(ln, "long-dash", "long dash in a string - write \" - \" or a comma instead", by_line=True)
            for k, ch in enumerate(s):
                if ch == EN and not (k > 0 and s[k - 1].isdigit() and k + 1 < len(s) and s[k + 1].isdigit()):
                    report(ln, "long-dash", "en dash outside a number range - write \" - \" or a comma instead",
                           by_line=True)
                    break

    # newest announcement (the first entry) - the first thing a user reads after an update
    ann_path = os.path.join(ROOT, "announcements.json")
    if os.path.exists(ann_path):
        raw = io.open(ann_path, encoding="utf-8").read()
        ann = json.loads(raw)
        if ann:
            first = ann[0]
            for k, v in first.items():
                if not isinstance(v, str):
                    continue
                bad = EM in v or any(ch == EN and not (i > 0 and v[i - 1].isdigit() and i + 1 < len(v) and v[i + 1].isdigit())
                                     for i, ch in enumerate(v))
                if bad:
                    key_pos = raw.find('"%s"' % k)
                    ln = raw.count("\n", 0, key_pos) + 1 if key_pos >= 0 else 1
                    problems.append("announcements.json:%d: [long-dash] %s (entry %s) - write \" - \" or a comma "
                                    "instead" % (ln, k, first.get("id", "?")))

    print("dpi-check: %d source files" % checked)
    if problems:
        print("FAILED (%d):" % len(problems))
        for p in problems:
            print("  -", p)
        return 1
    print("OK - one DPI source, fonts via Dpi.Font, no fixed-height text rectangles, every ellipsis explained, no long dashes")
    return 0


if __name__ == "__main__":
    sys.exit(main())
