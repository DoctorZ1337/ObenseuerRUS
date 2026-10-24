"""
Строки интерфейса из кода игры (по декомпилированному Assembly-CSharp) -> translations/code.json.

Декомпиляция (только чтение игры, результат вне репозитория):
  dotnet tool install ilspycmd --tool-path <папка>
  <папка>/ilspycmd -p -o <decomp> -r "<игра>/Obenseuer_Data/Managed" "<игра>/Obenseuer_Data/Managed/Assembly-CSharp.dll"
Затем:
  python -I tools/extract/code_strings.py <decomp>

Ищутся строки, которые попадают на экран: присваивания .text и локальных title/details/text,
подписи строки управления ("C)DRAG & DROP"), UseText.ChangeUseText, уведомления,
предупреждения строительного меню, useMessage/title, [Description] у перечислений, подсказки карточных игр,
return в методах вида Get...Text/Title/Name/Message. Склейки "A: " + x и $"...{x}..." превращаются
в шаблоны "A: {0}". Это кандидаты: что из них реально видно игроку, решается при переводе.
"""
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)
import catalog_io  # noqa: E402

SKIP_DIRS = ("BehaviorDesigner", "Pathfinding", "ECM", "Beautify", "DG.", "Steamworks", "CrazyMinnow", "Unity.",
             "UnityStandardAssets", "AmplifyShader", "TMPro", "FullSerializer", "SFB", "Properties")
SKIP_FILES = re.compile(r"^(Debug|DeveloperConsole|Achievements|Export|Import|Azure|Hx|BasicSample|Canvas|StandaloneFileBrowser|Lutify|Test)")
SINKS = [
    re.compile(r"\.text\s*\+?=\s*(?P<e>.+);\s*$"),
    re.compile(r"ChangeUseText\((?P<e>.+)\);\s*$"),
    re.compile(r"CreateNotification\((?P<e>.+)\);\s*$"),
    re.compile(r"ShowWarningText(?:Timed)?\((?P<e>.+)\);\s*$"),
    re.compile(r"ShowNotification\((?P<e>.+)\);\s*$"),
    # Поля с префиксом тоже: _winningLineUseMessage = "selected 1: center" (SlotMachineGameplay).
    re.compile(r"\b(?:\w*[uU]seMessage|title|objectName|noRequiredItemsText|cantUseText)\s*=\s*(?P<e>.+);\s*$"),
    re.compile(r"\b(?:AddText|AddList|HintContent|ShowLine|ShowHint|SetHint|ShowTutorialButton)\((?P<e>.+)\)"),
    # Локальные переменные, из которых потом собирается текст окна или подсказки (PlayerStatsToolTipInfo и т.п.).
    re.compile(r"^\s*(?:string\s+)?(?:title|details|description|message|msg|info|label|tooltip|header|"
               r"unlockedMsg|lockedMsg|warning|hint|useText|text\d*)\s*\+?=\s*(?P<e>.+);\s*$"),
    # Подписи строки управления: KeyCodeInfoToString(text, "DRAG & DROP", ...).
    re.compile(r"KeyCodeInfoToString\(\s*\w+\s*,\s*(?P<e>\"[^\"]*\")"),
]
# '... + ")EXIT"': подпись после клавиши; сама строка управления переводится по частям (TextTranslator).
KEY_LABEL = re.compile(r'"\)([A-Z][A-Za-z0-9 /&:.+\-]*?)\s*"')
DESCRIPTION = re.compile(r'\[Description\("(?P<s>(?:[^"\\]|\\.)*)"\)\]')
METHOD = re.compile(r"^\s*(?:public|private|protected|internal)\s+(?:static\s+|override\s+|virtual\s+)*string\s+(?P<n>\w+)\s*\(")
# Locked: UpgradePanel.IsLocked возвращает причину ("Inferior upgrade", "Pay the final payment").
UI_METHOD = re.compile(r"(Text|Title|Name|Message|Description|String|Label|Info|Status|Reason|Response|Tooltip|ToolTip|Hint|Unit|Locked)", re.I)
RETURN = re.compile(r"^\s*return\s+(?P<e>.+);\s*$")
CASE_RETURN = re.compile(r"=>\s*(?P<e>\$?\"(?:[^\"\\]|\\.)*\")\s*,?\s*$")
STRING = re.compile(r'\$?@?"(?:[^"\\]|\\.)*"')
LETTERS = re.compile(r"[A-Za-z]{2,}")
NOISE = re.compile(r"\.(png|jpg|bytes|json|txt|cs)\b|/StreamingAssets|Assets/|=\(don't set\)|^[A-Za-z]+_[A-Za-z_]+$")
BAD_CONTEXT = re.compile(r"Debug\.Log|GetKey|Resources\.Load|Find\(|CompareTag|SetTrigger|SetBool|Animator|PlayerPrefs|"
                         r"Contains\(\"|StartsWith\(\"|EndsWith\(\"|== \"|!= \"|Categories|addressable|Path\.|GetComponent|LoadScene|\.name\b")


