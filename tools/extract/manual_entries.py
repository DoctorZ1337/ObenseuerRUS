"""
Строки, которые игра собирает так, что автоматически их не извлечь: "Taste: " + "Good",
подсказки клавиш карточных игр, значения Lua-вставок (TellTimeResponse и др.), модификаторы станков,
удобство кровати, заголовки счетов, названия навыков (enum.ToString()).
Источник — декомпилированный код игры (ToolTip.cs, ModifierUI.cs, Sleep.cs, Item.cs, InvoiceController.cs,
PlayerStats.cs, PlayerSkillIncreaseUI.cs, CardGameUI.cs, TellTime.cs, Crime.cs, Bank.cs), а также названия из объектов,
которые UnityPy не читает (build/raw_strings.json от tools/extract/scan_raw.py).
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
# RecipeCondition — модификатор станка (ModifierUI: "Modifier: Clean"); мужской род, как у слова «модификатор».
MODIFIERS = {"None": "нет", "Clean": "чистый", "Dirty": "грязный", "Infested": "заражённый", "Makeshift": "самодельный",
             "Crude": "грубый", "Normal": "обычный", "Upgraded": "улучшенный", "Advanced": "продвинутый",
             "Industrial": "промышленный", "Special": "особый", "Wasteland": "пустошь", "Water": "вода",
             "Electricity": "электричество", "Steam": "пар", "Sun": "солнце", "Moist": "влажный", "Large": "большой",
             "Small": "маленький"}
# Те же модификаторы без "Modifier: " — список рецепта (RecipeInfoUI: "Excluded modifiers:\nMakeshift, crude.";
# со второго слова игра пишет строчными, переводчик сохраняет регистр). Clean, None, Normal, Special, Water,
# Electricity и Small уже переведены в других местах тем же или близким словом.
CONDITIONS = {"Dirty": "Грязный", "Infested": "Заражённый", "Makeshift": "Самодельный", "Crude": "Грубый",
              "Upgraded": "Улучшенный", "Advanced": "Продвинутый", "Industrial": "Промышленный", "Wasteland": "Пустошь",
              "Steam": "Пар", "Sun": "Солнце", "Moist": "Влажный", "Large": "Большой"}
# Уровни улучшений доходного дома (UpgradeType.*: TenementUpgrade.GetName возвращает имя значения): мужской род,
# как у слова «уровень». Строчными игра пишет их в названиях задач ремонта ("Apt 1 shabby bathroom").
LEVELS = {"None": "Нет", "Bare": "Голый", "Makeshift": "Самодельный", "Shabby": "Убогий", "Decent": "Приличный",
          "Fine": "Неплохой", "Luxurious": "Роскошный", "Off": "Выкл.", "On": "Вкл.", "Dirty": "Грязный", "Good": "Хороший",
          "Best": "Лучший", "Shit": "Дерьмо", "Central": "Центральный", "District": "Районный", "Small": "Маленький",
          "Medium": "Средний", "Large": "Большой", "Huge": "Огромный"}
# Уже переведены в других местах (таблицы, StoragePanelUI) — отдельные строки не нужны.
LEVELS_ELSEWHERE = {"None", "Makeshift", "Decent", "Fine", "Off", "On", "Dirty", "Good", "Shit", "Small", "Medium", "Large"}
# Подписи собеседников, которые не являются именами (имена людей — в names-people.json): заголовки InteractableTalk
# и имена актёров Dialogue System над репликой (игра показывает часть после последнего "/": "Start/Interviewer").
TALK_TITLES = {"Hole": "Дыра", "Interviewers": "Интервьюеры", "Max's evil door": "Злая дверь Max", "Newsreader": "Диктор",
               "Pizza Hatch": "Окошко пиццерии", "Priest": "Священник", "Tape recorder": "Магнитофон",
               "Warden": "Надзиратель", "Woman in bed": "Женщина в кровати",
               "Interviewer": "Интервьюер", "Prison guard": "Тюремный охранник", "Automated message": "Автоматическое сообщение",
               "Emergency services": "Экстренные службы",
               "Hobbyprof Publishing Customer Service": "Служба поддержки Hobbyprof Publishing",
               "Osmo Olut customer service": "Служба поддержки Osmo Olut",
               "Stalburg Bank customer service": "Служба поддержки банка Stalburg",
               "Swamp Publishing customer service": "Служба поддержки Swamp Publishing",
               "Hazmat Guy": "Человек в химзащите", "Patient": "Пациент", "Shadow woman": "Женщина-тень",
               "Shadow Burnt Apartment": "Тень из сгоревшей квартиры", "Shadow Lagerfeld 1": "Тень Lagerfeld 1",
               "Shadow Lagerfeld 2": "Тень Lagerfeld 2", "Shadow Lagerfeld 3": "Тень Lagerfeld 3"}
# Sleep.GetTitle: удобство кровати и безопасность места.
COMFORT = {"Torturous": "мучительно", "Aching": "всё ломит", "Uncomfortable": "неудобно", "Okay": "сносно",
           "Pleasant": "приятно", "Relaxing": "расслабляет", "Comfy": "уютно"}
SAFETY = {"Unsafe": "опасно", "Safeish": "относительно безопасно"}
SKILLS = {"Lockpicking": "Взлом замков", "Drinking": "Выпивка", "Cooking": "Готовка", "Mashing": "Затирание",
          "Brewing": "Пивоварение", "Distilling": "Дистилляция", "Farming": "Фермерство", "Malting": "Соложение",
          "Manufacturing": "Производство", "Paperwork": "Бумажная работа", "Chemistry": "Химия",
          "Carpentry": "Плотничество", "Machining": "Станочное дело", "Engineering": "Инженерия", "Bribery": "Подкуп",
          "Cleaning": "Чистка", "Sewing": "Шитьё", "Electronics": "Электроника", "Mining": "Горное дело",
          "Music": "Музыка", "Smithing": "Кузнечное дело", "Medical": "Медицина", "Computers": "Компьютеры",
          "Sauna": "Сауна"}
# Видимые названия из объектов, которые не читает UnityPy (build/raw_strings.json, tools/extract/scan_raw.py).
RAW_TITLES = {
    # RandommailSender.randomInvoiceNames — кому выставлен случайный счёт ("Invoice - <имя>: ...").
    "invoice": {
        "Chad's Chadlery Service": "Услуги Chadlery от Chad", "Pertti's Suprise Invoice": "Внезапный счёт от Pertti",
        "Jasmine's Support Fund": "Фонд поддержки Jasmine",
        "Stalburg Phonebook Service Listing": "Размещение в телефонном справочнике Stalburg",
        "Orphaned Cats and Dogs Charity": "Фонд помощи бездомным кошкам и собакам", "Davidcorp": "Davidcorp",
        "Junk Subscription": "Мусорная подписка", "Ellie's Social Media Subscription": "Подписка на соцсети Ellie",
        "Only Bans": "Only Bans", "Blackmail Letter": "Письмо с шантажом", "Not Taxes": "Не налоги",
        "Help Me!": "Помогите!", "I want to buy a new car, so here's an invoice.": "Хочу купить новую машину, так что вот счёт.",
        "Jonne's Beer and Lonkero Fund": "Фонд Jonne на пиво и Lonkero",
        "Cybersecurity Protection Renewal": "Продление защиты от киберугроз", "Just a Scam": "Просто развод",
        "Hello, I though you could help me to achieve my dreams.": "Здравствуйте, мне показалось, что вы поможете мне исполнить мечту.",
        "Suprise Invoice": "Внезапный счёт", "Veeti's OS Energy Drink Fund": "Фонд Veeti на энергетик OS",
        "People's Manatee": "Народный ламантин", "Lund’s Bridge Maintenance Tax": "Налог Lund на содержание моста",
        "Tenement Association Membership Fee": "Членский взнос ассоциации доходного дома",
        "Manly Max’s Manly Support Fee": "Мужской взнос поддержки от Manly Max",
        "Honey, I need money to get to Obenseuer!": "Солнышко, мне нужны деньги, чтобы добраться до Obenseuer!",
        "Money! NOW!": "Деньги! НЕМЕДЛЕННО!", "Cryptocurrency Investment": "Инвестиции в криптовалюту",
    },
    # RandommailSender.randomMail[].mailNames — название случайного письма; PlayerMailSubscription — журналы.
    "mail": {"Random junk": "Всякий хлам", "Ads": "Реклама", "Junk mail": "Спам", "Lifehack-Pekka": "Lifehack-Pekka",
             "OpenSewer Magazine": "Журнал OpenSewer", "Very Good Friend": "Очень хороший друг"},
    # manufacturing / Generator / Dishwasher / InvoicePaymentMachine / BottleRecyclingPanel — названия станков и автоматов.
    "station": {"Brick stove": "Кирпичная печь", "Espresso Machine": "Эспрессо-машина", "Field kitchen": "Полевая кухня",
                "Gas stove": "Газовая плита", "Injection Machine": "Литьевая машина",
                "Makeshift Pizza Oven": "Самодельная печь для пиццы", "Ore Crusher": "Рудодробилка",
                "Pizza Oven": "Печь для пиццы", "Basement Sauna": "Сауна в подвале", "Campfire": "Костёр",
                "Tilestove": "Изразцовая печь", "Dishwashing powder": "Порошок для посудомойки",
                "Payment machine": "Платёжный автомат", "Apartment 7": "Квартира 7"},
    # Growing — грядки и растения.
    "growing": {"Greener mushrooms": "Грибы позеленее", "Greenest Mushrooms": "Самые зелёные грибы",
                "Leek plant": "Лук-порей (растение)", "Lettuce plant": "Салат (растение)", "Monstera plant": "Монстера",
                "Onion Plant": "Лук (растение)", "Orange mushrooms": "Оранжевые грибы", "Potato plant": "Картофель (растение)",
                "Sewer fish grow": "Разведение канализационной рыбы", "Small farm": "Маленькая ферма",
                "Sugarbeet Plant": "Сахарная свёкла (растение)", "Tire pot": "Горшок из шины",
                "Tomato plant": "Томат (растение)", "Turnip Plant": "Репа (растение)"},
    # Trade — торговые точки (имена владельцев и бренды остаются как есть).
    "shop": {"Henning's shop": "Лавка Henning", "One stop shop": "Всё в одном", "Seamus' potatoes": "Картошка Seamus",
             "Selmas's Stall": "Прилавок Selma", "Sirkku's Flower Stall": "Цветочный прилавок Sirkku",
             "Teuvo's alcohol stall": "Алкогольный прилавок Teuvo"},
    # Storage — полки и холодильники магазинов (название при наведении).
    "storage": {
        "Plastic Parts": "Пластиковые детали", "10L Plastic Bottles": "Пластиковые бутыли 10 л",
        "10L Spring Water Bottles": "Бутыли родниковой воды 10 л", "Alarm Clocks": "Будильники", "Apples": "Яблоки",
        "Ball Bearings": "Шарикоподшипники", "Bandages": "Бинты", "Big Flashlights": "Большие фонарики",
        "Bleaches": "Отбеливатели", "Borlmaro cigarette packs": "Пачки сигарет Borlmaro", "Bottles": "Бутылки",
        "Bricks": "Кирпичи", "Buckets": "Вёдра", "Cabbages": "Капуста", "Cables": "Кабели",
        "Camera in a Sales Package": "Фотоаппарат в упаковке", "Cameras": "Фотоаппараты", "Can Openers": "Консервные ножи",
        "Candles": "Свечи", "Canned Pea Soups": "Гороховый суп в банках", "Canned Peaches": "Персики в банках",
        "Canned Pineapples": "Ананасы в банках", "Canned Tomato Soups": "Томатный суп в банках",
        "Canned Tuna": "Тунец в банках", "Canned meat": "Мясные консервы", "Canned sardines": "Сардины в банках",
        "Canned vendaces": "Ряпушка в банках", "Cans of Pea Soup": "Банки горохового супа", "Cans of Peaches": "Банки персиков",
        "Cans of Tomato Soup": "Банки томатного супа", "Carl Fatler Chocolate Bars": "Шоколадки Carl Fatler",
        "Carl Fatler chocolate bar": "Шоколадка Carl Fatler", "Carrots": "Морковь", "Chocolate Bars": "Шоколадки",
        "Cleaning sprays": "Чистящие спреи", "Cola drinks": "Кола", "Cooking oils": "Растительное масло",
        "Crowbars": "Монтировки", "Cucumbers": "Огурцы", "Cups": "Чашки", "Disinfectants": "Дезинфицирующие средства",
        "Doughnuts": "Пончики", "Drinks (Left)": "Напитки (слева)", "Drinks (Right)": "Напитки (справа)",
        "Ducky Strike cigarette packs": "Пачки сигарет Ducky Strike", "Duct Tapes": "Клейкая лента",
        "Egg Cartons": "Коробки яиц", "Eggs": "Яйца", "El Presidente Coffee bags": "Пачки кофе El Presidente",
        "Electronic Cigarettes": "Электронные сигареты", "Empty Cups": "Пустые чашки",
        "Empty Flowerpots": "Пустые цветочные горшки", "Empty Plastic Buckets": "Пустые пластиковые вёдра",
        "Empty Plastic Containers": "Пустые пластиковые контейнеры", "Envelopes": "Конверты",
        "Evil Bear Candy Bags": "Пакеты конфет Evil Bear", "Evotan Bottle": "Бутылка Evotan",
        "Facemask Packs": "Упаковки масок", "Fish Trap": "Рыболовная ловушка", "Flashlights": "Фонарики",
        "Flowerpots": "Цветочные горшки", "Fuse socket": "Гнездо предохранителя", "Gas Bottles": "Газовые баллоны",
        "Gas Mask Filters": "Фильтры противогазов", "Gas Masks": "Противогазы", "Glues": "Клей", "Grapes": "Виноград",
        "Green Mushroom Mycelium Injectors": "Инжекторы мицелия зелёных грибов", "Green orbs": "Зелёные шары",
        "Grill Coal Bags": "Мешки угля для гриля", "Gritbox": "Ящик с песком", "Hand soaps": "Мыло для рук",
        "Handheld Console in a Sales Package": "Портативная консоль в упаковке", "Handheld Consoles": "Портативные консоли",
        "Hardhats": "Каски", "Hop bags": "Мешки хмеля", "Hot Dog Bun Packs": "Упаковки булочек для хот-догов",
        "Hot Dog Weiner Pack": "Упаковка сосисок для хот-догов", "Hotdog Grill": "Гриль для хот-догов",
        "Hotdog buns": "Булочки для хот-догов", "Ice tea": "Холодный чай",
        "Industrial Fuses": "Промышленные предохранители", "Industrial fuse": "Промышленный предохранитель",
        "Insulation Wool": "Изоляционная вата", "Jars": "Банки", "Jubilee Mocca Coffee bags": "Пачки кофе Jubilee Mocca",
        "Juice Concentrate": "Концентрат сока", "Kitties Candy Bags": "Пакеты конфет Kitties",
        "Kitties candy bag": "Пакет конфет Kitties", "Laundry detergents": "Стиральный порошок",
        "Lies chip bags": "Пакеты чипсов Lies", "Lighters": "Зажигалки", "Macaroni bags": "Пачки макарон",
        "Malts bags": "Мешки солода", "Market Small Bottle Refigerator": "Маленький холодильник для бутылок",
        "Mason Jars": "Стеклянные банки", "Matchboxes": "Спичечные коробки", "Matches": "Спички", "Meshes": "Сетки",
        "Metal Slabs": "Металлические плиты", "Mobile Phone in a Sales Package": "Мобильный телефон в упаковке",
        "Mobile Phones": "Мобильные телефоны", "Moca Cola Can": "Банка Moca Cola",
        "Moca Cola Light Can": "Банка Moca Cola Light", "Mugs": "Кружки", "Multigrain breads": "Многозерновой хлеб",
        "Mushroom Substitute": "Заменитель грибов", "Mättöx Candy Bags": "Пакеты конфет Mättöx",
        "Mättöx candy bag": "Пакет конфет Mättöx", "Nort cigarette packs": "Пачки сигарет Nort",
        "Old Mobile Phone in a Sales Package": "Старый мобильный телефон в упаковке",
        "Old Mobile Phones": "Старые мобильные телефоны", "Onions": "Лук", "Ore Chute": "Рудный жёлоб",
        "Osmo Olut bottle": "Бутылка Osmo Olut", "Osmo olut (Left)": "Osmo Olut (слева)",
        "Osmo olut (Right)": "Osmo Olut (справа)", "Osmo-Olut Bottles": "Бутылки Osmo Olut", "Ovencloths": "Прихватки",
        "Pack of Bolrmaro Cigarettes": "Пачка сигарет Bolrmaro", "Packs of Nort Cigarettes": "Пачки сигарет Nort",
        "Packs of Winstead Cigarettes": "Пачки сигарет Winstead", "Papers": "Бумага", "Pea soup cans": "Банки горохового супа",
        "Peach cans": "Банки персиков", "Pekka Coffee bags": "Пачки кофе Pekka", "Peppers": "Перцы",
        "Pickaxes": "Кирки", "Planks": "Доски", "Plastic Buckets": "Пластиковые вёдра",
        "Plastic Containers": "Пластиковые контейнеры", "Plates": "Тарелки", "Pluto Candy Bars": "Батончики Pluto",
        "Pluto candy bar": "Батончик Pluto", "Potatoes": "Картофель", "Rails": "Рельсы", "Rat Traps": "Крысоловки",
        "Refrigerator (Butterine)": "Холодильник (маргарин)", "Refrigerator (Cheese)": "Холодильник (сыр)",
        "Refrigerator (Cream)": "Холодильник (сливки)", "Refrigerator (Fish)": "Холодильник (рыба)",
        "Refrigerator (Meat Products)": "Холодильник (мясные продукты)", "Refrigerator (Meat)": "Холодильник (мясо)",
        "Refrigerator (Milk)": "Холодильник (молоко)", "Refrigerator (Mämmi)": "Холодильник (Mämmi)",
        "Refrigerator (Pizza)": "Холодильник (пицца)", "Refrigerator (Prepared Foods)": "Холодильник (готовая еда)",
        "Refrigerator (Sausages)": "Холодильник (колбасы)", "Refrigerator (Wieners)": "Холодильник (сосиски)",
        "Refrigerator (Yeast)": "Холодильник (дрожжи)", "Rice bags": "Пачки риса", "Rye breads": "Ржаной хлеб",
        "Rye flour bags": "Мешки ржаной муки", "SLR Camera": "Зеркальный фотоаппарат",
        "SLR Camera in a Sales Package": "Зеркальный фотоаппарат в упаковке", "Scrapers": "Скребки",
        "Scratchcards": "Лотерейные билеты", "Sheet Metals": "Листовой металл", "Sledgehammers": "Кувалды",
        "Snacks (Left)": "Закуски (слева)", "Snacks (Right)": "Закуски (справа)", "Soft drinks (Left)": "Газировка (слева)",
        "Soft drinks (Right)": "Газировка (справа)", "Sour herring": "Квашеная сельдь", "Stainless Steel": "Нержавеющая сталь",
        "Sugar bags": "Пачки сахара", "Sugar beets": "Сахарная свёкла", "Sugar flakes": "Сахарные хлопья",
        "Tablet in a Sales Package": "Планшет в упаковке", "Tablets": "Планшеты", "Tarps": "Брезент",
        "Tea boxes": "Коробки чая", "Teddy bear": "Плюшевый мишка", "Teddy bears": "Плюшевые мишки",
        "Toilet Paper 6 Packs": "Туалетная бумага (6 рулонов)", "Tomato soup cans": "Банки томатного супа",
        "Tomatoes": "Помидоры", "Turnip chip bags": "Пакеты репных чипсов", "Turnips": "Репа", "Valve": "Вентиль",
        "Watermelons": "Арбузы", "Wheat flour bags": "Мешки пшеничной муки", "White breads": "Белый хлеб",
        "Windshield wiper fluids": "Омывающая жидкость", "Wine (Left)": "Вино (слева)", "Wine (Right)": "Вино (справа)",
        "Wineglasses": "Бокалы", "Winstead cigarette packs": "Пачки сигарет Winstead", "Wood Logs": "Брёвна",
        "Yarn rolls": "Мотки пряжи", "Zebra cigarette packs": "Пачки сигарет Zebra",
    },
}


def entries():
    out = []

    def text(i, s, t, ctx):
        out.append({"id": "manual/" + i, "kind": "text", "source": s, "target": t, "status": "translated", "context": ctx})

    def tpl(i, s, t, ctx, scope=None):
        e = {"id": "manual/" + i, "kind": "template", "source": s, "target": t, "status": "translated", "context": ctx}
        if scope:
            e["scope"] = scope
        out.append(e)

    def scoped(i, scope, s, t, ctx):
        out.append({"id": "manual/" + i, "kind": "text", "scope": scope, "source": s, "target": t, "status": "translated", "context": ctx})

    for k, v in TASTE.items():
        text("taste/" + k, "Taste: " + k, "Вкус: " + v, "ToolTip.MentalHealthConsumableDataToString")
    for k, v in EFFICACY.items():
        text("efficacy/" + k, "Efficacy: " + k, "Эффективность: " + v, "ToolTip.MentalHealthMedicineDataToString")
    for k, v in QUALITY.items():
        text("quality/" + k, "Quality: " + k, "Качество: " + v, "ToolTip.QualityDataToString")
    for k, v in CARD_KEYS.items():
        tpl("cards/" + k, k + " ({0})", v + " ({0})", "CardGameUI: строка подсказки с клавишей")
    for k, v in DAYS.items():
        # Без scope: так же выглядят часы на экране ("Wednesday 11:33").
        tpl("time/at/" + k, k + " at {0}", v + ", {0}", "Lua TellTimeResponse (варианты 0–1)")
        tpl("time/day/" + k, k + " {0}", v + ", {0}", "Lua TellTimeResponse (вариант 3); часы на экране")
        tpl("time/oclock/" + k, "It's {0} o'clock at " + k.lower(), "Сейчас {0} ч, " + v.lower(), "Lua TellTimeResponse (вариант 2)", "lua")
    tpl("time/clock", "Clock is {0}", "На часах {0}", "Lua TellTimeResponse (варианты 4–5)", "lua")
    tpl("time/now", "Now it's {0}", "Сейчас {0}", "Lua TellTimeResponse (варианты 6–7)", "lua")
    # Без scope: то же "3h" показывает окно предмета (ItemInfoPanel: время чтения, топливо).
    tpl("crime/hours", "{0}h", "{0} ч", "Lua GetSentenceLengthPreArrest; ItemInfoPanel")
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

    # Подсказка предмета (ToolTip.cs): строки, которые код собирает сам.
    tpl("tooltip/liquid", "Contains: {0} {1}L/{2}L", "Содержит: {0} {1} л/{2} л", "ToolTip.LiquidDataToString")
    tpl("tooltip/package-ready", "Package {0} ready to be sent", "Посылка «{0}» готова к отправке", "ToolTip.MailableDataToString")
    tpl("tooltip/mail-ready", "Mail {0} ready to be sent", "Письмо «{0}» готово к отправке", "ToolTip.MailableDataToString")
    tpl("tooltip/total-oc", "TOTAL {0} OC", "ИТОГО {0} OC", "ToolTip.ReceiptDataToString")
    tpl("tooltip/total-rm", "TOTAL {0} RM", "ИТОГО {0} RM", "ToolTip.ReceiptDataToString")
    tpl("tooltip/certificate", "Certificate for {0}", "Сертификат: {0}", "ToolTip.CertificateDataToString")
    tpl("tooltip/certificate-value", "Certificate for {0} at nominal value of {1} OC", "Сертификат: {0}, номинал {1} OC",
        "ToolTip.CertificateDataToString")
    tpl("tooltip/connectivity", "{0} ,{1}", "{0}, {1}", "ToolTip.ConnectivityDataToString: сети через запятую без пробела")
    tpl("tooltip/required-skill", "Required skill: <b>{0} {1}</b>", "Нужный навык: <b>{0} {1}</b>", "ToolTip.Activate(TaskItem): чертёж")
    tpl("tooltip/required-in-apartment", "Required in: Apartment {0} {1}", "Нужно для: квартира {0}, {1}",
        "TenementController.CreateMarkedItemToolTipText: отмеченное улучшение квартиры")
    tpl("money/not-enough", "Not enough {0}", "Недостаточно {0}", "MoneySliderPanel")
    # Модификаторы станков (ModifierUI.cs): значок под окном станка и его подсказка.
    for k, v in MODIFIERS.items():
        text("modifier/" + k, "Modifier: " + k, "Модификатор: " + v, "ModifierUI.UpdateModifierInfo (RecipeCondition)")
    for k, v in CONDITIONS.items():
        text("condition/" + k, k, v, "RecipeInfoUI: список нужных и исключающих модификаторов (RecipeCondition)"
             + ("; уровень улучшения (UpgradeType)" if k in LEVELS else ""))
    # "Advanced" в создании персонажа — кнопка «Дополнительно»: строка ui.json со scope этого объекта.

    # Улучшения доходного дома (TenementUpgrade.GetName, TenementUpgradeProgress): имя уровня отдельно
    # (окно улучшения, "Current upgrade: Shabby") и в названии и описании задачи ремонта, где игра пишет его строчными
    # между номером квартиры и тем, что улучшается: "Apt 1 shabby bathroom", "Tenement shabby courtyard".
    for k, v in LEVELS.items():
        if k not in LEVELS_ELSEWHERE and k not in CONDITIONS:
            text("upgrade-level/" + k, k, v, "TenementUpgrade.GetName: уровень улучшения (UpgradeType)")
        low = k.lower()
        tpl("upgrade-task/tenement/" + k, "Tenement %s {0}" % low, "Доходный дом: {0} — «%s»" % v,
            "TenementUpgradeProgress.AddGeneral: название задачи ремонта")
        tpl("upgrade-task/apartment/" + k, "Apt {0} %s {1}" % low, "Кв. {0}: {1} — «%s»" % v,
            "TenementUpgradeProgress.AddApartment: название задачи ремонта")
        tpl("upgrade-task/tenement-ordered/" + k, "You ordered a tenement %s {0} upgrade from {1}" % low,
            "Вы заказали у {1} улучшение доходного дома: {0} — «%s»" % v, "TenementUpgradeProgress.AddGeneral: описание задачи ремонта")
        tpl("upgrade-task/apartment-ordered/" + k, "You ordered an apartment {0} %s {1} upgrade from {2}" % low,
            "Вы заказали у {2} улучшение квартиры {0}: {1} — «%s»" % v, "TenementUpgradeProgress.AddApartment: описание задачи ремонта")

    for k, v in TALK_TITLES.items():
        text("talk/" + k, k, v, "InteractableTalk.title или имя актёра диалога: подпись собеседника, не имя")

    # Строки одного места (scope): общий перевод того же текста здесь не подходит.
    # Заставка интро: "OPEN SEWER" собрана из двух надписей с центрами под половинками английской фразы.
    # Русские слова длиннее — вторую половину сдвигают пробелы в начале (подобрано в игре: половинки потом разъезжаются).
    scoped("object/intro-open", "object:Text Screen/OPEN", "OPEN", "ОТКРЫТАЯ",
           "Интро (level1, Main/Text Screen/OPEN): левая половина надписи «OPEN SEWER»; вывеска \"OPEN\" — «ОТКРЫТО»")
    scoped("object/intro-sewer", "object:Text Screen/SEWER", "SEWER", "      КЛОАКА",
           "Интро (level1, Main/Text Screen/SEWER): правая половина надписи «OPEN SEWER»")
    # Круглая кнопка в окне предмета вмещает около пяти букв в строке.
    scoped("object/drop-button", "object:Drop button/ButtonText", "Drop", "Бро-\nсить",
           "Окно предмета: круглая кнопка Drop (Info Panel/.../Buttons/Drop button)")
    # Подсказка UseText на лестнице "(Drop)"; у UseText scope action (см. TmpLocalizer).
    scoped("action/ladder-drop", "action", "Drop", "Спрыгнуть", "InteractableLadder: UseText \"(Drop)\" — слезть с лестницы")
    # Игровой автомат: кнопки фиксации барабана (заголовок "Lock") подписаны именем SlotMachineHelper.LockStatuses;
    # в общем словаре "Left"/"Right" — направления управления («Влево»).
    for k, v in {"Left": "Левый барабан", "Center": "Центральный барабан", "Right": "Правый барабан"}.items():
        scoped("action/slot-lock/" + k, "action", k, v, "SlotMachineGameplay: подсказка кнопки фиксации барабана")
        scoped("action/slot-locked/" + k, "action", k + " locked", v + " зафиксирован", "SlotMachineGameplay: барабан зафиксирован")
    # Заголовок этих кнопок — "Lock"; в общем словаре это глагол для дверей «Запереть». У заголовка UseText scope usetitle.
    scoped("usetitle/slot-lock", "usetitle", "Lock", "Фиксация", "SlotMachineGameplay: заголовок кнопки фиксации барабана (UseText)")
    tpl("modifier/efficiency", "Efficiency: {0}%", "Эффективность: {0}%", "ModifierUI.UpdateQualityModifierInfo")
    text("modifier/efficiency-details", "Affects working speed, output quality and fuel economy",
         "Влияет на скорость работы, качество продукции и расход топлива", "ModifierUI.UpdateQualityModifierInfo")
    tpl("modifier/fuel", "Fuel efficiency: {0}%", "Экономия топлива: {0}%", "ModifierUI.UpdateBurnTimeFactorModifierInfo")
    tpl("modifier/capacity", "Capacity: {0} liters", "Объём: {0} л", "ModifierUI.UpdateCapacityModifierInfo")
    text("modifier/manual", "Manually operated (Requires player presence)", "Ручное управление (нужно присутствие игрока)",
         "ModifierUI.UpdateManualModifier")
    text("modifier/upgrades", "Applied upgrades:", "Установленные улучшения:", "ModifierUI.UpdateUpgradesModifierInfo")
    # Кровать (Sleep.GetTitle): "<название> — <удобство>[, <безопасность>]". Отдельные шаблоны, чтобы слова
    # вроде "Okay" не брали перевод из других мест.
    for k, v in COMFORT.items():
        tpl("sleep/" + k, "{0} — " + k, "{0} — " + v, "Sleep.GetTitle: удобство кровати")
        for s, sv in SAFETY.items():
            tpl("sleep/%s/%s" % (k, s), "{0} — %s, %s" % (k, s), "{0} — %s, %s" % (v, sv), "Sleep.GetTitle: удобство и безопасность")
    # Счета (Item.GetItemTitle, InvoiceController и др.): кому платить приходит из разных мест и хранится в сохранении.
    tpl("invoice/title", "Invoice - {0}: {1} {2}", "Счёт — {0}: {1} {2}", "Item.GetItemTitle: счёт")
    tpl("invoice/expired", "Expired Invoice - {0}", "Просроченный счёт — {0}", "Item.GetItemTitle: просроченный счёт")
    tpl("invoice/reminder", "REMINDER: {0}", "НАПОМИНАНИЕ: {0}", "InvoiceController.SendReminder")
    tpl("invoice/collateral", "Rent collateral: {0}", "Залог за аренду: {0}", "TenementController: залог жильца")
    tpl("invoice/downpayment", "{0}: Debt Downpayment", "{0}: взнос по долгу", "Money: банковский долг")
    text("invoice/jorma", "Jorma's protection services", "Охранные услуги Jorma", "SleepEventController: счёт за «охрану»")
    # Навыки (PlayerStats.PlayerSkillType): окно персонажа показывает skill.Type.ToString(), уведомления — тоже.
    for k, v in SKILLS.items():
        text("skill/" + k, k, v, "PlayerStats.PlayerSkillType: название навыка")
    text("skill/none-details", "You couldn't learn a new skills", "Не удалось изучить новые навыки", "PlayerStats.NoNewSkillsNotification")
    # Сложность (DifficultyController.difficulties[].difficultyName) — род как у «сложность».
    text("difficulty/casual", "Casual", "Лёгкая", "DifficultyUI: название сложности")
    # Подсказки взаимодействия (UseText оборачивает подпись в скобки). Отдельно от "Enter"/"Interact" — названий клавиш и действий.
    text("use/enter", "(Enter)", "(Войти)", "DoorChangelevel: useMessage = \"Enter\"")
    text("use/interact", "(Interact)", "(Взаимодействовать)", "InteractableTalk: useMessage = \"Interact\"")
    # Вкладка и кнопка меню модов (их добавляет Obenseuer Modding Framework) и заголовки титров.
    text("menu/mods", "Mods", "Моды", "ObenseuerCore: вкладка MODS в меню игры и кнопка главного меню")
    text("credits/loiste", "Loiste Interactive is:", "Loiste Interactive — это:", "Титры (Credits)")
    text("credits/thanks", "Thanks to the following contributors for participating in the development of this game:",
         "Спасибо всем, кто участвовал в разработке игры:", "Титры (Credits)")

    # Строки из объектов, у которых UnityPy не читает дерево типов (tools/extract/scan_raw.py). Имён полей там нет,
    # поэтому сюда отобраны только видимые названия: счета и письма RandommailSender, станки, грядки, полки магазинов.
    for group, table in RAW_TITLES.items():
        for k, v in table.items():
            text("raw/%s/%s" % (group, k), k, v, "scan_raw.py: " + group)
    return out


def main():
    path = os.path.join(ROOT, "translations", "manual.json")
    catalog_io.write(path, "manual", entries(), "25969064")
    print("manual.json:", len(entries()))


if __name__ == "__main__":
    main()
