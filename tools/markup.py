"""Служебные элементы строки, которые перевод обязан сохранить в том же количестве."""
import re
from collections import Counter

PATTERNS = [
    # Lua-вставки Dialogue System: [lua(Variable["PlayerFirstName"])]
    ("lua", re.compile(r"\[lua\(.*?\)\]")),
    # Прочие теги Dialogue System: [var=X], [em1], [/em1], [pic=1], [f], [a], [auto] ...
    ("ds", re.compile(r"\[(?!lua\()/?[a-z][a-z0-9]*(?:=[^\]\s]*)?\]")),
    # Rich text TextMeshPro: <color=#fff>, </color>, <size=80%>, <i>, <sprite=1>
    ("tag", re.compile(r"</?[a-zA-Z#][^<>\n]{0,80}>")),
    # Коды печатной машинки Dialogue System: \, \. \^ \< \> \n \\
    ("code", re.compile(r"\\[,.^<>n|\\]")),
    # Подстановки .NET и шаблонов мода: {0}, {1:N0}, {name}
    ("slot", re.compile(r"\{[0-9A-Za-z_]+(?::[^{}]*)?\}")),
    # printf: %s %d %.2f %1$s
    ("printf", re.compile(r"(?<![0-9A-Za-z])%(?:\d+\$)?[-+0#]*\d*(?:\.\d+)?[sdif](?![A-Za-z])")),
]


# Русские реплики могут вызывать Lua-функции мода вместо вставок, возвращающих английский текст.
# Перед сравнением они сводятся к исходной форме: количество и смысл вставок остаются прежними.
LUA_EQUIV = [
    (re.compile(r"\[lua\(Mod_obenseuerrus_Tr\((.*?)\)\)\]"), r"[lua(\1)]"),
    (re.compile(r"\[lua\(Mod_obenseuerrus_AgeName\(\)\)\]"), '[lua(Actor["Player"].AgeName)]'),
    (re.compile(r"\[lua\(Mod_obenseuerrus_AgeNameFormalShort\(\)\)\]"), '[lua(Actor["Player"].AgeNameFormalShort)]'),
    (re.compile(r"\[lua\(Mod_obenseuerrus_Pronoun\(\)\)\]"), '[lua(Actor["Player"].Pronoun)]'),
    (re.compile(r"\[lua\(Mod_obenseuerrus_PronounCapitalized\(\)\)\]"), '[lua(Actor["Player"].PronounCapitalized)]'),
]


def normalize_lua(text):
    for rx, repl in LUA_EQUIV:
        text = rx.sub(lambda m: m.expand(repl) if "\\1" in repl else repl, text)
    return text


def tokens(text):
    found = Counter()
    text = (text or "").replace("\r\n", "\n").replace("\r", "\n")
    text = normalize_lua(text)
    rest = text
    for name, rx in PATTERNS:
        for m in rx.findall(rest):
            found[(name, m)] += 1
        rest = rx.sub(" ", rest)
    found[("newline", "\\n")] += (text or "").count("\n")
    return found


def compare(source, target):
    """Пустая строка, если элементы совпадают; иначе краткое описание расхождения."""
    a, b = tokens(source), tokens(target)
    if a == b:
        return ""
    parts = []
    for key in sorted(set(a) | set(b)):
        if a[key] != b[key]:
            parts.append("%s %r: %d -> %d" % (key[0], key[1], a[key], b[key]))
    return "; ".join(parts)