def unescape(lit):
    body = lit.lstrip("$@")[1:-1]
    return body.encode("latin-1", "backslashreplace").decode("unicode_escape") if "\\" in body else body


def split_top(expr, sep):
    parts, depth, cur, i, q = [], 0, "", 0, None
    while i < len(expr):
        c = expr[i]
        if q:
            cur += c
            if c == "\\":
                cur += expr[i + 1:i + 2]
                i += 2
                continue
            if c == q:
                q = None
        elif c == '"':
            q = '"'
            cur += c
        elif c in "([{":
            depth += 1
            cur += c
        elif c in ")]}":
            depth -= 1
            cur += c
        elif depth == 0 and expr.startswith(sep, i):
            parts.append(cur)
            cur = ""
            i += len(sep)
            continue
        else:
            cur += c
        i += 1
    parts.append(cur)
    return [p.strip() for p in parts]


def interpolated(lit):
    """$"Skill: {Skill} is too low!" -> "Skill: {0} is too low!" (литеральные {{ }} сохраняются)."""
    body = unescape(lit)
    out, n, i = "", 0, 0
    while i < len(body):
        if body.startswith("{{", i) or body.startswith("}}", i):
            out += body[i]
            i += 2
            continue
        if body[i] == "{":
            j = body.index("}", i)
            out += "\0"
            i = j + 1
            continue
        out += body[i]
        i += 1
    return out


def to_template(expr):
    """Склейка строк и выражений -> текст шаблона, где \\0 — изменяемая часть."""
    expr = expr.strip()
    m = re.match(r"^string\.Format\((?P<a>.+)\)$", expr)
    if m:
        args = split_top(m.group("a"), ",")
        if STRING.fullmatch(args[0]):
            return re.sub(r"\{\d+(?::[^}]*)?\}", "\0", unescape(args[0]))
    out = ""
    for part in split_top(expr, "+"):
        part = part.strip()
        while part.startswith("(") and part.endswith(")") and len(split_top(part[1:-1], "+")) == 1 and part.count("(") == 1:
            part = part[1:-1].strip()
        if STRING.fullmatch(part):
            out += interpolated(part) if part.startswith("$") else unescape(part)
        else:
            out += "\0"
    return out


def finalize(tpl):
    """\\0 -> {0}, {1}...; соседние изменяемые части сливаются."""
    tpl = re.sub("\0+", "\0", tpl)
    n = 0
    res = ""
    for ch in tpl:
        if ch == "\0":
            res += "{%d}" % n
            n += 1
        else:
            res += ch
    return res, n


def literal_strings(expr):
    for lit in STRING.findall(expr):
        if lit.startswith("$"):
            continue
        s = unescape(lit)
        if LETTERS.search(s):
            yield s


