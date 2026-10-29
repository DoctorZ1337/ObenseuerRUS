using System.IO;
using ObenseuerRUS.Core;
using Xunit;

namespace ObenseuerRUS.Tests
{
    /// <summary>Файлы translations/ должны загружаться тем же кодом, что и в игре, без ошибок.</summary>
    public class TranslationFilesTests
    {
        private static string TranslationsDir()
        {
            string dir = Directory.GetCurrentDirectory();
            while (dir != null && !Directory.Exists(Path.Combine(dir, "translations"))) dir = Path.GetDirectoryName(dir);
            Assert.NotNull(dir);
            return Path.Combine(dir, "translations");
        }

        [Fact]
        public void All_files_load_without_problems()
        {
            Catalog catalog = Catalog.LoadDirectory(TranslationsDir());
            Assert.Empty(catalog.Problems);
            Assert.True(catalog.Used > 0);
        }

        [Theory]
        [InlineData("Taste: Good", "Вкус: хороший")]
        [InlineData("Account: 250 OC", "Счёт: 250 КМ")]
        [InlineData("BRICK", "КИРПИЧ")]
        [InlineData("Pod 18", "Капсула 18")]
        [InlineData("Tenement resources (Empty)", "Ресурсы доходного дома (пусто)")]
        [InlineData("(Steal)", "(Украсть)")]
        [InlineData("Thirst!", "Жажда!")]
        [InlineData("C)DRAG & DROP   R)DROP   Q)SWITCH TAB   X)USE ITEM   Tab)EXIT", "C)ПЕРЕТАЩИТЬ   R)ВЫБРОСИТЬ   Q)СМЕНИТЬ ВКЛАДКУ   X)ИСПОЛЬЗОВАТЬ   Tab)ВЫХОД")]
        [InlineData("You're very addicted to alcohol and nicotine.", "Сильная зависимость: алкоголь и никотин.")]
        // Строки со скриншотов 0.3.1, где перевод был частичным или отсутствовал.
        [InlineData("Tenement Resource\nA sawed piece of wood\nEstimated value: 4 OC\n\nFuel for: 1h\n", "Ресурс доходного дома\nA sawed piece of wood\nПримерная стоимость: 4 КМ\n\nТопливо на: 1 ч\n")]
        [InlineData("Due in 6 days\n", "Срок через 6 дн.\n")]
        [InlineData("Invoice - Blackmail Letter: 190 OC", "Счёт — Письмо с шантажом: 190 КМ")]
        [InlineData("Efficiency: 75%", "Эффективность: 75%")]
        [InlineData("Affects working speed, output quality and fuel economy", "Влияет на скорость работы, качество продукции и расход топлива")]
        [InlineData("Mattress — Aching, Unsafe", "Матрас — всё ломит, опасно")]
        [InlineData("(Not tired enough to sleep!)", "(Спать пока не хочется!)")]
        [InlineData("Drinking", "Выпивка")]
        [InlineData("Carpentry", "Плотничество")]
        [InlineData("Grow turnips for Jomppe 0/10", "Вырастить репу для Jomppe 0/10")]
        [InlineData("(Enter)", "(Войти)")]
        [InlineData("Key Bindings", "Назначение клавиш")]
        [InlineData("Controls", "Управление")]
        [InlineData("Malone Petronis wants to help you renovate the tenement. He told you that there should be contract work business owner, Samuel Jonasson, in Deekula building who can help you with it.\n\nMalone also told you that you need a lot of money so he suggested that you go and ask for a job at Kurahaara brothers' farm.",
                    "Malone Petronis хочет помочь вам отремонтировать доходный дом. Он сказал, что в здании Deekula должен быть владелец подрядной фирмы, Samuel Jonasson, который может с этим помочь.\n\nЕщё Malone сказал, что вам понадобится много денег, и предложил спросить работу на ферме братьев Kurahaara.")]
        [InlineData("Bridge toll contact info​", "Контакты по оплате проезда по мосту​")]
        // Голое "Search" — поле поиска (меню модов, компьютер в игре); у контейнера UseText показывает "(Search)".
        [InlineData("Search", "Поиск")]
        [InlineData("(Search)", "(Обыскать)")]
        // Строки со скриншотов 0.4.6.
        // TaskPanel: описание задания (два абзаца) + "\n\n" + описание выполненной цели.
        [InlineData("Malone Petronis wants you to go find the body of his friend named Seamus Liepa. Malone believes that his body is somewhere in Obenseuer's old mine.\n\nHe said that there should be access to the mines in basement of Deekula building, but someone may live in the basement which can be problematic.\n\nYou got to the old mining tunnels through Deekula's basement.",
                    // Второй абзац дословно есть и в таблице Tasks (Orange9.description): берётся её перевод.
                    "Malone Petronis хочет, чтобы вы нашли тело его друга Seamus Liepa. Malone считает, что тело где-то в старой шахте Obenseuer.\n\nОн сказал, что в подвале здания Deekula должен быть проход в шахты, но в подвале, возможно, кто-то живёт, и с этим могут быть проблемы.\n\nВы попали в старые шахтные туннели через подвал Deekula.")]
        [InlineData("Apt 1 shabby bathroom", "Кв. 1: ванная — «Убогий»")]
        [InlineData("Tenement shabby courtyard", "Доходный дом: двор — «Убогий»")]
        [InlineData("You ordered an apartment 1 shabby bathroom upgrade from Samuel Jonasson", "Вы заказали у Samuel Jonasson улучшение квартиры 1: ванная — «Убогий»")]
        [InlineData("Wait for the apartment 1 bathroom upgrade to complete", "Дождитесь завершения улучшения «ванная» в квартире 1")]
        [InlineData("Current upgrade: Shabby", "Текущее улучшение: Убогий")]
        [InlineData("Cucumber Seeds (5)", "Семена огурца (5)")]
        [InlineData("Excluded modifiers:\n<color=\"white\">Makeshift<color=\"white\">, <#a8211f>crude<color=\"white\">.", "Исключающие модификаторы:\n<color=\"white\">Самодельный<color=\"white\">, <#a8211f>грубый<color=\"white\">.")]
        [InlineData("<color=\"white\">Sun<color=\"white\">, <color=\"white\">large<color=\"white\">.", "<color=\"white\">Солнце<color=\"white\">, <color=\"white\">большой<color=\"white\">.")]
        [InlineData("Owned - Mirjam Freiter", "Собственность — Mirjam Freiter")]
        [InlineData("Building Menu\n(Not fully examined!)", "Меню строительства\n(Осмотрено не полностью!)")]
        [InlineData("Interviewers", "Интервьюеры")]
        // Имя актёра над репликой (часть после "/" в "Start/Interviewer").
        [InlineData("Interviewer", "Интервьюер")]
        [InlineData("Alcohol addiction: +10\nMushroom addiction: +10\nFungal disease: +10\n", "Тяга к алкоголю: +10\nТяга к грибам: +10\nГрибковая болезнь: +10\n")]
        [InlineData("Fried glowing rat", "Жареная светящаяся крыса")]
        [InlineData("Drink", "Напиток")]
        // Валюты: OC — КМ (клоачные монеты), RM — РД (реальные деньги); «OC Pot» — бренд, кириллицей он «КМ Пот» (словарь названий).
        [InlineData("OC", "КМ")]
        [InlineData("1 RM = 10 OC", "1 РД = 10 КМ")]
        [InlineData("Invoice - Only Bans: 50 RM", "Счёт — Only Bans: 50 РД")]
        [InlineData("OC Pot", "OC Pot")]
        // Окно улучшения: узкое поле взноса и срок из кусочков (TenementUpgrade.GetTimeString).
        [InlineData("First: 300 OC", "Аванс: 300 КМ")]
        [InlineData("12 hours ", "12 ч ")]
        [InlineData("2d 5h ", "2 дн. 5 ч ")]
        [InlineData("Skip two hours", "Пропустить два часа")]
        // Часы на экране: те же шаблоны, что и у Lua TellTimeResponse.
        [InlineData("Wednesday 11:33", "Среда, 11:33")]
        // Рецепт, лежащий как предмет: название рецепта + " Recipe" (CollectibleRecipe).
        [InlineData("Wiring Components Recipe", "Рецепт: Электромонтажные детали")]
        [InlineData("Time: 12:25", "Время: 12:25")]
        // Врач: кнопки с ценой и заключения, которые код склеивает из готовых фраз (DoctorPanel, Treatment*).
        [InlineData("Mental health (180 OC)", "Психическое здоровье (180 КМ)")]
        [InlineData("You are very unwell now and you have severe mental health problems! I recommend prescription medicine! You also seem to have an addiction to ducks, maybe that affects your mood.",
                    "Сейчас вам очень плохо, и у вас серьёзные проблемы с психическим здоровьем! Рекомендую рецептурные лекарства! Похоже, у вас ещё и зависимость от уток — может, это влияет на настроение.")]
        [InlineData("It's really high. It's 640 millisieverts.", "Очень высокий. 640 миллизиверт.")]
        [InlineData("Mashing", "Браговарение")]
        [InlineData("Pod 20: 46 OC per night", "Капсула 20: 46 КМ за ночь")]
        [InlineData("Room: 12 RM per night", "Номер: 12 РД за ночь")]
        [InlineData("Apartment Bathroom\nMinimum requirement: Makeshift", "Ванная квартиры\nМинимальное требование: Самодельный")]
        [InlineData("Preferred size\nSmall apartment", "Желаемый размер\nМаленькая квартира")]
        [InlineData("1 night: 42 OC", "1 ночь: 42 КМ")]
        [InlineData("3 nights: 126 OC", "3 ночи: 126 КМ")]
        [InlineData("7 nights: 294 OC", "7 ночей: 294 КМ")]
        // Окно станка: кнопка, объём, остаток времени и топлива.
        [InlineData("Abort", "Прервать")]
        [InlineData("5L", "5 л")]
        [InlineData("52m", "52 мин")]
        [InlineData("7,2h/12h", "7,2 ч/12 ч")]
        [InlineData("Room", "Room")]
        // Generator.ManuName в 0.5.13 — с двумя пробелами (объект не читается UnityPy, строка из scan_raw.py).
        [InlineData("Mobile  generator", "Передвижной генератор")]
        // Строка управления в режиме перемещения предметов (BuildingSystemMenu): клавиши как в игре, подписи по-русски.
        [InlineData("LMB)USE SLOT   Q/E)CHANGE SLOT   RMB)HOLD AND MOVE TO ROTATE   V)ROTATION MODE:FREE   Tab)EXIT",
                    "LMB)ИСПОЛЬЗОВАТЬ СЛОТ   Q/E)СМЕНИТЬ СЛОТ   RMB)УДЕРЖИВАТЬ И ДВИГАТЬ ДЛЯ ПОВОРОТА   V)РЕЖИМ ПОВОРОТА:СВОБОДНЫЙ   Tab)ВЫХОД")]
        [InlineData("V)ROTATION MODE:45°", "V)РЕЖИМ ПОВОРОТА:45°")]
        public void Real_catalog_translates_game_strings(string source, string expected)
        {
            var tr = new TextTranslator(Catalog.LoadDirectory(TranslationsDir()));
            Assert.Equal(expected, tr.Translate(source));
        }

