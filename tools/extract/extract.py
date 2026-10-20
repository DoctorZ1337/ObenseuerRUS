"""
Извлекает строки, которые видит игрок, из установленной игры в translations/*.json.
Игра только читается. Уже сделанные переводы сохраняются (сопоставление по id).

  python -I tools/extract/extract.py [--game "E:\\SteamLibrary\\steamapps\\common\\Obenseuer"] [--only dialogue,tables,items,ui]

Нужны UnityPy и TypeTreeGeneratorAPI: pip install -r tools/requirements.txt
Строки сцен (ui) берутся из результата scan_scenes.py (долгий проход по всем уровням).
"""
import argparse
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import catalog_io  # noqa: E402

ROOT = os.path.dirname(os.path.dirname(HERE))
OUT = os.path.join(ROOT, "translations")
LETTERS = re.compile(r"[A-Za-z]")
TABLES = ["Menu", "ToolTip", "UseText", "ItemTypes", "ObjectName", "Tasks", "Notes"]


def build_id(game):
    acf = os.path.join(os.path.dirname(os.path.dirname(game)), "appmanifest_951240.acf")
    if os.path.exists(acf):
        m = re.search(r'"buildid"\s+"(\d+)"', open(acf, encoding="utf-8", errors="replace").read())
        if m:
            return m.group(1)
    return "unknown"


def field(fields, title):
    for f in fields:
        if f["title"] == title:
            return f["value"]
    return None


def genders(game):
    """Пол персонажей из Characters.json — нужен переводчику для согласования глаголов."""
    path = os.path.join(game, "Obenseuer_Data", "StreamingAssets", "Characters.json")
    out = {}
    if os.path.exists(path):
        with open(path, encoding="utf-16") as f:
            text = f.read().strip()
        # В игре это объекты через запятую без квадратных скобок.
        if not text.startswith("["):
            text = "[" + text.rstrip(",") + "]"
        if True:
            for c in json.loads(text):
                if c.get("Name") and c.get("GenderString"):
                    out[c["Name"].strip()] = "м" if c["GenderString"] == "Male" else "ж" if c["GenderString"] == "Female" else ""
    return out


def extract_dialogue(reader, game):
    env = reader.load("sharedassets0.assets")
    db = next(reader.find_monobehaviours(env, "DialogueDatabase")).read_typetree()
    sex = genders(game)
    actors = {}
    for a in db["actors"]:
        name = field(a["fields"], "Name") or ""
        short = name.split("/")[-1]
        actors[a["id"]] = short + ("[" + sex[short] + "]" if sex.get(short) else "")
    groups = {}
    for conv in db["conversations"]:
        title = field(conv["fields"], "Title") or ""
        group = (title.split("/")[0] or "misc").strip().lower().replace(" ", "-")
        for e in conv["dialogueEntries"]:
            actor = field(e["fields"], "Actor")
            speaker = actors.get(int(actor), "?") if actor and actor.lstrip("-").isdigit() else "?"
            ctx = "%s | %s" % (title, speaker)
            text = field(e["fields"], "Dialogue Text") or ""
            menu = field(e["fields"], "Menu Text") or ""
            if LETTERS.search(text):
                groups.setdefault(group, []).append({"id": "dlg/%d/%d" % (conv["id"], e["id"]), "kind": "dialogue",
                                                     "conv": conv["id"], "entry": e["id"], "source": text,
                                                     "target": "", "status": "todo", "context": ctx})
            if LETTERS.search(menu):
                groups.setdefault(group, []).append({"id": "dlg/%d/%d/menu" % (conv["id"], e["id"]), "kind": "dialogue-menu",
                                                     "conv": conv["id"], "entry": e["id"], "source": menu,
                                                     "target": "", "status": "todo", "context": ctx + " | вариант ответа"})
    return {"dialogue-" + g: v for g, v in groups.items()}


def extract_tables(reader):
    tables = reader.string_tables(("english(en)", "finnish(fi)"))
    out = []
    for name in TABLES:
        for row in tables.get(name, []):
            en = row.get("en") or ""
            if not LETTERS.search(en):
                continue
            ctx = "Unity Localization, таблица %s, ключ %s" % (name, row["key"])
            if row.get("fi") and row["fi"] != en:
                ctx += " | fi: " + row["fi"]
            out.append({"id": "table/%s/%s" % (name, row["key"]), "kind": "table", "table": name, "key": row["key"],
                        "source": en, "target": "", "status": "todo", "context": ctx})
    return {"tables": out}


