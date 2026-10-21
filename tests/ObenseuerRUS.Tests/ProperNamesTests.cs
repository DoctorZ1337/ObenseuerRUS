using ObenseuerRUS.Core;
using Xunit;

namespace ObenseuerRUS.Tests
{
    public class ProperNamesTests
    {
        private static Catalog Names()
        {
            var catalog = new Catalog();
            foreach (string[] p in new[]
            {
                new[] { "Arvo", "Арво" }, new[] { "Kirkastaja", "Киркастая" }, new[] { "Osmo", "Осмо" }, new[] { "Saarinen", "Сааринен" },
                new[] { "Pekka", "Пекка" }, new[] { "Aziz", "Азиз" }, new[] { "Mark", "Марк" }, new[] { "Welho", "Вельхо" },
                new[] { "Jomppe", "Йомппе" }, new[] { "Mr. Duck", "Мистер Дак" },
            })
                catalog.Add(new Entry { Id = "person/" + p[0], Kind = Kinds.Person, Source = p[0], Target = p[1], Status = "translated" });
            foreach (string[] b in new[]
            {
                new[] { "Osmo Olut", "Осмо Олут" }, new[] { "Lifehack-Pekka", "Лайфхак-Пекка" }, new[] { "Obenseuer", "Обенсеуер" },
                new[] { "Socca Junior", "Сокка Джуниор" },
            })
                catalog.Add(new Entry { Id = "brand/" + b[0], Kind = Kinds.Brand, Source = b[0], Target = b[1], Status = "translated" });
            return catalog;
        }

        private static string Apply(string text, bool people = true, bool brands = true) => ProperNames.For(Names(), people, brands).Apply(text);

        [Fact]
        public void Names_are_not_part_of_the_translation()
        {
            Catalog catalog = Names();
            Assert.Empty(catalog.Exact);
            Assert.Null(ProperNames.For(catalog, false, false));
            Assert.Equal("Arvo", new TextTranslator(catalog).Translate("Arvo"));
        }

        [Fact]
        public void Names_in_russian_text_and_labels()
        {
            Assert.Equal("Арво Киркастая хочет помочь.", Apply("Arvo Kirkastaja хочет помочь."));
            Assert.Equal("Арво Киркастая", Apply("Arvo Kirkastaja"));
            Assert.Equal("Мистер Дак?", Apply("Mr. Duck?"));
            Assert.Equal("ОБЕНСЕУЕР", Apply("OBENSEUER"));
        }

        [Fact]
        public void Untranslated_english_stays_english()
        {
            string s = "Talk to Arvo in Obenseuer";
            Assert.Same(s, Apply(s));
        }

        [Fact]
        public void Each_line_is_decided_separately()
        {
            // Консоль разработчика: английский журнал и русская строка в одном поле TMP.
            Assert.Equal("Getting identity for Arvo Kirkastaja\nпривет, Арво", Apply("Getting identity for Arvo Kirkastaja\nпривет, Arvo"));
        }

        [Fact]
        public void Paths_and_identifiers_are_left_alone()
        {
            Assert.Equal("Saves/Obenseuer/Елена", Apply("Saves/Obenseuer/Елена"));
            Assert.Equal("Mines_Arvo_Kirkastaja и Арво", Apply("Mines_Arvo_Kirkastaja и Arvo"));
        }

        [Fact]
        public void Groups_switch_independently()
        {
            Assert.Equal("фабрика Osmo Olut, где работал Осмо Сааринен", Apply("фабрика Osmo Olut, где работал Osmo Saarinen", brands: false));
            Assert.Equal("фабрика Осмо Олут, где работал Osmo Saarinen", Apply("фабрика Osmo Olut, где работал Osmo Saarinen", people: false));
            Assert.Equal("журнал Lifehack-Pekka", Apply("журнал Lifehack-Pekka", brands: false));
        }

        [Fact]
        public void Word_boundaries()
        {
            Assert.Equal("доставка от Aziz's и от Азиз", Apply("доставка от Aziz's и от Aziz"));
            Assert.Equal("Марку нужно было", Apply("Mark'у нужно было"));
            Assert.Equal("собрат-Вельхо", Apply("собрат-Welho"));
            Assert.Equal("отравленный Сокка Джуниор", Apply("отравленный Socca  Junior"));
            Assert.Equal("ArvoX и Арво", Apply("ArvoX и Arvo"));
        }

        [Fact]
        public void Tags_are_left_alone()
        {
            Assert.Equal("<link=\"Arvo\">Арво</link> пришёл", Apply("<link=\"Arvo\">Arvo</link> пришёл"));
        }

        [Fact]
        public void Translator_applies_names_after_translation()
        {
            Catalog catalog = Names();
            catalog.Add(new Entry { Id = "t", Kind = Kinds.Template, Source = "Grow turnips for {0}", Target = "Вырастить репу для {0}", Status = "translated" });
            var tr = new TextTranslator(catalog) { Names = ProperNames.For(catalog, true, false) };
            Assert.Equal("Вырастить репу для Йомппе", tr.Translate("Grow turnips for Jomppe"));
        }
    }
}
