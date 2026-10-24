using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json;

namespace ObenseuerRUS.Core
{
    /// <summary>Одна строка перевода из translations/*.json.</summary>
    public sealed class Entry
    {
        [JsonProperty("id")] public string Id;
        // text | template | table | dialogue | dialogue-menu | person | brand
        [JsonProperty("kind")] public string Kind;
        [JsonProperty("source")] public string Source;
        [JsonProperty("target")] public string Target;
        // translated | review | todo
        [JsonProperty("status")] public string Status;
        [JsonProperty("context")] public string Context;
        [JsonProperty("note")] public string Note;
        // Где действует строка или шаблон (иначе — везде): "lua" — значения Lua-вставок диалогов (функция Tr),
        // "action" — подписи действий предметов (кнопки окна предмета, подсказка UseText), "usetitle" — заголовок объекта в UseText,
        // "object:<родитель>/<объект>" — текст одного объекта TextMeshPro. Сначала ищется перевод своего scope, затем общий.
        [JsonProperty("scope", NullValueHandling = NullValueHandling.Ignore)] public string Scope;
        [JsonProperty("table", NullValueHandling = NullValueHandling.Ignore)] public string Table;
        [JsonProperty("key", NullValueHandling = NullValueHandling.Ignore)] public string Key;
        [JsonProperty("conv", NullValueHandling = NullValueHandling.Ignore)] public int? Conv;
        [JsonProperty("entry", NullValueHandling = NullValueHandling.Ignore)] public int? EntryId;
    }

    public sealed class CatalogFile
    {
        public const string CurrentFormat = "obenseuer-rus/1";
        [JsonProperty("format")] public string Format;
        [JsonProperty("category")] public string Category;
        [JsonProperty("sourceLanguage")] public string SourceLanguage;
        [JsonProperty("targetLanguage")] public string TargetLanguage;
        [JsonProperty("gameBuild")] public string GameBuild;
        [JsonProperty("entries")] public List<Entry> Entries = new List<Entry>();
    }

    /// <summary>Перевод одной реплики базы диалогов: оригинал нужен, чтобы не наложить перевод на изменённую игрой строку.</summary>
    public sealed class DialogueLine
    {
        public string TextSource, TextTarget, MenuSource, MenuTarget;
    }

    public static class Kinds
    {
        public const string Text = "text", Template = "template", Table = "table", Dialogue = "dialogue", DialogueMenu = "dialogue-menu";
        // Необязательная кириллица для имён людей и для брендов, заведений и мест (см. ProperNames).
        public const string Person = "person", Brand = "brand";
        public static bool IsKnown(string k) => k == Text || k == Template || k == Table || k == Dialogue || k == DialogueMenu || k == Person || k == Brand;
    }

    public sealed class Catalog
    {
        public readonly Dictionary<string, string> Exact = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> ExactIgnoreCase = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        /// <summary>Отдельные строки многострочных записей; переводчик берёт их, только если не подошёл шаблон.</summary>
        public readonly Dictionary<string, string> Lines = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>
        /// Переводы по LooseKey — для строк, где игра поправила только грамматику: артикли, регистр, дефисы, знаки
        /// ("It's a rat meat" -> "It's rat meat"). null — у ключа несколько разных переводов, такой ключ не используется.
        /// </summary>
        public readonly Dictionary<string, string> Loose = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly List<Template> Templates = new List<Template>();
        public readonly Dictionary<string, List<Template>> ScopedTemplates = new Dictionary<string, List<Template>>(StringComparer.Ordinal);
        public readonly Dictionary<string, Dictionary<string, string>> ScopedExact = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        public readonly Dictionary<long, DialogueLine> Dialogue = new Dictionary<long, DialogueLine>();
        public readonly Dictionary<string, Dictionary<string, string>> Tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> People = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly Dictionary<string, string> Brands = new Dictionary<string, string>(StringComparer.Ordinal);
        public readonly List<string> Problems = new List<string>();
        public int Used, Pending;

        public static long DialogueKey(int conv, int entry) => ((long)conv << 32) | (uint)entry;

