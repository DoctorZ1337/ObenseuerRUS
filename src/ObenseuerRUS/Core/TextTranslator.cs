using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ObenseuerRUS.Core
{
    /// <summary>
    /// Переводит готовую строку интерфейса. Порядок: точное совпадение, совпадение без пробелов по краям,
    /// без учёта регистра, шаблоны, затем по частям (строки, участки между rich-text тегами, подсказки клавиш,
    /// "(подпись)", "Имя: текст", счётчик "3/10", перечисление через запятую), последним — без учёта правок грамматики (Catalog.Loose).
    /// Шаблон принимается, только если все его части переведены; шаблону с английским началом ("Invoice - {0}")
    /// можно оставить короткие имена и названия как есть. Так шаблон "{0} and {1}" не превращает
    /// чужой абзац в смесь английского и русского.
    /// </summary>
    public sealed class TextTranslator
    {
        private const int CacheLimit = 8192;
        private const int MaxDepth = 4;
        // Шаблоны — для коротких строк, собранных кодом; длинный текст переводится целиком или по строкам.
        private const int MaxTemplateText = 300;
        private static readonly Regex Tag = new Regex(@"<[^<>\n]{1,96}>", RegexOptions.CultureInvariant);
        // Строка управления: "C)DRAG & DROP   R)DROP   Tab)EXIT" — подсказки разделены несколькими пробелами.
        private static readonly Regex Gap = new Regex("( {2,})", RegexOptions.CultureInvariant);
        private static readonly Regex KeyHint = new Regex(@"^([^()\s]{1,16})\)(\S.*)$", RegexOptions.CultureInvariant);
        // "Grow turnips for Jomppe 0/10" — ObjectiveUI дописывает счётчик к названию цели; "Cucumber Seeds (5)" — CollectibleItem число предметов.
        private static readonly Regex Counter = new Regex(@"^(\S.*\S) (\d+/\d+|\(\d+\))$", RegexOptions.CultureInvariant);
        // Для журнала непереведённого: слово из двух и более латинских букв (коды вроде "OC" тоже попадут — журнал отладочный).
        private static readonly Regex LatinWord = new Regex("[A-Za-z]{2,}", RegexOptions.CultureInvariant);
        private const string TrailingPunctuation = "!?.:…";
        private static readonly char[] TrailingChars = TrailingPunctuation.ToCharArray();
        private static readonly string[] ListSeparator = { ", " };

        /// <summary>Перевод части строки: Misses — сколько кусков с латиницей осталось без перевода, Uses — сколько применено шаблонов.</summary>
        private sealed class Result
        {
            public readonly string Text;
            public readonly int Misses, Uses;

            public Result(string text, int misses, int uses)
            {
                Text = text;
                Misses = misses;
                Uses = uses;
            }
        }

        private readonly Catalog catalog;
        private readonly Dictionary<string, List<Template>> scopeTemplates = new Dictionary<string, List<Template>>(StringComparer.Ordinal);
        // Scope текущего вызова Translate: свои строки (или null) и шаблоны (свои, затем общие).
        private Dictionary<string, string> scoped;
        private List<Template> templates;
        private readonly Dictionary<string, string> cache = new Dictionary<string, string>(StringComparer.Ordinal);
        // Части одной строки переводятся много раз (каждый шаблон пробует свои), поэтому внутри Translate они запоминаются.
        private readonly Dictionary<string, Result> memo = new Dictionary<string, Result>(StringComparer.Ordinal);

        /// <summary>Вызывается для строк, где остался непереведённый английский текст (целиком или частично).</summary>
        public Action<string> Missed;

        /// <summary>Кириллица для имён и названий поверх перевода; null — имена как в игре. После замены нужен ClearCache.</summary>
        public ProperNames Names;

        public TextTranslator(Catalog catalog)
        {
            this.catalog = catalog;
            catalog.SortTemplates();
            templates = catalog.Templates;
        }

        public void ClearCache() => cache.Clear();

        /// <summary>Есть ли в каталоге строки или шаблоны с таким scope.</summary>
        public bool HasScope(string scope) => catalog.ScopedExact.ContainsKey(scope) || catalog.ScopedTemplates.ContainsKey(scope);

        public string Translate(string text) => Translate(text, null);

        /// <param name="scope">null — общий перевод; иначе сначала строки и шаблоны этого scope (см. Entry.Scope), затем общие.</param>
        public string Translate(string text, string scope)
        {
            if (string.IsNullOrEmpty(text) || !HasLatinLetter(text)) return text;
            string result, key = scope == null ? text : scope + "\u0002" + text;
            if (cache.TryGetValue(key, out result)) return result;
            UseScope(scope);
            Result r;
            try
            {
                r = Core(text, 0);
            }
            finally
            {
                memo.Clear();
            }
            result = r?.Text ?? text;
            if (Names != null) result = Names.Apply(result);
            if (cache.Count >= CacheLimit) cache.Clear();
            cache[key] = result;
            if ((r == null || r.Misses > 0) && Missed != null && LatinWord.IsMatch(Tag.Replace(result, " "))) Missed(text);
            return result;
        }

        private void UseScope(string scope)
        {
            scoped = null;
            templates = catalog.Templates;
            if (scope == null) return;
            catalog.ScopedExact.TryGetValue(scope, out scoped);
            List<Template> own;
            if (!catalog.ScopedTemplates.TryGetValue(scope, out own)) return;
            if (!scopeTemplates.TryGetValue(scope, out templates))
            {
                scopeTemplates[scope] = templates = new List<Template>(own);
                templates.AddRange(catalog.Templates);
            }
        }

        private bool Lookup(string text, out string translation)
        {
            return (scoped != null && scoped.TryGetValue(text, out translation)) || catalog.Exact.TryGetValue(text, out translation);
        }

        /// <summary>Перевод или null, если не переведено ничего.</summary>
        private Result Core(string text, int depth)
        {
            string key = depth.ToString() + "\u0001" + text;
            Result r;
            if (memo.TryGetValue(key, out r)) return r;
            r = Whole(text, depth);
            if ((r == null || r.Misses > 0) && depth < MaxDepth) r = Better(r, Split(text, depth));
            if (r == null || r.Misses > 0) r = Loose(text) ?? r;
            memo[key] = r;
            return r;
        }

        /// <summary>Перевод строки целиком: точный, без краевых пробелов, без учёта регистра, по шаблону.</summary>
        private Result Whole(string text, int depth)
        {
            string t;
            if (Lookup(text, out t)) return new Result(t, 0, 0);
            int start = 0, end = text.Length;
            while (start < end && IsEdge(text[start])) start++;
            while (end > start && IsEdge(text[end - 1])) end--;
            if (start == end) return null;
            string core = text.Substring(start, end - start);
            string lead = text.Substring(0, start), trail = text.Substring(end);
            if (core.Length != text.Length && Lookup(core, out t)) return new Result(lead + t + trail, 0, 0);
            // Игра часто делает ToLower() ("shabby", "crude"): сначала то же слово с заглавной буквы, а не любой регистр
            // (иначе "sun" находит "SUN" — воскресенье в часах).
            if (char.IsLower(core[0]) && Lookup(char.ToUpperInvariant(core[0]) + core.Substring(1), out t)) return new Result(lead + MatchCase(core, t) + trail, 0, 0);
            if (catalog.ExactIgnoreCase.TryGetValue(core, out t)) return new Result(lead + MatchCase(core, t) + trail, 0, 0);
            // "(No mail to drop)" — UseText сам ставит скобки: фраза из каталога целиком важнее разбора по шаблону "({0} to {1})".
            if (core.Length > 2 && core[0] == '(' && core[core.Length - 1] == ')' && Lookup(core.Substring(1, core.Length - 2), out t))
                return new Result(lead + "(" + t + ")" + trail, 0, 0);
            // Подходящих шаблонов бывает несколько ("Tenement {0}" и "{0} (Empty)" для "Tenement resources (Empty)").
            // Лучший — без непереведённых частей и с наименьшим числом вложенных шаблонов.
            Result best = null;
            if (depth < MaxDepth && core.Length <= MaxTemplateText) best = FromTemplates(core, lead, trail, depth);
            if (best != null && best.Misses == 0) return best;
            // Строка многострочной записи — после шаблонов: "Integrity: {0}" важнее строки "Integrity: 100%" из макета окна.
            if (catalog.Lines.TryGetValue(core, out t)) return new Result(lead + t + trail, 0, 0);
            return best;
        }

        /// <summary>
        /// Та же строка с правками грамматики после обновления игры ("It's a rat meat" -> "It's rat meat").
        /// Последний вариант: ключ не видит знаков, поэтому скобки, счётчики и "Имя: текст" сначала разбирает Split.
        /// </summary>
        private Result Loose(string text)
        {
            int start = 0, end = text.Length;
            while (start < end && IsEdge(text[start])) start++;
            while (end > start && IsEdge(text[end - 1])) end--;
            string key = Catalog.LooseKey(text.Substring(start, end - start)), t;
            if (key == null || !catalog.Loose.TryGetValue(key, out t) || t == null) return null;
            return new Result(text.Substring(0, start) + t + text.Substring(end), 0, 0);
        }

        private Result FromTemplates(string core, string lead, string trail, int depth)
        {
            Result best = null;
            foreach (Template template in templates)
            {
                foreach (Match match in template.Matches(core))
                {
                    int misses = 0, uses = 0;
                    bool shortParts = true;
                    string built = template.Build(match, part =>
                    {
                        if (!HasLatinLetter(part)) return part;
                        Result tp = Core(part, depth + 1);
                        if (tp == null)
                        {
                            if (IsCode(part)) return part;
                            misses++;
                            shortParts &= IsShortName(part);
                            return part;
                        }
                        if (tp.Misses > 0) shortParts &= IsShortName(part);
                        misses += tp.Misses;
                        uses += tp.Uses;
                        return tp.Text;
                    });
                    if (misses > 0 && !(template.Strong && shortParts)) continue;
                    best = Better(best, new Result(lead + built + trail, misses, uses + 1));
                    if (misses == 0 && uses == 0) return best;
                }
            }
            return best;
        }

        /// <summary>Перевод по частям, если строка целиком не нашлась.</summary>
        private Result Split(string text, int depth)
        {
            if (text.IndexOf('\n') >= 0) return Join(text.Split('\n'), "\n", depth);
            if (text.IndexOf('<') >= 0 && Tag.IsMatch(text)) return Runs(text, depth);
            int start = 0, end = text.Length;
            while (start < end && IsEdge(text[start])) start++;
            while (end > start && IsEdge(text[end - 1])) end--;
            if (start == end) return null;
            if (start > 0 || end < text.Length)
            {
                return Wrap(text.Substring(0, start), Core(text.Substring(start, end - start), depth + 1), text.Substring(end));
            }
            if (text.IndexOf("  ", StringComparison.Ordinal) > 0) return Join(Gap.Split(text), "", depth);
            // Дальше правила могут пересекаться ("Job: KEBAB MASTER!" — и знак в конце, и "Имя: текст"):
            // пробуются все, первый полный перевод побеждает, иначе — лучший частичный.
            Result best = null;
            Match key = KeyHint.Match(text);
            if (key.Success)
            {
                // Обозначение клавиши ("Tab", "Enter", "LMB") остаётся как в игре: это название кнопки, а не текст.
                Result label = Core(key.Groups[2].Value, depth + 1);
                if (label != null && Take(ref best, new Result(key.Groups[1].Value + ")" + label.Text, label.Misses, label.Uses))) return best;
            }
            // "(Steal)" — UseText сам добавляет скобки вокруг подписи из таблицы.
            if (text.Length > 2 && text[0] == '(' && text[text.Length - 1] == ')')
            {
                if (Take(ref best, Wrap("(", Core(text.Substring(1, text.Length - 2), depth + 1), ")"))) return best;
            }
            // "Thirst!" — PlayerStatUI дописывает знак к названию показателя.
            int stem = text.Length;
            while (stem > 0 && TrailingPunctuation.IndexOf(text[stem - 1]) >= 0) stem--;
            if (stem > 0 && stem < text.Length)
            {
                // Знак игры заменяет знак в конце перевода, а не добавляется к нему: "...in the world!" -> "...на свете!", а не "свете.!".
                Result inner = Core(text.Substring(0, stem), depth + 1);
                if (inner != null) inner = new Result(inner.Text.TrimEnd(TrailingChars), inner.Misses, inner.Uses);
                if (Take(ref best, Wrap("", inner, text.Substring(stem)))) return best;
            }
            Match counter = Counter.Match(text);
            if (counter.Success)
            {
                if (Take(ref best, Wrap("", Core(counter.Groups[1].Value, depth + 1), " " + counter.Groups[2].Value))) return best;
            }
            int colon = text.IndexOf(": ", StringComparison.Ordinal);
            if (colon > 0 && colon <= 48)
            {
                string head = text.Substring(0, colon), tail = text.Substring(colon + 2);
                Result th = Whole(head, MaxDepth), tt = tail.Length > 0 && HasLatinLetter(tail) ? Core(tail, depth + 1) : null;
                if (th != null || tt != null)
                {
                    int misses = (th == null ? Miss(head) : 0) + (tt == null ? Miss(tail) : tt.Misses);
                    if (Take(ref best, new Result((th?.Text ?? head) + ": " + (tt?.Text ?? tail), misses, tt?.Uses ?? 0))) return best;
                }
            }
            // "Aching, Unsafe" — перечисление коротких подписей; переводится, только если переведено всё.
            if (text.IndexOf(", ", StringComparison.Ordinal) > 0) Take(ref best, List(text, depth));
            return best;
        }

        /// <summary>Запоминает лучший вариант; true — вариант полный, искать дальше не нужно.</summary>
        private static bool Take(ref Result best, Result candidate)
        {
            best = Better(best, candidate);
            return candidate != null && candidate.Misses == 0;
        }

        private Result List(string text, int depth)
        {
            string[] items = text.Split(ListSeparator, StringSplitOptions.None);
            int uses = 0;
            for (int i = 0; i < items.Length; i++)
            {
                if (!HasLatinLetter(items[i]) || IsCode(items[i])) continue;
                Result r = Core(items[i], depth + 1);
                if (r == null || r.Misses > 0) return null;
                items[i] = r.Text;
                uses += r.Uses;
            }
            return new Result(string.Join(ListSeparator[0], items), 0, uses);
        }

        private Result Join(string[] parts, string separator, int depth)
        {
            bool changed = false;
            int misses = 0, uses = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                if (!HasLatinLetter(parts[i])) continue;
                Result r = Core(parts[i], depth + 1);
                if (r == null)
                {
                    misses += Miss(parts[i]);
                    continue;
                }
                parts[i] = r.Text;
                misses += r.Misses;
                uses += r.Uses;
                changed = true;
            }
            return changed ? new Result(string.Join(separator, parts), misses, uses) : null;
        }

        private Result Runs(string text, int depth)
        {
            var sb = new StringBuilder(text.Length + 16);
            bool changed = false;
            int at = 0, misses = 0, uses = 0;
            foreach (Match m in Tag.Matches(text))
            {
                changed |= AppendRun(sb, text.Substring(at, m.Index - at), depth, ref misses, ref uses);
                sb.Append(m.Value);
                at = m.Index + m.Length;
            }
            changed |= AppendRun(sb, text.Substring(at), depth, ref misses, ref uses);
            return changed ? new Result(sb.ToString(), misses, uses) : null;
        }

        private bool AppendRun(StringBuilder sb, string run, int depth, ref int misses, ref int uses)
        {
            Result r = HasLatinLetter(run) ? Core(run, depth + 1) : null;
            if (r == null)
            {
                misses += Miss(run);
                sb.Append(run);
                return false;
            }
            misses += r.Misses;
            uses += r.Uses;
            sb.Append(r.Text);
            return true;
        }

        private static Result Wrap(string before, Result inner, string after)
        {
            return inner == null ? null : new Result(before + inner.Text + after, inner.Misses, inner.Uses);
        }

        private static Result Better(Result a, Result b)
        {
            if (a == null) return b;
            if (b == null) return a;
            if (b.Misses != a.Misses) return b.Misses < a.Misses ? b : a;
            return b.Uses < a.Uses ? b : a;
        }

        /// <summary>Пробелы по краям и невидимый U+200B, который TMP_InputField дописывает в конец текста поля.</summary>
        private static bool IsEdge(char c) => char.IsWhiteSpace(c) || c == '\u200B' || c == '\uFEFF';

        private static int Miss(string s) => HasLatinLetter(s) && !IsCode(s) ? 1 : 0;

        /// <summary>Коды и обозначения без перевода: "OC", "RM", "A8", "#12" — не больше трёх латинских букв и ни одной строчной.</summary>
        private static bool IsCode(string s)
        {
            int letters = 0;
            foreach (char c in s)
            {
                if (c >= 'a' && c <= 'z') return false;
                if (c >= 'A' && c <= 'Z') letters++;
            }
            return letters <= 3;
        }

        /// <summary>Короткое имя или название ("Blackmail Letter", "Jomppe Kurahaara"), а не фраза.</summary>
        private static bool IsShortName(string s)
        {
            s = s.Trim();
            if (s.Length == 0 || s.Length > 40 || s.IndexOfAny(new[] { '.', '!', '?' }) >= 0) return false;
            return s.Split(' ').Length <= 4;
        }

        /// <summary>Игра иногда меняет регистр (ToLower/ToUpper) — переносим его на перевод.</summary>
        private static string MatchCase(string original, string translation)
        {
            bool anyLetter = false, allUpper = true, allLower = true;
            foreach (char c in original)
            {
                if (!char.IsLetter(c)) continue;
                anyLetter = true;
                if (char.IsUpper(c)) allLower = false;
                if (char.IsLower(c)) allUpper = false;
            }
            if (!anyLetter) return translation;
            if (allUpper && original.Length > 1) return translation.ToUpperInvariant();
            if (allLower) return translation.ToLowerInvariant();
            // "Internet connection" (игра подняла первую букву) при переводе "internet connection" — первая буква, как у игры.
            int o = FirstLetter(original), t = FirstLetter(translation);
            if (o >= 0 && t >= 0 && char.IsUpper(original[o]) && char.IsLower(translation[t]))
                return translation.Substring(0, t) + char.ToUpperInvariant(translation[t]) + translation.Substring(t + 1);
            return translation;
        }

        private static int FirstLetter(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsLetter(s[i])) return i;
            }
            return -1;
        }

        public static bool HasLatinLetter(string s)
        {
            foreach (char c in s)
            {
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) return true;
            }
            return false;
        }
    }
}
