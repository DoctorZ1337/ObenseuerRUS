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
        public void Real_catalog_translates_game_strings(string source, string expected)
        {
            var tr = new TextTranslator(Catalog.LoadDirectory(TranslationsDir()));
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
