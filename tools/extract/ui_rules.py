"""Какие строки из сцен и префабов (build/scene_strings.json) игрок видит на экране.

Поле задаётся как "Класс:путь". Берём только поля с показываемым текстом; ссылки на объекты (*.name),
GUID, пути ассетов, имена анимаций и методов отбрасываются. Имена людей переводить не нужно (остаются латиницей),
поэтому заголовки интерактивных NPC тоже не берутся.
"""
import hashlib
import re

# Последний сегмент пути поля -> показываемый текст.
VISIBLE_LEAVES = {
    "m_text", "m_Text", "title", "description", "content", "hintText", "statName",
    "m_useMessage", "m_overrideUseMessage", "m_overrideName", "UseMessage", "ManuName",
    "roomName", "marketName", "customTitle", "itemName", "rewardName",
}
# Классы, у которых эти поля служебные или содержат имена людей.
SKIP_CLASSES = {
    "AzureWeatherProfile", "Soundscape", "InteractableTalk", "InteractableRelay", "Character", "NPCInfo",
    "TenementResident", "GUIButton", "logic_state",
}
# Отдельные поля вне правила выше.
EXTRA_FIELDS = {"SelectionButton:Options[].Name", "StartSelection:selections[].title"}
PLACEHOLDERS = {"new text", "text", "label", "button", "placeholder", "name", "title", "test", "description", "trigger", "use"}
GUID = re.compile(r"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$", re.I)
IDENT = re.compile(r"^[A-Za-z0-9_.\-]+$")
WORD = re.compile(r"[A-Za-z]{2,}")
MOCKUP = re.compile(r"(?i)lorem ipsum|some name|(\w)\1{9,}|9999|99:99")


def field_visible(field):
    if field in EXTRA_FIELDS:
        return True
    cls, _, path = field.partition(":")
    leaf = path.rsplit(".", 1)[-1].replace("[]", "")
    return cls not in SKIP_CLASSES and leaf in VISIBLE_LEAVES


def value_visible(value):
    v = value.strip()
    if not WORD.search(v) or v.lower() in PLACEHOLDERS or GUID.match(v):
        return False
    if "Assets/" in v or v.endswith((".asset", ".prefab", ".png")):
        return False
    # Заглушки из макетов интерфейса: игра заменяет их своим текстом при показе.
    if MOCKUP.search(v):
        return False
    # Одно слово с _ или CamelCase внутри — идентификатор, а не текст.
    if IDENT.match(v) and ("_" in v or re.search(r"[a-z][A-Z]", v)):
        return False
    return True


def is_player_visible(value, fields):
    return value_visible(value) and any(field_visible(f) for f in fields)


def stable_id(value):
    return hashlib.sha1(value.encode("utf-8")).hexdigest()[:12]


if __name__ == "__main__":
    assert field_visible("TextMeshProUGUI:m_text") and field_visible("LoadingScreen:hintList[].hintText")
    assert not field_visible("CollectibleItem:owner.name") and not field_visible("InteractableTalk:title")
    assert value_visible("Start five renovations") and not value_visible("UnderMapStart")
    assert not value_visible("c90f859d-03a9-4811-a5a7-ca472a65316b") and not value_visible("New Text")
    assert not value_visible("Generic_Prison_guard") and value_visible("O-Market")
    assert not value_visible("BBBBBBBBBBBBBBBBBBB") and not value_visible("Autosave 1: 99/99/9999 99:99:99")
    print("ok")
