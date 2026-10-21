using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace ObenseuerRUS.Core
{
    /// <summary>
    /// Шаблон для строк, которые игра собирает из частей, например "Status: {0}".
    /// {0}..{9} в source — изменяемые части; в target те же номера, порядок можно менять.
    /// Часть шаблона не захватывает перевод строки: многострочный текст переводится построчно.
    /// </summary>
    public sealed class Template
    {
        private static readonly Regex Slot = new Regex(@"\{(\d)\}", RegexOptions.CultureInvariant);
        private const int StrongPrefixLetters = 4;
        public readonly string Source, Target, Prefix, LongestLiteral;
        public readonly int LiteralLength, SlotCount;
        /// <summary>
        /// Шаблон начинается с английского слова ("Invoice - {0}", "Owned - {0}"): он подходит только строкам с этим началом,
        /// поэтому короткие непереведённые части (имена, названия) в нём допустимы. Остальным ("{0} and {1}") нужен полный перевод частей.
        /// </summary>
        public readonly bool Strong;
        private readonly Regex lazy, greedy;

        public Template(string source, string target)
        {
            if (string.IsNullOrEmpty(source)) throw new InvalidDataException("пустой шаблон");
            Source = source;
            Target = target;
            var lazyPattern = new StringBuilder("^");
            var greedyPattern = new StringBuilder("^");
            var seen = new HashSet<int>();
            int at = 0;
            string longest = "";
            foreach (Match m in Slot.Matches(source))
            {
                string literal = source.Substring(at, m.Index - at);
                if (literal.Length > longest.Length) longest = literal;
                LiteralLength += literal.Length;
                lazyPattern.Append(Regex.Escape(literal));
                greedyPattern.Append(Regex.Escape(literal));
                int n = m.Groups[1].Value[0] - '0';
                if (!seen.Add(n)) throw new InvalidDataException("повтор {" + n + "} в шаблоне");
                lazyPattern.Append("(?<s").Append(n).Append(">[^\\n]+?)");
                greedyPattern.Append("(?<s").Append(n).Append(">[^\\n]+)");
                at = m.Index + m.Length;
            }
            string tail = source.Substring(at);
            if (tail.Length > longest.Length) longest = tail;
            LiteralLength += tail.Length;
            lazyPattern.Append(Regex.Escape(tail)).Append('$');
            greedyPattern.Append(Regex.Escape(tail)).Append('$');
            SlotCount = seen.Count;
            if (SlotCount == 0) throw new InvalidDataException("в шаблоне нет {0}");
            foreach (Match m in Slot.Matches(target ?? ""))
            {
                if (!seen.Contains(m.Groups[1].Value[0] - '0')) throw new InvalidDataException("в переводе лишний " + m.Value);
            }
            Match first = Slot.Match(source);
            Prefix = source.Substring(0, first.Index);
            LongestLiteral = longest;
            Strong = LatinLetters(Prefix) >= StrongPrefixLetters;
            lazy = new Regex(lazyPattern.ToString(), RegexOptions.CultureInvariant);
            greedy = new Regex(greedyPattern.ToString(), RegexOptions.CultureInvariant);
        }

        /// <summary>
        /// Разбиения строки по шаблону: сначала с короткими частями, затем с длинными
        /// ("Grow turnips for Jomppe 0/10" по "{0} {1}/{2}" правильно делится только вторым способом).
        /// </summary>
        public IEnumerable<Match> Matches(string text)
        {
            if (Prefix.Length > 0 && !text.StartsWith(Prefix, StringComparison.Ordinal)) yield break;
            if (LongestLiteral.Length > 0 && text.IndexOf(LongestLiteral, StringComparison.Ordinal) < 0) yield break;
            Match a = lazy.Match(text);
            if (!a.Success) yield break;
            yield return a;
            if (SlotCount < 2) yield break;
            Match b = greedy.Match(text);
            if (b.Success && !SameSplit(a, b)) yield return b;
        }

        /// <summary>Собирает перевод: translatePart переводит подставленные части (например, название предмета).</summary>
        public string Build(Match m, Func<string, string> translatePart)
        {
            var parts = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match s in Slot.Matches(Source))
            {
                Group g = m.Groups["s" + s.Groups[1].Value];
                parts[s.Groups[1].Value] = translatePart == null ? g.Value : translatePart(g.Value);
            }
            return Slot.Replace(Target, s => parts[s.Groups[1].Value]);
        }

        /// <summary>Перевод по первому разбиению или null (для простых случаев и тестов).</summary>
        public string TryApply(string text, Func<string, string> translatePart)
        {
            foreach (Match m in Matches(text)) return Build(m, translatePart);
            return null;
        }

        private bool SameSplit(Match a, Match b)
        {
            foreach (Match s in Slot.Matches(Source))
            {
                string name = "s" + s.Groups[1].Value;
                if (a.Groups[name].Index != b.Groups[name].Index || a.Groups[name].Length != b.Groups[name].Length) return false;
            }
            return true;
        }

        private static int LatinLetters(string s)
        {
            int n = 0;
            foreach (char c in s)
            {
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')) n++;
            }
            return n;
        }
    }
}
