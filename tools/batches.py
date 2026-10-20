"""
Пакеты для перевода: выгрузка непереведённых строк в компактный текст и загрузка переводов обратно.

  python -I tools/batches.py export dialogue-bazaar --words 3500
      -> build/batches/dialogue-bazaar-001.txt (+ .map.json)
  python -I tools/batches.py import build/batches/dialogue-bazaar-001.txt build/batches/dialogue-bazaar-001.ru.txt

Формат выгрузки:  N|говорящий|оригинал        (перевод строки в тексте показан как ¶)
Формат перевода:  N|перевод                    -> status "translated"
                  N?|перевод|причина            -> status "review" с пометкой
                  N!|текст|пояснение            -> status "translated" с пояснением (например, имя без перевода)
                  N|=                           -> оставить как в оригинале (имена, таблички с фамилиями)
Одинаковые строки одного говорящего внутри файла выгружаются один раз и получают один перевод.
"""
import argparse
import json
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
sys.path.insert(0, os.path.join(HERE, "extract"))
sys.path.insert(0, HERE)
import catalog_io  # noqa: E402
import markup  # noqa: E402

TR = os.path.join(ROOT, "translations")
OUT = os.path.join(ROOT, "build", "batches")
NL = "¶"


def read_catalog(name):
    path = os.path.join(TR, name + ".json")
    with open(path, encoding="utf-8") as f:
        return path, json.load(f)


def speaker_of(e):
    ctx = e.get("context") or ""
    parts = [p.strip() for p in ctx.split("|")]
    sp = parts[1] if len(parts) > 1 else ""
    return "P" if sp == "Player" else sp


def export(name, words, statuses):
    path, data = read_catalog(name)
    os.makedirs(OUT, exist_ok=True)
    seen = {}
    batches, cur, cur_words, last_conv = [], [], 0, None
    for e in data["entries"]:
        if e.get("status", "todo") not in statuses:
            continue
        sp = speaker_of(e) if e["kind"].startswith("dialogue") else ""
        key = (e["source"], sp, e["kind"])
        if key in seen:
            seen[key]["ids"].append(e["id"])
            continue
        conv = e.get("conv")
        if cur_words >= words and conv != last_conv:
            batches.append(cur)
            cur, cur_words = [], 0
        item = {"ids": [e["id"]], "e": e, "sp": sp}
        seen[key] = item
        cur.append(item)
        cur_words += len(e["source"].split())
        last_conv = conv
    if cur:
        batches.append(cur)
    written = []
    for i, items in enumerate(batches, 1):
        base = os.path.join(OUT, "%s-%03d" % (name, i))
        lines, mapping, conv = [], {}, None
        for n, item in enumerate(items, 1):
            e = item["e"]
            if e["kind"].startswith("dialogue") and e.get("conv") != conv:
                conv = e.get("conv")
                lines.append("# conv %s: %s" % (conv, (e.get("context") or "").split("|")[0].strip()))
            elif not e["kind"].startswith("dialogue"):
                lines.append("# " + (e.get("context") or "")[:160].replace("\n", " "))
            label = item["sp"] or ""
            if e["kind"] == "dialogue-menu":
                label += " [ответ]"
            # \r иначе разрывает строку пакета при чтении; в переводе достаточно \n.
            lines.append("%d|%s|%s" % (n, label, e["source"].replace("\r\n", "\n").replace("\r", "\n").replace("\n", NL)))
            mapping[str(n)] = item["ids"]
        with open(base + ".txt", "w", encoding="utf-8", newline="\n") as f:
            f.write("\n".join(lines) + "\n")
        with open(base + ".map.json", "w", encoding="utf-8") as f:
            json.dump({"catalog": name, "map": mapping}, f, ensure_ascii=False)
        written.append(base + ".txt")
    print("%s: %d пакетов, %d уникальных строк" % (name, len(batches), sum(len(b) for b in batches)))
    for w in written:
        print("  " + os.path.relpath(w, ROOT))


def parse_translation(path):
    out = {}
    with open(path, encoding="utf-8") as f:
        for raw in f:
            line = raw.rstrip("\n").rstrip("\r")
            if not line.strip() or line.startswith("#"):
                continue
            num, _, rest = line.partition("|")
            review = num.endswith("?")
            keep = num.endswith("!")
            num = num.rstrip("?!").strip()
            note = ""
            if (review or keep) and "|" in rest:
                rest, _, note = rest.rpartition("|")
            out[num] = (rest.replace(NL, "\n"), review, note.strip())
    return out


def do_import(batch, translation):
    with open(batch[:-4] + ".map.json", encoding="utf-8") as f:
        meta = json.load(f)
    path, data = read_catalog(meta["catalog"])
    by_id = {e["id"]: e for e in data["entries"]}
    tr = parse_translation(translation)
    done, problems, missing = 0, [], []
    for num, ids in meta["map"].items():
        if num not in tr:
            missing.append(num)
            continue
        target, review, note = tr[num]
        src = by_id[ids[0]]["source"]
        if target == "=":
            target = src
        diff = markup.compare(src, target)
        if diff:
            problems.append("%s: разметка не совпадает: %s" % (num, diff))
            review, note = True, (note + "; " if note else "") + "разметка: " + diff
        for i in ids:
            e = by_id[i]
            e["target"] = target
            e["status"] = "review" if review else "translated"
            if note:
                e["note"] = note
            elif "note" in e:
                del e["note"]
            done += 1
    if missing:
        problems.append("нет перевода для %d строк: %s" % (len(missing), ", ".join(missing[:10]) + (" ..." if len(missing) > 10 else "")))
    extra = set(tr) - set(meta["map"])
    if extra:
        problems.append("лишние номера: " + ", ".join(sorted(extra, key=lambda x: int(x) if x.isdigit() else 0)))
    catalog_io.write(path, data["category"], data["entries"], data.get("gameBuild", "unknown"))
    print("%s: записано %d строк" % (meta["catalog"], done))
    for p in problems:
        print("  ! " + p)
    return not problems


def main():
    sys.stdout.reconfigure(encoding="utf-8")
    ap = argparse.ArgumentParser()
    sub = ap.add_subparsers(dest="cmd", required=True)
    e = sub.add_parser("export")
    e.add_argument("catalog")
    e.add_argument("--words", type=int, default=3500)
    e.add_argument("--status", default="todo")
    i = sub.add_parser("import")
    i.add_argument("batch")
    i.add_argument("translation")
    args = ap.parse_args()
    if args.cmd == "export":
        export(args.catalog, args.words, set(args.status.split(",")))
    else:
        sys.exit(0 if do_import(args.batch, args.translation) else 1)


if __name__ == "__main__":
    main()
