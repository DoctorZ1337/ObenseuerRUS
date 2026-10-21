"""
Проверка translations/*.json и отчёт о готовности перевода.

  python -I tools/check.py            -> ошибки в консоль, отчёты в reports/
  python -I tools/check.py --strict   -> предупреждения тоже считаются ошибками

Проверяется: формат и UTF-8, пустые и дублирующиеся id, известные kind/status, обязательные поля,
шаблоны {0}, сохранность разметки (теги, Lua-вставки, коды \\, и т.п., переводы строк),
кириллица в переводе, смешение латиницы и кириллицы в одном слове, пробел перед знаком препинания,
расхождения переводов одной строки.
"""
import argparse
import json
import os
import re
import sys
from collections import Counter, defaultdict

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
sys.path.insert(0, HERE)
import markup  # noqa: E402

FORMAT = "obenseuer-rus/1"
KINDS = {"text", "template", "table", "dialogue", "dialogue-menu"}
STATUSES = {"translated", "review", "todo"}
CYR = re.compile(r"[А-Яа-яЁё]")
LAT = re.compile(r"[A-Za-z]")
LOWER_WORD = re.compile(r"\b[a-z]{3,}\b")
MIXED_WORD = re.compile(r"\b(?=\w*[А-Яа-яЁё])(?=\w*[A-Za-z])\w+\b")
SLOT = re.compile(r"\{(\d)\}")
PAUSE_CODES = re.compile(r"\\[,.^<>|]")
SPACE_BEFORE_PUNCT = re.compile(r" [,!?:;]| \.(?!\.)")
STRIP_MARKUP = [rx for _, rx in markup.PATTERNS]


def visible(text):
    """Текст без разметки — для проверок на кириллицу и смешение алфавитов."""
    text = markup.normalize_lua(text or "")
    for rx in STRIP_MARKUP:
        text = rx.sub(" ", text)
    return text


def check(folder):
    errors, warnings = [], []
    entries = []
    for name in sorted(os.listdir(folder)):
        if not name.endswith(".json"):
            continue
        path = os.path.join(folder, name)
        raw = open(path, "rb").read()
        if raw.startswith(b"\xef\xbb\xbf"):
            warnings.append("%s: файл с BOM" % name)
        try:
            text = raw.decode("utf-8")
        except UnicodeDecodeError as e:
            errors.append("%s: не UTF-8: %s" % (name, e))
            continue
        if "�" in text:
            errors.append("%s: символ замены U+FFFD (испорченная кодировка)" % name)
        try:
            data = json.loads(text)
        except ValueError as e:
            errors.append("%s: неверный JSON: %s" % (name, e))
            continue
        if data.get("format") != FORMAT:
            errors.append("%s: format должен быть %s" % (name, FORMAT))
        if not isinstance(data.get("entries"), list):
            errors.append("%s: нет списка entries" % name)
            continue
        for e in data["entries"]:
            entries.append((name, e))

    ids = Counter(e.get("id") for _, e in entries)
    for i, n in ids.items():
        if n > 1:
            errors.append("дублирующийся id: %r (%d раз)" % (i, n))
    by_source = defaultdict(set)
    by_template = defaultdict(set)
    stats = defaultdict(Counter)
    review, todo = [], []
    for name, e in entries:
        where = "%s: %s" % (name, e.get("id"))
        if not e.get("id"):
            errors.append("%s: пустой id" % name)
        kind, status = e.get("kind"), e.get("status", "todo")
        if kind not in KINDS:
            errors.append("%s: неизвестный kind %r" % (where, kind))
        if status not in STATUSES:
            errors.append("%s: неизвестный status %r" % (where, status))
        src, tgt = e.get("source"), e.get("target") or ""
        if not isinstance(src, str) or not src.strip():
            errors.append("%s: пустой source" % where)
            continue
        if kind == "table" and (not e.get("table") or e.get("key") is None):
            errors.append("%s: у table нет table/key" % where)
        if kind in ("dialogue", "dialogue-menu") and (not isinstance(e.get("conv"), int) or not isinstance(e.get("entry"), int)):
            errors.append("%s: у реплики нет conv/entry" % where)
        if kind == "template":
            s_slots, t_slots = set(SLOT.findall(src)), set(SLOT.findall(tgt))
            if not s_slots:
                errors.append("%s: в шаблоне нет {0}" % where)
            if tgt and not t_slots <= s_slots:
                errors.append("%s: в переводе шаблона лишние слоты %s" % (where, sorted(t_slots - s_slots)))
        stats[name][status if tgt or status == "todo" else "todo"] += 1
        if status in ("translated", "review") and not tgt.strip():
            errors.append("%s: статус %s, но перевод пустой" % (where, status))
        if not tgt:
            todo.append((name, e))
            continue
        diff = markup.compare(src, tgt)
        if diff:
            (errors if status == "translated" else warnings).append("%s: разметка не совпадает: %s" % (where, diff))
        vis = visible(tgt)
        if tgt == src:
            stats[name]["kept"] += 1
        elif LOWER_WORD.search(visible(src)) and not CYR.search(vis) and status == "translated" and not e.get("note"):
            warnings.append("%s: в переводе нет кириллицы: %r" % (where, tgt[:80]))
        mixed = [w for w in MIXED_WORD.findall(vis)]
        if mixed:
            errors.append("%s: латиница и кириллица в одном слове: %s" % (where, ", ".join(mixed[:5])))
        if SPACE_BEFORE_PUNCT.search(PAUSE_CODES.sub("", tgt)) and not SPACE_BEFORE_PUNCT.search(PAUSE_CODES.sub("", src)):
            warnings.append("%s: пробел перед знаком препинания (после удаления кодов пауз)" % where)
        if tgt != tgt.strip() and src == src.strip():
            warnings.append("%s: лишние пробелы по краям перевода" % where)
        src_lines = src.replace("\r\n", "\n").replace("\r", "\n")
        tgt_lines = tgt.replace("\r\n", "\n").replace("\r", "\n")
        if src_lines.endswith("\n") != tgt_lines.endswith("\n"):
            warnings.append("%s: перевод строки в конце не совпадает" % where)
        if kind in ("text", "table"):
            by_source[src].add(tgt)
        if kind == "template":
            # Шаблоны с одинаковым оригиналом: в игре сработает первый загруженный, второй перевод не виден.
            by_template[(e.get("scope", ""), src.strip())].add(tgt.strip())
        if status == "review":
            review.append((name, e))
    for src, targets in by_source.items():
        if len(targets) > 1:
            warnings.append("разные переводы одной строки %r: %s" % (src[:60], " | ".join(sorted(t[:40] for t in targets))))
    for (scope, src), targets in by_template.items():
        if len(targets) > 1:
            warnings.append("разные переводы одного шаблона %r: %s" % (src[:60], " | ".join(sorted(t[:40] for t in targets))))
    return errors, warnings, stats, review, todo


