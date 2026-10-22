"""Чтение и запись translations/*.json (формат obenseuer-rus/1) с сохранением уже сделанных переводов."""
import json
import os

FORMAT = "obenseuer-rus/1"
FIELD_ORDER = ["id", "kind", "scope", "table", "key", "conv", "entry", "source", "target", "status", "context", "note"]


def load(path):
    if not os.path.exists(path):
        return {}
    with open(path, encoding="utf-8") as f:
        data = json.load(f)
    return {e["id"]: e for e in data.get("entries", [])}


def load_dir(folder):
    out = {}
    for name in sorted(os.listdir(folder)):
        if name.endswith(".json"):
            out.update(load(os.path.join(folder, name)))
    return out


def ordered(e):
    return {k: e[k] for k in FIELD_ORDER if k in e and e[k] is not None}


def write(path, category, entries, game_build):
    lines = ["{",
             '  "format": %s,' % json.dumps(FORMAT),
             '  "category": %s,' % json.dumps(category, ensure_ascii=False),
             '  "sourceLanguage": "en",',
             '  "targetLanguage": "ru",',
             '  "gameBuild": %s,' % json.dumps(game_build),
             '  "entries": [']
    body = [json.dumps(ordered(e), ensure_ascii=False) for e in entries]
    lines.append(",\n".join("    " + b for b in body))
    lines.append("  ]")
    lines.append("}")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines) + "\n")


def merge(new_entries, old):
    """Переносит target/status/note из старых записей. Если оригинал поменялся — статус review."""
    stats = {"kept": 0, "changed": 0, "new": 0}
    for e in new_entries:
        prev = old.get(e["id"])
        if prev is None:
            stats["new"] += 1
            continue
        e["target"] = prev.get("target", "")
        e["status"] = prev.get("status", "todo")
        if prev.get("note"):
            e["note"] = prev["note"]
        # scope, заданный вручную (например, перевод одного объекта), переживает повторное извлечение.
        if prev.get("scope") and not e.get("scope"):
            e["scope"] = prev["scope"]
        if prev.get("source") != e["source"] and e["target"]:
            e["status"] = "review"
            e["note"] = "оригинал изменился, был: " + prev.get("source", "")
            stats["changed"] += 1
        else:
            stats["kept"] += 1
    return stats