        /// <summary>
        /// Слова текста в нижнем регистре без артиклей a/an/the, через пробел. null — меньше трёх слов:
        /// короткие подписи сравниваются только точно, у них правка буквы может менять смысл.
        /// </summary>
        public static string LooseKey(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var sb = new StringBuilder(text.Length);
            int words = 0, i = 0;
            while (i < text.Length)
            {
                if (!char.IsLetterOrDigit(text[i]))
                {
                    i++;
                    continue;
                }
                int start = i;
                while (i < text.Length && char.IsLetterOrDigit(text[i])) i++;
                string word = text.Substring(start, i - start).ToLowerInvariant();
                if (word == "a" || word == "an" || word == "the") continue;
                if (words++ > 0) sb.Append(' ');
                sb.Append(word);
            }
            return words >= 3 ? sb.ToString() : null;
        }

        /// <summary>Тот же текст или тот же с правками только грамматики (см. LooseKey).</summary>
        public static bool SameText(string a, string b)
        {
            if (a == b) return true;
            string key = LooseKey(a);
            return key != null && key == LooseKey(b);
        }

        /// <summary>Перевод пригоден к применению: есть текст и статус не "todo".</summary>
        public static bool IsUsable(Entry e) => e != null && !string.IsNullOrEmpty(e.Target) && e.Status != "todo";

        public static Catalog LoadDirectory(string dir)
        {
            var catalog = new Catalog();
            if (!Directory.Exists(dir))
            {
                catalog.Problems.Add("нет папки " + dir);
                return catalog;
            }
            string[] files = Directory.GetFiles(dir, "*.json");
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            foreach (string file in files)
            {
                try
                {
                    catalog.AddFile(ReadFile(file), Path.GetFileName(file));
                }
                catch (Exception e)
                {
                    catalog.Problems.Add(Path.GetFileName(file) + ": " + e.Message);
                }
            }
            return catalog;
        }

        public static CatalogFile ReadFile(string path)
        {
            string json = File.ReadAllText(path, new UTF8Encoding(false, true));
            var file = JsonConvert.DeserializeObject<CatalogFile>(json);
            if (file == null) throw new InvalidDataException("пустой файл");
            if (file.Format != CatalogFile.CurrentFormat) throw new InvalidDataException("неизвестный формат " + file.Format);
            return file;
        }

        public void AddFile(CatalogFile file, string name)
        {
            foreach (Entry e in file.Entries)
            {
                if (!IsUsable(e))
                {
                    Pending++;
                    continue;
                }
                try
                {
                    Add(e);
                    Used++;
                }
                catch (Exception ex)
                {
                    Problems.Add(name + ": " + e.Id + ": " + ex.Message);
                }
            }
        }

        public void Add(Entry e)
        {
            switch (e.Kind)
            {
                case Kinds.Text:
                    if (e.Scope == null)
                    {
                        AddExact(e.Source, e.Target);
                        break;
                    }
                    if (string.IsNullOrEmpty(e.Source)) throw new InvalidDataException("пустой source");
                    Dictionary<string, string> scoped;
                    if (!ScopedExact.TryGetValue(e.Scope, out scoped)) ScopedExact[e.Scope] = scoped = new Dictionary<string, string>(StringComparer.Ordinal);
                    if (!scoped.ContainsKey(e.Source)) scoped[e.Source] = e.Target;
                    if (!scoped.ContainsKey(e.Source.Trim())) scoped[e.Source.Trim()] = e.Target.Trim();
                    break;
                case Kinds.Template:
                    List<Template> list = Templates;
                    if (e.Scope != null && !ScopedTemplates.TryGetValue(e.Scope, out list)) ScopedTemplates[e.Scope] = list = new List<Template>();
                    list.Add(new Template(e.Source, e.Target));
                    // Переводчик сравнивает строку без краевых пробелов и переводов строк ("Due in {0} days\n").
                    string trimmed = e.Source.Trim();
                    if (trimmed != e.Source && trimmed.Length > 0) list.Add(new Template(trimmed, (e.Target ?? "").Trim()));
                    break;
                case Kinds.Table:
                    if (string.IsNullOrEmpty(e.Table) || e.Key == null) throw new InvalidDataException("нет table/key");
                    Dictionary<string, string> table;
                    if (!Tables.TryGetValue(e.Table, out table)) Tables[e.Table] = table = new Dictionary<string, string>(StringComparer.Ordinal);
                    table[e.Key] = e.Target;
                    AddExact(e.Source, e.Target);
                    break;
                case Kinds.Dialogue:
                case Kinds.DialogueMenu:
                    if (e.Conv == null || e.EntryId == null) throw new InvalidDataException("нет conv/entry");
                    long key = DialogueKey(e.Conv.Value, e.EntryId.Value);
                    DialogueLine line;
                    if (!Dialogue.TryGetValue(key, out line)) Dialogue[key] = line = new DialogueLine();
                    if (e.Kind == Kinds.Dialogue)
                    {
                        line.TextSource = e.Source;
                        line.TextTarget = e.Target;
                    }
                    else
                    {
                        line.MenuSource = e.Source;
                        line.MenuTarget = e.Target;
                    }
                    break;
                case Kinds.Person:
                case Kinds.Brand:
                    // В Exact не попадают: без включённой настройки имена остаются латиницей.
                    if (string.IsNullOrEmpty(e.Source)) throw new InvalidDataException("пустой source");
                    (e.Kind == Kinds.Person ? People : Brands)[e.Source] = e.Target;
                    break;
                default:
                    throw new InvalidDataException("неизвестный kind " + e.Kind);
            }
        }

