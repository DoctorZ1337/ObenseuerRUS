using ObenseuerRUS.Core;
using Xunit;

namespace ObenseuerRUS.Tests
{
    public class TranslatorTests
    {
        private static TextTranslator Make(params Entry[] entries)
        {
            var catalog = new Catalog();
            foreach (Entry e in entries) catalog.Add(e);
            return new TextTranslator(catalog);
        }

        private static Entry Text(string s, string t) => new Entry { Id = s, Kind = Kinds.Text, Source = s, Target = t, Status = "translated" };
        private static Entry Tpl(string s, string t) => new Entry { Id = s, Kind = Kinds.Template, Source = s, Target = t, Status = "translated" };

        [Fact]
        public void Scoped_text_wins_only_in_its_scope()
        {
            var tr = Make(Text("Drink", "Напиток"), Text("Smoke", "Курение"), Tpl("Hold - {0}", "Удерживать — {0}"),
                          new Entry { Id = "a", Kind = Kinds.Text, Scope = "action", Source = "Drink", Target = "Выпить", Status = "translated" });
            Assert.Equal("Напиток", tr.Translate("Drink"));
            Assert.Equal("Выпить", tr.Translate("Drink", "action"));
            Assert.Equal("Удерживать — Выпить", tr.Translate("Hold - Drink", "action"));
            Assert.Equal("Курение", tr.Translate("Smoke", "action"));
            Assert.Equal("Напиток", tr.Translate("Drink"));
        }

        [Fact]
        public void Lines_of_multiline_entry_are_known_separately()
        {
            var tr = Make(Text("Task one.\n\nTask two.", "Задание один.\n\nЗадание два."), Text("Goal done.", "Цель выполнена."));
            Assert.Equal("Задание один.\n\nЗадание два.\n\nЦель выполнена.", tr.Translate("Task one.\n\nTask two.\n\nGoal done."));
        }

        [Fact]
        public void Lowercase_text_prefers_capitalized_entry()
        {
            var tr = Make(Text("SUN", "ВС"), Text("Sun", "Солнце"), Text("Crude", "Грубый"));
            Assert.Equal("солнце", tr.Translate("sun"));
            Assert.Equal("грубый", tr.Translate("crude"));
            Assert.Equal("ВС", tr.Translate("SUN"));
        }

        [Fact]
        public void Exact_and_trimmed()
        {
            var tr = Make(Text("Options", "Параметры"));
            Assert.Equal("Параметры", tr.Translate("Options"));
            Assert.Equal("  Параметры\n", tr.Translate("  Options\n"));
        }

        [Fact]
        public void Case_follows_the_game()
        {
            var tr = Make(Text("Resume Game", "Продолжить игру"), Text("Heated", "Нагретый"));
            Assert.Equal("ПРОДОЛЖИТЬ ИГРУ", tr.Translate("RESUME GAME"));
            Assert.Equal("нагретый", tr.Translate("heated"));
        }

        [Fact]
        public void Unknown_text_is_left_as_is()
        {
            var tr = Make(Text("Options", "Параметры"));
            string s = "Something new";
            Assert.Same(s, tr.Translate(s));
            Assert.Equal("12:30", tr.Translate("12:30"));
            Assert.Equal("Уже по-русски", tr.Translate("Уже по-русски"));
        }

        [Fact]
        public void Template_translates_inner_part()
        {
            var tr = Make(Tpl("Status: {0}", "Состояние: {0}"), Text("Healthy", "Здоров"));
            Assert.Equal("Состояние: Здоров", tr.Translate("Status: Healthy"));
            Assert.Equal("Состояние: Unknown", tr.Translate("Status: Unknown"));
        }

        [Fact]
        public void Template_can_reorder_slots()
        {
            var tr = Make(Tpl("{0} of {1}", "{1}: {0}"));
            Assert.Equal("B: A", tr.Translate("A of B"));
        }

        [Fact]
        public void Lines_and_tags_are_translated_separately()
        {
            var tr = Make(Text("Bread", "Хлеб"), Text("Any amount", "Любое количество"));
            Assert.Equal("<color=\"white\">Хлеб\nЛюбое количество", tr.Translate("<color=\"white\">Bread\nAny amount"));
            Assert.Equal("<#A8211F>Хлеб</color>", tr.Translate("<#A8211F>Bread</color>"));
            Assert.Equal("Хлеб\nx5", tr.Translate("Bread\nx5"));
        }