def write_reports(stats, review, todo, errors, warnings):
    out = os.path.join(ROOT, "reports")
    os.makedirs(out, exist_ok=True)
    total = Counter()
    lines = ["# Состояние перевода", "",
             "«Без изменений» — строки, намеренно оставленные как в оригинале (имена, бренды, финские слова, коды); они входят в «Переведено».", "",
             "| Файл | Всего | Переведено | из них без изменений | На проверке | Не переведено |", "|---|---:|---:|---:|---:|---:|"]
    for name in sorted(stats):
        c = stats[name]
        n = c["translated"] + c["review"] + c["todo"]
        total.update(c)
        lines.append("| %s | %d | %d | %d | %d | %d |" % (name, n, c["translated"], c["kept"], c["review"], c["todo"]))
    n = total["translated"] + total["review"] + total["todo"]
    lines.append("| **Итого** | **%d** | **%d** | **%d** | **%d** | **%d** |" % (n, total["translated"], total["kept"], total["review"], total["todo"]))
    lines += ["", "Ошибок проверки: %d, предупреждений: %d." % (len(errors), len(warnings)), "",
              "Список строк на проверке — reports/review.tsv, непереведённых — reports/untranslated.tsv.", ""]
    with open(os.path.join(out, "report.md"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))

    def tsv(path, rows, with_note):
        with open(path, "w", encoding="utf-8", newline="\n") as f:
            f.write("file\tid\tsource\ttarget\t" + ("note" if with_note else "context") + "\n")
            for name, e in rows:
                cells = [name, e["id"], e["source"], e.get("target", ""), e.get("note", "") if with_note else e.get("context", "")]
                f.write("\t".join(c.replace("\t", " ").replace("\n", "\\n") for c in cells) + "\n")
    tsv(os.path.join(out, "review.tsv"), review, True)
    tsv(os.path.join(out, "untranslated.tsv"), todo, False)
    with open(os.path.join(out, "check.log"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(["ERROR " + e for e in errors] + ["WARN  " + w for w in warnings]) + "\n")
    return total


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser()
    ap.add_argument("--dir", default=os.path.join(ROOT, "translations"))
    ap.add_argument("--strict", action="store_true")
    args = ap.parse_args()
    errors, warnings, stats, review, todo = check(args.dir)
    total = write_reports(stats, review, todo, errors, warnings)
    for e in errors[:50]:
        print("ОШИБКА  " + e)
    for w in warnings[:30]:
        print("предупр " + w)
    if len(errors) > 50 or len(warnings) > 30:
        print("... полный список в reports/check.log")
    n = total["translated"] + total["review"] + total["todo"]
    print("строк %d: переведено %d, на проверке %d, не переведено %d; ошибок %d, предупреждений %d"
          % (n, total["translated"], total["review"], total["todo"], len(errors), len(warnings)))
    sys.exit(1 if errors or (args.strict and warnings) else 0)


if __name__ == "__main__":
    main()
