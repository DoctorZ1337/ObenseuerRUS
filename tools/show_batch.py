"""Показывает оригинал и перевод строк пакета рядом, с числом кодов \\, :
python -I tools/show_batch.py build/batches/X.txt build/batches/X.ru.txt 12 45 ...
Без номеров — все строки, где разметка не совпадает."""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import markup  # noqa: E402

sys.stdout.reconfigure(encoding="utf-8")
batch, ru = sys.argv[1], sys.argv[2]
want = set(sys.argv[3:])
src, tgt = {}, {}
for line in open(batch, encoding="utf-8"):
    if line.startswith("#"):
        continue
    n, _, rest = line.rstrip("\n").partition("|")
    src[n] = rest.partition("|")[2]
for line in open(ru, encoding="utf-8"):
    n, _, rest = line.rstrip("\n").partition("|")
    n = n.rstrip("?!").strip()
    tgt[n] = rest
for n in sorted(src, key=lambda x: int(x)):
    s, t = src[n].replace("¶", "\n"), tgt.get(n, "").replace("¶", "\n")
    if t == "=":
        t = s
    diff = markup.compare(s, t)
    if (want and n in want) or (not want and diff):
        print("%s  [%s]\n  EN: %s\n  RU: %s" % (n, diff or "ok", src[n], tgt.get(n, "")))
