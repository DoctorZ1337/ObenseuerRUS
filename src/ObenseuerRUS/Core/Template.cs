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
    /// </summary>
    public sealed class Template
    {
        private static readonly Regex Slot = new Regex(@"\{(\d)\}", RegexOptions.CultureInvariant);
        public readonly string Source, Target, Prefix, LongestLiteral;
        public readonly int LiteralLength, SlotCount;
        private readonly Regex regex;

        public Template(string source, string target)
        {
            if (string.IsNullOrEmpty(source)) throw new InvalidDataException("пустой шаблон");
            Source = source;
            Target = target;
            var pattern = new StringBuilder("^");
            var seen = new HashSet<int>();
            int at = 0;
            string longest = "";
            foreach (Match m in Slot.Matches(source))
            {
                string literal = source.Substring(at, m.Index - at);
                if (literal.Length > longest.Length) longest = literal;
                LiteralLength += literal.Length;
                pattern.Append(Regex.Escape(literal));
                int n = m.Groups[1].Value[0] - '0';
                if (!seen.Add(n)) throw new InvalidDataException("повтор {" + n + "} в шаблоне");
                pattern.Append("(?<s").Append(n).Append(">.+?)");
                at = m.Index + m.Length;
            }
            string tail = source.Substring(at);
            if (tail.Length > longest.Length) longest = tail;
            LiteralLength += tail.Length;
            pattern.Append(Regex.Escape(tail)).Append('$');
            SlotCount = seen.Count;
            if (SlotCount == 0) throw new InvalidDataException("в шаблоне нет {0}");
            foreach (Match m in Slot.Matches(target ?? ""))
            {
                if (!seen.Contains(m.Groups[1].Value[0] - '0')) throw new InvalidDataException("в переводе лишний " + m.Value);
            }
            Match first = Slot.Match(source);
            Prefix = source.Substring(0, first.Index);
            LongestLiteral = longest;
            regex = new Regex(pattern.ToString(), RegexOptions.CultureInvariant | RegexOptions.Singleline);
        }

        /// <summary>Возвращает перевод или null. translatePart переводит подставленные части (например, название предмета).</summary>
        public string TryApply(string text, Func<string, string> translatePart)
        {
            if (Prefix.Length > 0 && !text.StartsWith(Prefix, StringComparison.Ordinal)) return null;
            if (LongestLiteral.Length > 0 && text.IndexOf(LongestLiteral, StringComparison.Ordinal) < 0) return null;
            Match m = regex.Match(text);
            if (!m.Success) return null;
            return Slot.Replace(Target, s =>
            {
                Group g = m.Groups["s" + s.Groups[1].Value];
                return translatePart == null ? g.Value : translatePart(g.Value);
            });
        }
    }
}
