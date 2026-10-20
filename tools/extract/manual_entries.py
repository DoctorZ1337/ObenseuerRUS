"""
Строки, которые игра собирает так, что автоматически их не извлечь: "Taste: " + "Good",
подсказки клавиш карточных игр, значения Lua-вставок (TellTimeResponse и др.).
Источник — декомпилированный код игры (ToolTip.cs, CardGameUI.cs, TellTime.cs, Crime.cs, Bank.cs).
Запуск пересоздаёт translations/manual.json; переводы задаются здесь же.
"""
import os
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
sys.path.insert(0, HERE)
import catalog_io  # noqa: E402

TASTE = {"Fantastic": "фантастический", "Good": "хороший", "Okay": "нормальный", "Terrible": "ужасный",
         "Bad": "плохой", "Poor": "слабый", "Bland": "пресный"}
EFFICACY = {"Fantastic": "фантастическая", "Fine": "отличная", "Good": "хорошая", "Okay": "нормальная",
            "Passable": "сносная", "Poor": "слабая", "None": "нет"}
QUALITY = {"Bad": "плохое", "Good": "хорошее"}
CARD_KEYS = {"Request Card": "Попросить карту", "Sort Cards": "Сортировать карты", "Call Ready": "Объявить готовность",
             "Pickup Pile": "Забрать стопку", "Risk Draw": "Рискнуть взять карту", "Pass Turn": "Пасовать",
             "Show Hints": "Показать подсказки", "Hold to View Table": "Удерживайте, чтобы осмотреть стол",
             "Hold to Leave": "Удерживайте, чтобы уйти"}
DAYS = {"Monday": "Понедельник", "Tuesday": "Вторник", "Wednesday": "Среда", "Thursday": "Четверг",
        "Friday": "Пятница", "Saturday": "Суббота", "Sunday": "Воскресенье"}


def entries():
    out = []

    def text(i, s, t, ctx):
        out.append({"id": "manual/" + i, "kind": "text", "source": s, "target": t, "status": "translated", "context": ctx})

    def tpl(i, s, t, ctx, scope=None):
        e = {"id": "manual/" + i, "kind": "template", "source": s, "target": t, "status": "translated", "context": ctx}
        if scope:
            e["scope"] = scope
        out.append(e)

    for k, v in TASTE.items():
        text("taste/" + k, "Taste: " + k, "Вкус: " + v, "ToolTip.MentalHealthConsumableDataToString")
    for k, v in EFFICACY.items():
        text("efficacy/" + k, "Efficacy: " + k, "Эффективность: " + v, "ToolTip.MentalHealthMedicineDataToString")
    for k, v in QUALITY.items():
        text("quality/" + k, "Quality: " + k, "Качество: " + v, "ToolTip.QualityDataToString")
    for k, v in CARD_KEYS.items():
        tpl("cards/" + k, k + " ({0})", v + " ({0})", "CardGameUI: строка подсказки с клавишей")
    for k, v in DAYS.items():
        tpl("time/at/" + k, k + " at {0}", v + ", {0}", "Lua TellTimeResponse (варианты 0–1)", "lua")
        tpl("time/day/" + k, k + " {0}", v + ", {0}", "Lua TellTimeResponse (вариант 3)", "lua")
        tpl("time/oclock/" + k, "It's {0} o'clock at " + k.lower(), "Сейчас {0} ч, " + v.lower(), "Lua TellTimeResponse (вариант 2)", "lua")
    tpl("time/clock", "Clock is {0}", "На часах {0}", "Lua TellTimeResponse (варианты 4–5)", "lua")
    tpl("time/now", "Now it's {0}", "Сейчас {0}", "Lua TellTimeResponse (варианты 6–7)", "lua")
    tpl("crime/hours", "{0}h", "{0} ч", "Lua GetSentenceLengthPreArrest", "lua")
    tpl("bank/loan", "{0} with a payment time of {1} months and an interest of {2}%",
        "{0} со сроком выплаты {1} мес. и ставкой {2}%", "Lua GetLoanDetailsDialogue", "lua")
    # Заключение врача о зависимостях (TreatmentAddictions.cs) собирается из этих частей; списки — через " and ".
    tpl("addiction/withdrawal", "Your {0} addiction widthdrawal symptoms are about to start soon", "Скоро начнётся ломка: {0}", "TreatmentAddictions")
    tpl("addiction/high", "You're very addicted to {0}", "Сильная зависимость: {0}", "TreatmentAddictions")
    tpl("addiction/moderate", "You have a moderate {0} addiction", "Умеренная зависимость: {0}", "TreatmentAddictions")
    tpl("addiction/normal", "You have {0}", "У вас {0}", "TreatmentAddictions")
    tpl("list/and", "{0} and {1}", "{0} и {1}", "перечисление из двух частей (TreatmentAddictions и др.)")
    # Слоты сохранений (LoadMenu.GetSlotDisplayName): между словом и номером неразрывный пробел, как в игре.
    tpl("save/slot", "Slot {0}", "Слот {0}", "LoadMenu: название слота")
    tpl("save/autosave", "Autosave {0}", "Автосохр. {0}", "LoadMenu: название слота автосохранения")
    tpl("save/corrupted", "Corrupted: {0}", "Повреждено: {0}", "LoadMenu: панель сведений о слоте")
    return out


def main():
    path = os.path.join(ROOT, "translations", "manual.json")
    catalog_io.write(path, "manual", entries(), "25969064")
    print("manual.json:", len(entries()))


if __name__ == "__main__":
    main()