        [Theory]
        [InlineData("Grow turnips for Jomppe 0/10", "Вырастить репу для Йомппе 0/10")]
        [InlineData("Arvo Kirkastaja", "Арво Киркастая")]
        [InlineData("Osmo Olut", "Осмо Олут")]
        [InlineData("Mr. Duck", "Мистер Дак")]
        [InlineData("B1 Piggyhill", "B1 Пиггихилл")]
        // Сокращения компаний — в словаре названий.
        [InlineData("SBB debit card delivery handling fee", "Сбор СББ за доставку дебетовой карты")]
        [InlineData("Fungispring LTD service hotline:\n030 892 951", "Горячая линия ООО «Фангиспринг»:\n030 892 951")]
        [InlineData("OC Pot", "КМ Пот")]
        // Названия со смыслом переведены; родовое слово перед ними — в самой фразе перевода.
        [InlineData("OS Power Company", "«Клоакэнерго»")]
        [InlineData("One Stop Shop", "Магазин «Всё в одном»")]
        public void Real_names_are_written_in_cyrillic_when_enabled(string source, string expected)
        {
            Catalog catalog = Catalog.LoadDirectory(TranslationsDir());
            Assert.NotEmpty(catalog.People);
            Assert.NotEmpty(catalog.Brands);
            var tr = new TextTranslator(catalog) { Names = ProperNames.For(catalog, true, true) };
            Assert.Equal(expected, tr.Translate(source));
        }

