"""Замена отдельных строк в файле перевода пакета: python -I tools/patch_batch.py FILE.ru.txt PATCH.txt
PATCH.txt — строки вида N|новый перевод (как в самом файле перевода)."""
import sys

target, patch = sys.argv[1], sys.argv[2]
fix = {}
with open(patch, encoding="utf-8") as f:
    for line in f:
        line = line.rstrip("\n")
        if line.strip() and not line.startswith("#"):
            n, _, t = line.partition("|")
            fix[n.strip()] = line
out = []
with open(target, encoding="utf-8") as f:
    for line in f:
        line = line.rstrip("\n")
        n = line.partition("|")[0].rstrip("?").strip()
        out.append(fix.pop(n, line))
out.extend(fix.values())
with open(target, "w", encoding="utf-8", newline="\n") as f:
    f.write("\n".join(out) + "\n")
print("заменено/добавлено строк:", len(sys.argv) and "ok")
