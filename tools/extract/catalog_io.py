"""Чтение и запись translations/*.json (формат obenseuer-rus/1) с сохранением уже сделанных переводов."""
import difflib
import json
import os
import re

FORMAT = "obenseuer-rus/1"
# Пометка записи, у которой после обновления игры изменился оригинал (статус review): дальше идёт прежний оригинал.
CHANGED = "оригинал изменился, был: "
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


def norm(text):
    """Текст без регистра, артиклей, дефисов и знаков: так выглядят правки грамматики в игре ("It's a rat meat" -> "It's rat meat")."""
    s = re.sub(r"\b(a|an|the)\b", " ", (text or "").lower().replace("’", "'"))
    return " ".join(re.sub(r"[\W_]+", " ", s).split())


def same_meaning(a, b):
    """Правка без смены слов, вопроса или восклицания: перевод остаётся верным."""
    return norm(a) == norm(b) and sorted(re.findall(r"[!?]", a)) == sorted(re.findall(r"[!?]", b))


def _group(kind):
    return "template" if kind == "template" else "text"


class Pool:
    """
    Переведённые строки интерфейса без scope — для новых записей, у которых нет старой записи с тем же id:
    у ui/ и code/ id зависит от текста, поэтому правка текста в игре даёт новый id. Берётся строка с тем же текстом
    без учёта грамматики, иначе самая похожая (от 90%).
    """

    def __init__(self, entries):
        self.by_norm, self.items, self.sources = {}, [], set()
        for e in entries:
            if e.get("target") and e.get("status") != "todo" and not e.get("scope") and e.get("kind") in ("text", "table", "template"):
                self.by_norm.setdefault((_group(e["kind"]), norm(e["source"])), e)
                self.items.append(e)
                self.sources.add(e["source"])

    def find(self, e):
        # Строка, уже переведённая слово в слово в другом файле, остаётся без перевода: её уберёт extract.dedupe_dir.
        if e.get("kind") not in ("text", "template") or e.get("scope") or e["source"] in self.sources:
            return None
        hit = self.by_norm.get((_group(e["kind"]), norm(e["source"])))
        if hit:
            return hit
        src, best, score = e["source"], None, 0.9
        for c in self.items:
            if _group(c["kind"]) != _group(e["kind"]) or abs(len(c["source"]) - len(src)) > len(src) // 5 + 5:
                continue
            m = difflib.SequenceMatcher(None, c["source"], src, autojunk=False)
            if m.real_quick_ratio() >= score and m.quick_ratio() >= score and m.ratio() >= score:
                best, score = c, m.ratio()
        return best


def merge(new_entries, old, pool=None):
    """
    Переносит target/status/note из старых записей (по id, иначе из pool по тексту).
    Если оригинал поменялся — статус review; правка только грамматики (same_meaning) статус не меняет.
    """
    stats = {"kept": 0, "changed": 0, "new": 0}
    for e in new_entries:
        prev = old.get(e["id"])
        if prev is None and pool is not None:
            prev = pool.find(e)
            if prev is not None:
                prev = {k: v for k, v in prev.items() if k != "scope"}
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
        if prev.get("source") != e["source"] and e["target"] and not same_meaning(prev.get("source"), e["source"]):
            e["status"] = "review"
            e["note"] = CHANGED + prev.get("source", "")
            stats["changed"] += 1
        else:
            stats["kept"] += 1
    return stats