        private void AddExact(string source, string target)
        {
            if (string.IsNullOrEmpty(source)) throw new InvalidDataException("пустой source");
            // Код склеивает "Required skill: " + навык: пробелы по краям оригинала должны остаться и в переводе.
            string lead = source.Substring(0, source.Length - source.TrimStart().Length), trail = source.Substring(source.TrimEnd().Length);
            if (lead.Length + trail.Length > 0 && target.Trim().Length > 0) target = lead + target.Trim() + trail;
            // Первый загруженный перевод побеждает: файлы читаются по алфавиту.
            Put(source, target);
            // Код игры часто склеивает "\nAny amount" или "Account: " + число; переводчик ищет части без краевых
            // пробелов и без двоеточия на конце, поэтому добавляем и такие ключи.
            string s = source.Trim(), t = target.Trim();
            if (s.Length > 0 && s != source) Put(s, t);
            if (s.Length > 1 && s.EndsWith(":", StringComparison.Ordinal) && t.EndsWith(":", StringComparison.Ordinal)) Put(s.TrimEnd(':').TrimEnd(), t.TrimEnd(':').TrimEnd());
            PutLoose(s, t);
            // TaskPanel склеивает описание задания с описаниями целей через "\n\n", а переводчик делит такой текст по строкам,
            // поэтому строки многострочной записи нужны и по отдельности (если перевод разбит на строки так же).
            if (s.IndexOf('\n') < 0) return;
            string[] sl = s.Split('\n'), tl = t.Split('\n');
            if (sl.Length != tl.Length) return;
            for (int i = 0; i < sl.Length; i++)
            {
                string ls = sl[i].Trim(), lt = tl[i].Trim();
                if (ls.Length == 0 || lt.Length == 0) continue;
                if (!Lines.ContainsKey(ls)) Lines[ls] = lt;
                PutLoose(ls, lt);
            }
        }

        private void PutLoose(string source, string target)
        {
            string key = LooseKey(source);
            if (key == null) return;
            string known;
            if (!Loose.TryGetValue(key, out known)) Loose[key] = target;
            else if (known != null && known != target) Loose[key] = null;
        }

        private void Put(string source, string target)
        {
            if (!Exact.ContainsKey(source)) Exact[source] = target;
            if (!ExactIgnoreCase.ContainsKey(source)) ExactIgnoreCase[source] = target;
        }

        /// <summary>Шаблоны с более длинной неизменяемой частью проверяются первыми.</summary>
        public void SortTemplates()
        {
            Templates.Sort((a, b) => b.LiteralLength.CompareTo(a.LiteralLength));
            foreach (List<Template> list in ScopedTemplates.Values) list.Sort((a, b) => b.LiteralLength.CompareTo(a.LiteralLength));
        }
    }
}