        [Fact]
        public void Speaker_prefix_is_translated()
        {
            var tr = Make(Text("Guard", "Охранник"));
            Assert.Equal("Охранник: Стой!", tr.Translate("Guard: Стой!"));
        }

        [Fact]
        public void Composed_ui_strings_are_translated_by_parts()
        {
            var tr = Make(Text("Steal", "Украсть"), Text("Thirst", "Жажда"), Text("DRAG & DROP", "ПЕРЕТАЩИТЬ"),
                          Text("DROP", "ВЫБРОСИТЬ"), Text("EXIT", "ВЫХОД"));
            Assert.Equal("(Украсть)", tr.Translate("(Steal)"));
            Assert.Equal("Жажда!", tr.Translate("Thirst!"));
            Assert.Equal("C)ПЕРЕТАЩИТЬ   R)ВЫБРОСИТЬ   Tab)ВЫХОД", tr.Translate("C)DRAG & DROP   R)DROP   Tab)EXIT"));
        }

        [Fact]
        public void Template_that_translates_everything_wins()
        {
            var tr = Make(Tpl("Tenement {0}", "Доходный дом {0}"), Tpl("{0} (Empty)", "{0} (пусто)"),
                          Text("Tenement resources", "Ресурсы доходного дома"));
            Assert.Equal("Ресурсы доходного дома (пусто)", tr.Translate("Tenement resources (Empty)"));
            Assert.Equal("Доходный дом B", tr.Translate("Tenement B"));
        }

        [Fact]
        public void Weak_template_is_not_applied_to_foreign_text()
        {
            // 0.3.1: "{0} and {1}" и "{0} in {1}" превращали описание задания в "go) и ask for a job".
            var tr = Make(Tpl("{0} and {1}", "{0} и {1}"), Tpl("{0} in {1}", "{0} ({1})"), Text("Bread", "Хлеб"), Text("Milk", "Молоко"));
            string s = "He suggested that you go and ask for a job in the farm.";
            Assert.Same(s, tr.Translate(s));
            Assert.Equal("Хлеб и Молоко", tr.Translate("Bread and Milk"));
        }

        [Fact]
        public void Template_slots_do_not_cross_lines()
        {
            var tr = Make(Tpl("Tenement {0}", "Доходный дом {0}"), Text("Tenement Resource", "Ресурс доходного дома"),
                          Tpl("Estimated value: {0} OC", "Примерная стоимость: {0} OC"));
            Assert.Equal("Ресурс доходного дома\nПримерная стоимость: 4 OC\n",
                         tr.Translate("Tenement Resource\nEstimated value: 4 OC\n"));
        }

        [Fact]
        public void Strong_template_keeps_short_names()
        {
            var tr = Make(Tpl("Invoice - {0}: {1} {2}", "Счёт — {0}: {1} {2}"), Text("Blackmail Letter", "Письмо с шантажом"),
                          Tpl("{0} at {1}", "{0}, {1}"));
            Assert.Equal("Счёт — Письмо с шантажом: 190 OC", tr.Translate("Invoice - Blackmail Letter: 190 OC"));
            Assert.Equal("Счёт — Only Bans: 50 RM", tr.Translate("Invoice - Only Bans: 50 RM"));
            // Слабый шаблон с непереведённой частью не применяется.
            Assert.Equal("Somebody at work", tr.Translate("Somebody at work"));
        }

        [Fact]
        public void Template_tries_long_parts_too()
        {
            var tr = Make(Tpl("{0} {1}/{2}", "{0} {1}/{2}"), Text("Grow turnips for Jomppe", "Вырастить репу для Jomppe"),
                          Tpl("{0} (Optional)", "{0} (необязательно)"));
            Assert.Equal("Вырастить репу для Jomppe 0/10", tr.Translate("Grow turnips for Jomppe 0/10"));
            Assert.Equal("Вырастить репу для Jomppe 0/10 (необязательно)", tr.Translate("Grow turnips for Jomppe 0/10 (Optional)"));
        }

