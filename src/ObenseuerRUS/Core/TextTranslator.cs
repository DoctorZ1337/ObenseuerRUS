using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ObenseuerRUS.Core
{
    /// <summary>
    /// Переводит готовую строку интерфейса. Порядок: точное совпадение, совпадение без пробелов по краям,
    /// без учёта регистра, шаблоны, затем по частям (строки, участки между rich-text тегами, "Имя: текст").
    /// Непереведённые части остаются как есть.
    /// </summary>
    public sealed class TextTranslator
    {
        private const int CacheLimit = 8192;
        private const int MaxDepth = 3;
        private static readonly Regex Tag = new Regex(@"<[^<>\n]{1,96}>", RegexOptions.CultureInvariant);
        // Строка управления: "C)DRAG & DROP   R)DROP   Tab)EXIT" — подсказки разделены несколькими пробелами.
        private static readonly Regex Gap = new Regex("( {2,})", RegexOptions.CultureInvariant);
        private static readonly Regex KeyHint = new Regex(@"^([^()\s]{1,16})\)(\S.*)$", RegexOptions.CultureInvariant);
        private const string TrailingPunctuation = "!?.:…";
        // Сколько частей с латиницей осталось без перевода — по нему видно, полный ли вышел перевод по шаблону.
        private int untranslatedParts, templateUses;
        private readonly Catalog catalog;
        private readonly List<Template> templates;
        private readonly Dictionary<string, string> cache = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <summary>Вызывается для строк с латиницей, у которых не нашлось перевода.</summary>
        public Action<string> Missed;

        /// <param name="forLua">true — для значений Lua-вставок диалогов: сначала шаблоны со scope "lua", затем общие.</param>
        public TextTranslator(Catalog catalog, bool forLua = false)
        {
            this.catalog = catalog;
            catalog.SortTemplates();
            templates = forLua ? new List<Template>(catalog.LuaTemplates) : catalog.Templates;
            if (forLua) templates.AddRange(catalog.Templates);
        }

        public void ClearCache() => cache.Clear();

        public string Translate(string text)
        {
            if (string.IsNullOrEmpty(text) || !HasLatinLetter(text)) return text;
            string result;
            if (cache.TryGetValue(text, out result)) return result;
            result = Core(text, 0) ?? text;
            if (cache.Count >= CacheLimit) cache.Clear();
            cache[text] = result;
            if (ReferenceEquals(result, text) && Missed != null) Missed(text);
            return result;
        }

        /// <summary>Возвращает перевод или null, если ничего не найдено.</summary>
        private string Core(string text, int depth)
        {
            string hit = Whole(text, depth);
            if (hit != null || depth >= MaxDepth) return hit;
            if (text.IndexOf('\n') >= 0) return Join(text.Split('\n'), "\n", depth);
            if (text.IndexOf('<') >= 0 && Tag.IsMatch(text)) return Runs(text, depth);
            int start = 0, end = text.Length;
            while (start < end && char.IsWhiteSpace(text[start])) start++;
            while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
            if (start == end) return null;
            if (start > 0 || end < text.Length)
            {
                string inner = Core(text.Substring(start, end - start), depth + 1);
                return inner == null ? null : text.Substring(0, start) + inner + text.Substring(end);
            }
            if (text.IndexOf("  ", StringComparison.Ordinal) > 0) return Join(Gap.Split(text), "", depth);
            Match key = KeyHint.Match(text);
            if (key.Success)
            {
                string label = Core(key.Groups[2].Value, depth + 1);
                if (label != null) return (Whole(key.Groups[1].Value, MaxDepth) ?? key.Groups[1].Value) + ")" + label;
            }
            // "(Steal)" — UseText сам добавляет скобки вокруг подписи из таблицы.
            if (text.Length > 2 && text[0] == '(' && text[text.Length - 1] == ')')
            {
                string inner = Core(text.Substring(1, text.Length - 2), depth + 1);
                if (inner != null) return "(" + inner + ")";
            }
            // "Thirst!" — PlayerStatUI дописывает знак к названию показателя.
            int stem = text.Length;
            while (stem > 0 && TrailingPunctuation.IndexOf(text[stem - 1]) >= 0) stem--;
            if (stem > 0 && stem < text.Length)
            {
                string inner = Core(text.Substring(0, stem), depth + 1);
                if (inner != null) return inner + text.Substring(stem);
            }
            int colon = text.IndexOf(": ", StringComparison.Ordinal);
            if (colon > 0 && colon <= 48)
            {
                string head = text.Substring(0, colon), tail = text.Substring(colon + 2);
                string th = Whole(head, MaxDepth), tt = tail.Length > 0 && HasLatinLetter(tail) ? Core(tail, depth + 1) : null;
                if (th != null || tt != null) return (th ?? head) + ": " + (tt ?? tail);
            }
            return null;
        }

        /// <summary>Перевод строки целиком: точный, без краевых пробелов, без учёта регистра, по шаблону.</summary>
        private string Whole(string text, int depth)
        {
            string t;
            if (catalog.Exact.TryGetValue(text, out t)) return t;
            int start = 0, end = text.Length;
            while (start < end && char.IsWhiteSpace(text[start])) start++;
            while (end > start && char.IsWhiteSpace(text[end - 1])) end--;
            if (start == end) return null;
            string core = text.Substring(start, end - start);
            string lead = text.Substring(0, start), trail = text.Substring(end);
            if (core.Length != text.Length && catalog.Exact.TryGetValue(core, out t)) return lead + t + trail;
            if (catalog.ExactIgnoreCase.TryGetValue(core, out t)) return lead + MatchCase(core, t) + trail;
            if (depth >= MaxDepth) return null;
            // Подходящих шаблонов бывает несколько ("Tenement {0}" и "{0} (Empty)" для "Tenement resources (Empty)").
            // Лучший — без непереведённых частей и с наименьшим числом вложенных шаблонов: его части нашлись в каталоге целиком.
            int startMisses = untranslatedParts, startUses = templateUses;
            string best = null;
            int bestMisses = int.MaxValue, bestUses = int.MaxValue;
            foreach (Template template in templates)
            {
                untranslatedParts = startMisses;
                templateUses = startUses;
                string r = template.TryApply(core, part =>
                {
                    if (!HasLatinLetter(part)) return part;
                    string tp = Core(part, depth + 1);
                    if (tp == null) untranslatedParts++;
                    return tp ?? part;
                });
                if (r == null) continue;
                int misses = untranslatedParts - startMisses, uses = templateUses - startUses;
                if (misses < bestMisses || (misses == bestMisses && uses < bestUses))
                {
                    best = r;
                    bestMisses = misses;
                    bestUses = uses;
                    if (misses == 0 && uses == 0) break;
                }
            }
            untranslatedParts = startMisses + (best == null ? 0 : bestMisses);
            templateUses = startUses + (best == null ? 0 : bestUses + 1);
            return best == null ? null : lead + best + trail;
        }

        private string Join(string[] parts, string separator, int depth)
        {
            bool changed = false;
            for (int i = 0; i < parts.Length; i++)
            {
                if (!HasLatinLetter(parts[i])) continue;
                string t = Core(parts[i], depth + 1);
                if (t == null)
                {
                    untranslatedParts++;
                    continue;
                }
                parts[i] = t;
                changed = true;
            }
            return changed ? string.Join(separator, parts) : null;
        }

        private string Runs(string text, int depth)
        {
            var sb = new StringBuilder(text.Length + 16);
            bool changed = false;
            int at = 0;
            foreach (Match m in Tag.Matches(text))
            {
                changed |= AppendRun(sb, text.Substring(at, m.Index - at), depth);
                sb.Append(m.Value);
                at = m.Index + m.Length;
            }
            changed |= AppendRun(sb, text.Substring(at), depth);
            return changed ? sb.ToString() : null;
        }

        private bool AppendRun(StringBuilder sb, string run, int depth)
        {
            string t = HasLatinLetter(run) ? Core(run, depth + 1) : null;
            if (t == null && HasLatinLetter(run)) untranslatedParts++;
            sb.Append(t ?? run);
            return t != null;
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
            return translation;
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