        [Fact]
        public void Real_note_gets_names_on_every_line()
        {
            // Записка показывается одним полем TMP: подпись и название отеля — отдельные строки без кириллицы.
            Catalog catalog = Catalog.LoadDirectory(TranslationsDir());
            string source = Catalog.ReadFile(Path.Combine(TranslationsDir(), "tables.json")).Entries
                .Find(e => e.Key == "Confirmation of Your Hotel Korso Reservation.content").Source;
            var tr = new TextTranslator(catalog) { Names = ProperNames.For(catalog, true, true) };
            string[] lines = tr.Translate(source).Split('\n');
            Assert.Equal("Уважаемый Тамми,", lines[0]);
            Assert.Contains("- Имя гостя: Иисакки Тамми", lines);
            Assert.Contains("Карл Шепперн", lines);
            Assert.Equal("Отель Корсо Обенсеуер", lines[lines.Length - 1]);
        }

        [Theory]
        // Кнопки окна предмета и подсказка UseText: глаголы, а не типы предметов ("Drink" — «Напиток»).
        [InlineData("action", "Drink", "Выпить")]
        [InlineData("action", "Eat", "Съесть")]
        [InlineData("action", "Break", "Разбить")]
        [InlineData("action", "(Hold - Drink)", "(Удерживать — Выпить)")]
        [InlineData("action", "(Drop)", "(Спрыгнуть)")]
        [InlineData("action", "(Get off)", "(Слезть)")]
        [InlineData("action", "(Get off (Level 2))", "(Слезть (уровень 2))")]
        [InlineData("action", "No light", "Нечем прикурить")]
        [InlineData("action", "Owned", "Собственность")]
        // Игровой автомат: подсказки кнопок фиксации барабана и выигрышной линии.
        [InlineData("action", "(Center)", "(Центральный барабан)")]
        [InlineData("action", "(Left locked)", "(Левый барабан зафиксирован)")]
        [InlineData("action", "(selected 1: center)", "(выбрана 1: центр)")]
        [InlineData("action", "(Money in machine: 120)", "(Денег в автомате: 120)")]
        [InlineData("usetitle", "Lock", "Фиксация")]
        [InlineData("usetitle", "Winning Line: Center", "Выигрышная линия: центр")]
        [InlineData(null, "Lock", "Запереть")]
        // Строки одного объекта: половинки заставки интро и круглая кнопка; вывеска "OPEN" остаётся «ОТКРЫТО».
        [InlineData("object:Text Screen/OPEN", "OPEN", "ОТКРЫТАЯ")]
        [InlineData("object:Text Screen/SEWER", "SEWER", "      КЛОАКА")]
        [InlineData("object:Drop button/ButtonText", "Drop", "Бро-\nсить")]
        [InlineData(null, "OPEN", "ОТКРЫТО")]
        [InlineData(null, "Drop", "Выбросить")]
        [InlineData("object:Advanced/Advanced", "Advanced", "Дополнительно")]
        [InlineData(null, "Advanced", "Продвинутый")]
        // Почтовый ящик: фраза в скобках целиком, а не «Нет почты — спрыгнуть» (Drop у лестницы).
        [InlineData("action", "(No mail to drop)", "(Нечего отправить)")]
        public void Real_catalog_scoped_strings(string scope, string source, string expected)
        {
            var tr = new TextTranslator(Catalog.LoadDirectory(TranslationsDir()));
            Assert.Equal(expected, tr.Translate(source, scope));
            if (scope != null) Assert.True(tr.HasScope(scope));
        }

        [Fact]
        public void Lua_scope_templates_apply_only_to_lua_values()
        {
            Catalog catalog = Catalog.LoadDirectory(TranslationsDir());
            var tr = new TextTranslator(catalog);
            Assert.True(tr.HasScope("lua"));
            Assert.Equal("Понедельник, 14", tr.Translate("Monday at 14", "lua"));
        }

        [Fact]
        public void Every_template_applies_to_its_own_source_shape()
        {
            Catalog catalog = Catalog.LoadDirectory(TranslationsDir());
            foreach (Template t in catalog.Templates)
            {
                string sample = t.Source;
                for (int i = 0; i < 10; i++) sample = sample.Replace("{" + i + "}", "X" + i);
                Assert.NotNull(t.TryApply(sample, null));
            }
        }
    }
}
