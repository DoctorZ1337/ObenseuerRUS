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
