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
        [InlineData("Account: 250 OC", "Счёт: 250 OC")]
        [InlineData("BRICK", "КИРПИЧ")]
        [InlineData("Pod 18", "Капсула 18")]
        [InlineData("Tenement resources (Empty)", "Ресурсы доходного дома (пусто)")]
        [InlineData("(Steal)", "(Украсть)")]
        [InlineData("Thirst!", "Жажда!")]
        [InlineData("C)DRAG & DROP   R)DROP   Q)SWITCH TAB   X)USE ITEM   Tab)EXIT", "C)ПЕРЕТАЩИТЬ   R)ВЫБРОСИТЬ   Q)СМЕНИТЬ ВКЛАДКУ   X)ИСПОЛЬЗОВАТЬ   Tab)ВЫХОД")]
        [InlineData("You're very addicted to alcohol and nicotine.", "Сильная зависимость: алкоголь и никотин.")]
        // Строки со скриншотов 0.3.1, где перевод был частичным или отсутствовал.
        [InlineData("Tenement Resource\nA sawed piece of wood\nEstimated value: 4 OC\n\nFuel for: 1h\n", "Ресурс доходного дома\nA sawed piece of wood\nПримерная стоимость: 4 OC\n\nТопливо на: 1 ч\n")]
        [InlineData("Due in 6 days\n", "Срок через 6 дн.\n")]
        [InlineData("Invoice - Blackmail Letter: 190 OC", "Счёт — Письмо с шантажом: 190 OC")]
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
        public void Real_names_are_written_in_cyrillic_when_enabled(string source, string expected)
        {
            Catalog catalog = Catalog.LoadDirectory(TranslationsDir());
            Assert.NotEmpty(catalog.People);
            Assert.NotEmpty(catalog.Brands);
            var tr = new TextTranslator(catalog) { Names = ProperNames.For(catalog, true, true) };
            Assert.Equal(expected, tr.Translate(source));
        }

        [Fact]
        public void Lua_scope_templates_apply_only_to_lua_values()
        {
            Catalog catalog = Catalog.LoadDirectory(TranslationsDir());
            Assert.Equal("Понедельник, 14", new TextTranslator(catalog, forLua: true).Translate("Monday at 14"));
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