def extract_items(game):
    path = os.path.join(game, "Obenseuer_Data", "StreamingAssets", "Items.json")
    with open(path, encoding="utf-8-sig") as f:
        items = json.load(f)
    out, seen = [], {}
    for it in items:
        cats = ", ".join(it.get("Categories") or [])
        for part, value in (("title", it.get("Title") or ""), ("desc", it.get("Description") or "")):
            value_key = value
            if not LETTERS.search(value):
                continue
            if value_key in seen:
                seen[value_key]["context"] += "; #%d" % it["ID"]
                continue
            e = {"id": "item/%d/%s" % (it["ID"], part), "kind": "text", "source": value, "target": "", "status": "todo",
                 "context": ("Название предмета #%d" if part == "title" else "Описание предмета #%d") % it["ID"]
                 + (" (%s)" % cats if cats else "") + (" | название: " + it["Title"] if part == "desc" else "")}
            seen[value_key] = e
            out.append(e)
    return {"items": out}


def extract_ui(scan_path):
    """Строки сцен и префабов из результата scan_scenes.py, отобранные правилами ui_rules.py."""
    import ui_rules
    with open(scan_path, encoding="utf-8") as f:
        scan = json.load(f)["strings"]
    out = []
    for value, places in sorted(scan.items()):
        fields = sorted(places)
        if not ui_rules.is_player_visible(value, fields):
            continue
        out.append({"id": "ui/" + ui_rules.stable_id(value), "kind": "text", "source": value, "target": "",
                    "status": "todo", "context": "; ".join(fields[:4])})
    return {"ui": out}


PRIORITY = ["tables", "items", "ui", "code"]


def dedupe_dir():
    """Одна строка интерфейса — один перевод: непереведённые дубли из менее приоритетных файлов
    и дубли строк, уже переведённых в любом файле, убираются."""
    seen = {}
    for name in PRIORITY:
        path = os.path.join(OUT, name + ".json")
        if os.path.exists(path):
            for e in catalog_io.load(path).values():
                if e["kind"] in ("text", "table") and e.get("target"):
                    seen.setdefault(e["source"], name)
    for name in PRIORITY:
        path = os.path.join(OUT, name + ".json")
        if not os.path.exists(path):
            continue
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        kept, dropped = [], 0
        for e in data["entries"]:
            if e["kind"] == "text" and e["source"] in seen and not e.get("target"):
                dropped += 1
                continue
            if e["kind"] in ("text", "table"):
                seen.setdefault(e["source"], name)
            kept.append(e)
        if dropped:
            catalog_io.write(path, data["category"], kept, data.get("gameBuild", "unknown"))
            print("%-28s убрано дублей: %d" % (name, dropped))


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--game")
    ap.add_argument("--only", default="dialogue,tables,items,ui")
    ap.add_argument("--scan", default=os.path.join(ROOT, "build", "scene_strings.json"))
    args = ap.parse_args()
    from unity_read import Reader, game_dir
    game = game_dir(args.game)
    build = build_id(game)
    reader = Reader(game)
    parts = set(args.only.split(","))
    files = {}
    if "dialogue" in parts:
        files.update(extract_dialogue(reader, game))
    if "tables" in parts:
        files.update(extract_tables(reader))
    if "items" in parts:
        files.update(extract_items(game))
    if "ui" in parts and os.path.exists(args.scan):
        files.update(extract_ui(args.scan))
    os.makedirs(OUT, exist_ok=True)
    for name, entries in sorted(files.items()):
        path = os.path.join(OUT, name + ".json")
        stats = catalog_io.merge(entries, catalog_io.load(path))
        catalog_io.write(path, name, entries, build)
        print("%-28s %6d строк  (сохранено переводов %d, изменилось %d, новых %d)" % (name, len(entries), stats["kept"], stats["changed"], stats["new"]))
    dedupe_dir()


if __name__ == "__main__":
    main()