        [Fact]
        public void Template_source_with_newline_matches_trimmed_text()
        {
            var catalog = new Catalog();
            catalog.Add(Tpl("Due in {0} days\n", "Срок через {0} дн.\n"));
            Assert.Equal("Срок через 6 дн.\n", new TextTranslator(catalog).Translate("Due in 6 days\n"));
        }

        [Fact]
        public void Input_field_zero_width_space_is_ignored()
        {
            // TMP_InputField дописывает U+200B в конец текста поля (окно записок).
            var tr = Make(Text("Bridge toll contact info", "Контакты по оплате проезда по мосту"));
            Assert.Equal("Контакты по оплате проезда по мосту​", tr.Translate("Bridge toll contact info​"));
        }

        [Fact]
        public void Edge_spaces_of_source_survive_translation()
        {
            var tr = Make(Text("Required skill: ", "Нужный навык:"), Text("Carpentry", "Плотничество"));
            Assert.Equal("Нужный навык: <b>Плотничество</b>", tr.Translate("Required skill: <b>Carpentry</b>"));
        }

        [Fact]
        public void Key_names_in_hints_stay_as_in_game()
        {
            var tr = Make(Text("Enter", "Войти"), Text("SEND", "ОТПРАВИТЬ"));
            Assert.Equal("Enter)ОТПРАВИТЬ", tr.Translate("Enter)SEND"));
        }

        [Fact]
        public void First_letter_case_follows_the_game()
        {
            var tr = Make(Text("internet connection", "интернет-соединение"));
            Assert.Equal("Интернет-соединение", tr.Translate("Internet connection"));
        }

        [Fact]
        public void Short_lists_are_translated_only_completely()
        {
            var tr = Make(Text("Aching", "всё ломит"), Text("Unsafe", "опасно"));
            Assert.Equal("всё ломит, опасно", tr.Translate("Aching, Unsafe"));
            Assert.Equal("Aching, Comfy", tr.Translate("Aching, Comfy"));
        }

        [Fact]
        public void Partial_results_are_reported_as_missed()
        {
            var catalog = new Catalog();
            catalog.Add(Text("Bread", "Хлеб"));
            var tr = new TextTranslator(catalog);
            var missed = new System.Collections.Generic.List<string>();
            tr.Missed = missed.Add;
            tr.Translate("Bread\nLeft");
            tr.Translate("Bread");
            Assert.Equal(new[] { "Bread\nLeft" }, missed);
        }

        [Fact]
        public void Template_rejects_unknown_slot_in_target()
        {
            Assert.Throws<System.IO.InvalidDataException>(() => new Template("Buy {0}", "Купить {1}"));
            Assert.Throws<System.IO.InvalidDataException>(() => new Template("Buy", "Купить"));
        }

        [Fact]
        public void Todo_entries_are_not_used()
        {
            var catalog = new Catalog();
            var file = new CatalogFile { Format = CatalogFile.CurrentFormat };
            file.Entries.Add(new Entry { Id = "a", Kind = Kinds.Text, Source = "Options", Target = "Параметры", Status = "todo" });
            file.Entries.Add(new Entry { Id = "b", Kind = Kinds.Text, Source = "Load Game", Target = "", Status = "translated" });
            catalog.AddFile(file, "test");
            Assert.Empty(catalog.Exact);
            Assert.Equal(2, catalog.Pending);
        }

        [Fact]
        public void Dialogue_lines_are_keyed_by_conversation_and_entry()
        {
            var catalog = new Catalog();
            catalog.Add(new Entry { Id = "d", Kind = Kinds.Dialogue, Conv = 7, EntryId = 3, Source = "Hi", Target = "Привет", Status = "translated" });
            catalog.Add(new Entry { Id = "m", Kind = Kinds.DialogueMenu, Conv = 7, EntryId = 3, Source = "Hi?", Target = "Привет?", Status = "review" });
            DialogueLine line = catalog.Dialogue[Catalog.DialogueKey(7, 3)];
            Assert.Equal("Привет", line.TextTarget);
            Assert.Equal("Привет?", line.MenuTarget);
            Assert.Empty(catalog.Exact);
        }
    }
}
