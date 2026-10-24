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

    def add(eid, value, context, scope=None, ref=""):
        if not LETTERS.search(value):
            return
        key = (scope, value)
        if key in seen:
            if ref:
                seen[key]["context"] += "; " + ref
            return
        e = {"id": eid, "kind": "text", "source": value, "target": "", "status": "todo", "context": context}
        if scope:
            e["scope"] = scope
        seen[key] = e
        out.append(e)

    for it in items:
        cats = ", ".join(it.get("Categories") or [])
        ref = "#%d" % it["ID"]
        add("item/%d/title" % it["ID"], it.get("Title") or "", "Название предмета %s" % ref + (" (%s)" % cats if cats else ""), ref=ref)
        add("item/%d/desc" % it["ID"], it.get("Description") or "",
            "Описание предмета %s" % ref + (" (%s)" % cats if cats else "") + " | название: " + (it.get("Title") or ""), ref=ref)
        # Вариант внешнего вида (обои, флажки): Item.GetItemTitle показывает его вместо названия.
        for i, skin in enumerate((it.get("Appearance") or {}).get("SkinNames") or []):
            add("item/%d/skin/%d" % (it["ID"], i), skin, "Вариант внешнего вида предмета %s | название: %s" % (ref, it.get("Title") or ""), ref=ref)
        for a in it.get("Actions") or []:
            # Глагол на кнопке окна предмета и в подсказке "(Hold - Drink)"; scope action — "Drink" здесь не тип "Напиток".
            add("item/action/" + (a.get("Name") or ""), a.get("Name") or "",
                "Действие предмета: кнопка окна предмета (ItemInfoPanel) и подсказка «(Hold - …)» (CollectibleItem) | " + ref,
                scope="action", ref=ref)
            add("item/no-required/" + (a.get("NoRequiredItems") or ""), a.get("NoRequiredItems") or "",
                "Кнопка окна предмета, когда для действия нет нужного предмета (NoRequiredItems) | " + ref, ref=ref)
    # "Accepts: Gasmask Filter" (ToolTip.AmmoDataToString) — имя ссылки на предмет, а не его название;
    # отдельным проходом, чтобы совпадающие с названиями предметов уже были записаны выше.
    for it in items:
        for m in it.get("Meta") or []:
            refs = m.get("ammoItems")
            refs = refs.get("$content") if isinstance(refs, dict) else refs
            for r in refs or []:
                add("item/accepts/" + (r.get("name") or ""), r.get("name") or "",
                    "Подсказка предмета: «Accepts: …» (ToolTip.AmmoDataToString) | #%d" % it["ID"])
    # Названия рецептов на кнопках станков (CraftingBase). Совпадающие с названием предмета уже есть выше.
    path = os.path.join(game, "Obenseuer_Data", "StreamingAssets", "Recipes.json")
    with open(path, encoding="utf-16") as f:
        recipes = json.load(f)["Recipes"]
    titles = {e["source"].strip().lower() for e in out}
    for r in recipes:
        name = r.get("Name") or ""
        if name.strip().lower() in titles:
            continue
        add("recipe/" + ui_id(name), name, "Название рецепта (кнопка станка), тип %s, навык %s" % (r.get("Type"), r.get("RelevantSkill")))
    return {"items": out}


def ui_id(value):
    import ui_rules
    return ui_rules.stable_id(value)


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
                if e["kind"] in ("text", "table") and e.get("target") and not e.get("scope"):
                    seen.setdefault(e["source"], name)
    for name in PRIORITY:
        path = os.path.join(OUT, name + ".json")
        if not os.path.exists(path):
            continue
        with open(path, encoding="utf-8") as f:
            data = json.load(f)
        kept, dropped = [], 0
        for e in data["entries"]:
            # Строка со scope нужна именно потому, что общий перевод того же текста здесь не подходит.
            if e.get("scope"):
                kept.append(e)
                continue
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
    pool = catalog_io.Pool(catalog_io.load_dir(OUT).values())
    for name, entries in sorted(files.items()):
        path = os.path.join(OUT, name + ".json")
        stats = catalog_io.merge(entries, catalog_io.load(path), pool)
        catalog_io.write(path, name, entries, build)
        print("%-28s %6d строк  (сохранено переводов %d, изменилось %d, новых %d)" % (name, len(entries), stats["kept"], stats["changed"], stats["new"]))
    dedupe_dir()


if __name__ == "__main__":
    main()