def scan(decomp):
    found = {}

    def add(text, where, code):
        if not text or len(text) > 400 or "\0" in text or NOISE.search(text):
            return
        # Переводчик сопоставляет участки между тегами по отдельности, поэтому кандидаты — тоже участки.
        text = re.sub(r"^[^<]*?>", "", text) if re.match(r"^[^<]*>", text) else text
        text = re.sub(r"<[^>]*$", "", text)
        for run in re.split(r"<[^<>]*>", text):
            run_n, _ = finalize(run.replace("{0}", "\0").replace("{1}", "\0").replace("{2}", "\0").replace("{3}", "\0"))
            if not LETTERS.search(re.sub(r"\{\d+\}", "", run_n)):
                continue
            e = found.setdefault(run_n, {"where": set(), "code": code[:200]})
            e["where"].add(where)

    for dp, dn, fn in os.walk(decomp):
        rel = os.path.relpath(dp, decomp)
        if rel != "." and rel.startswith(SKIP_DIRS):
            continue
        for f in fn:
            if not f.endswith(".cs") or SKIP_FILES.match(f):
                continue
            method = ""
            for i, line in enumerate(open(os.path.join(dp, f), encoding="utf-8", errors="replace"), 1):
                where = "%s:%d" % (f, i)
                m = METHOD.match(line)
                if m:
                    method = m.group("n")
                for d in DESCRIPTION.finditer(line):
                    add(unescape('"' + d.group("s") + '"'), where, line.strip())
                if '"' not in line or BAD_CONTEXT.search(line):
                    continue
                exprs = []
                for label in KEY_LABEL.findall(line):
                    add(label.strip(), where, line.strip())
                for rx in SINKS:
                    m = rx.search(line)
                    if m:
                        exprs.extend(split_top(m.group("e"), ","))
                if method and UI_METHOD.search(method):
                    m = RETURN.match(line) or CASE_RETURN.search(line)
                    if m:
                        exprs.append(m.group("e"))
                for e in exprs:
                    for alt in split_top(e, "?") if "?" in e and ":" in e else [e]:
                        for branch in split_top(alt, ":") if alt is not e else [alt]:
                            tpl, n = finalize(to_template(branch))
                            add(tpl, where, line.strip())
                    for s in literal_strings(e):
                        add(s, where, line.strip())
    return found


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    decomp = sys.argv[1]
    found = scan(decomp)
    entries = []
    for i, (text, info) in enumerate(sorted(found.items())):
        kind = "template" if re.search(r"\{\d\}", text) else "text"
        if kind == "template" and len(re.sub(r"\{\d\}", "", text).strip()) < 2:
            continue
        # "{0}Bad" (text + "Bad") и "{0}_{1}_Conn" — склейки без разделителя: как шаблоны они слишком общие,
        # такие строки описаны вручную целиком в translations/manual.json.
        if kind == "template" and re.search(r"\{\d\}[A-Z]|_\{\d\}|\{\d\}_", text):
            continue
        where = sorted(info["where"])
        entries.append({"id": "code/" + ("t/" if kind == "template" else "") + catalog_io_id(text), "kind": kind,
                        "source": text, "target": "", "status": "todo",
                        "context": "; ".join(where[:3]) + (" (+%d)" % (len(where) - 3) if len(where) > 3 else "") + " | " + info["code"]})
    path = os.path.join(ROOT, "translations", "code.json")
    stats = catalog_io.merge(entries, catalog_io.load(path))
    catalog_io.write(path, "code", entries, "25969064")
    print("code.json: %d строк (сохранено %d, изменилось %d, новых %d)" % (len(entries), stats["kept"], stats["changed"], stats["new"]))


def catalog_io_id(text):
    import hashlib
    slug = re.sub(r"[^A-Za-z0-9]+", "-", text).strip("-")[:40]
    return slug + "-" + hashlib.sha1(text.encode("utf-8")).hexdigest()[:8]


if __name__ == "__main__":
    main()
