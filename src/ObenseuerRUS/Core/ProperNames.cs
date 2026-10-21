using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace ObenseuerRUS.Core
{
    /// <summary>
    /// Необязательная кириллица для имён людей и названий (брендов, заведений, мест) в уже готовом тексте:
    /// "Arvo Kirkastaja хочет" -> "Арво Киркастая хочет". Перевод строк имена не трогает — замена идёт после него.
    /// Ключи выключенной группы тоже ищутся, но остаются как есть: так "Osmo Olut" (бренд) не превращается
    /// в "Осмо Olut", когда включены только имена людей.
    /// Английская строка без перевода остаётся английской целиком: замена принимается, только если в строке
    /// уже есть кириллица или после замены в ней не осталось латинских слов (табличка "Arvo Kirkastaja").
    /// Решение принимается для каждой строки текста отдельно: в одном поле TMP бывают и русские, и английские строки
    /// (консоль разработчика, вывески со списком заведений).
    /// </summary>
    public sealed class ProperNames
    {
        private static readonly Regex Word = new Regex(@"[A-Za-zÀ-ɏ]+", RegexOptions.CultureInvariant);
        private static readonly Regex Tag = new Regex(@"<[^<>\n]{1,96}>", RegexOptions.CultureInvariant);
        // Ключи по первому латинскому слову, длинные первыми: "Osmo Olut" раньше "Osmo".
        private readonly Dictionary<string, List<KeyValuePair<string, string>>> byFirstWord =
            new Dictionary<string, List<KeyValuePair<string, string>>>(StringComparer.Ordinal);

        /// <summary>null, если обе группы выключены.</summary>
        public static ProperNames For(Catalog catalog, bool people, bool brands)
        {
            if (!people && !brands) return null;
            var names = new ProperNames();
            names.AddGroup(catalog.People, people);
            names.AddGroup(catalog.Brands, brands);
            names.Sort();
            return names;
        }

        private void AddGroup(Dictionary<string, string> group, bool enabled)
        {
            foreach (KeyValuePair<string, string> kv in group)
            {
                string shown = enabled ? kv.Value : kv.Key;
                Add(kv.Key, shown);
                // Вывески и титры набраны капсом: "OBENSEUER", "V. PENTTI".
                Add(kv.Key.ToUpperInvariant(), shown.ToUpperInvariant());
            }
        }

        private void Add(string key, string shown)
        {
            Match first = Word.Match(key);
            if (!first.Success || first.Index != 0) return;
            List<KeyValuePair<string, string>> list;
            if (!byFirstWord.TryGetValue(first.Value, out list)) byFirstWord[first.Value] = list = new List<KeyValuePair<string, string>>();
            foreach (KeyValuePair<string, string> kv in list)
            {
                if (kv.Key == key) return;
            }
            list.Add(new KeyValuePair<string, string>(key, shown));
        }

        private void Sort()
        {
            foreach (List<KeyValuePair<string, string>> list in byFirstWord.Values) list.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));
        }

        public string Apply(string text)
        {
            if (string.IsNullOrEmpty(text) || !TextTranslator.HasLatinLetter(text)) return text;
            if (text.IndexOf('\n') < 0) return ApplyLine(text);
            string[] lines = text.Split('\n');
            bool changed = false;
            for (int i = 0; i < lines.Length; i++)
            {
                string line = ApplyLine(lines[i]);
                changed |= !ReferenceEquals(line, lines[i]);
                lines[i] = line;
            }
            return changed ? string.Join("\n", lines) : text;
        }

        private string ApplyLine(string text)
        {
            if (!TextTranslator.HasLatinLetter(text)) return text;
            MatchCollection tags = text.IndexOf('<') >= 0 ? Tag.Matches(text) : null;
            StringBuilder sb = null;
            int copied = 0, tag = 0;
            Match m = Word.Match(text);
            while (m.Success)
            {
                int at = m.Index;
                // Внутри rich-text тега (<link="...">) ничего не меняем.
                while (tags != null && tag < tags.Count && tags[tag].Index + tags[tag].Length <= at) tag++;
                if (tags != null && tag < tags.Count && tags[tag].Index < at)
                {
                    m = m.NextMatch();
                    continue;
                }
                List<KeyValuePair<string, string>> list;
                int end = -1;
                string key = null, shown = null;
                if (FreeBefore(text, at) && byFirstWord.TryGetValue(m.Value, out list))
                {
                    foreach (KeyValuePair<string, string> kv in list)
                    {
                        end = MatchAt(text, at, kv.Key);
                        if (end < 0) continue;
                        key = kv.Key;
                        shown = kv.Value;
                        break;
                    }
                }
                if (end < 0)
                {
                    m = m.NextMatch();
                    continue;
                }
                if (shown != key)
                {
                    // "Mark'у" -> "Марку": русское окончание через апостроф прирастает к имени.
                    int replaceEnd = end < text.Length - 1 && IsApostrophe(text[end]) && IsCyrillic(text[end + 1]) ? end + 1 : end;
                    if (sb == null) sb = new StringBuilder(text.Length + 16);
                    sb.Append(text, copied, at - copied).Append(shown);
                    copied = replaceEnd;
                }
                m = Word.Match(text, end);
            }
            if (sb == null) return text;
            string result = sb.Append(text, copied, text.Length - copied).ToString();
            return HasCyrillic(text) || !HasLatinWord(Tag.Replace(result, " ")) ? result : text;
        }

        /// <summary>Конец совпадения ключа с текстом в позиции at или -1. Пробел ключа совпадает с любым числом пробелов текста.</summary>
        private static int MatchAt(string text, int at, string key)
        {
            int i = at;
            for (int k = 0; k < key.Length; k++)
            {
                if (i >= text.Length) return -1;
                if (key[k] == ' ')
                {
                    if (text[i] != ' ') return -1;
                    while (i < text.Length && text[i] == ' ') i++;
                    continue;
                }
                if (text[i] != key[k]) return -1;
                i++;
            }
            return FreeAfter(text, i) ? i : -1;
        }

        /// <summary>Перед ключом не буква, не цифра, не апостроф и не дефис после латиницы ("Lifehack-Pekka" — не "Pekka").</summary>
        private static bool FreeBefore(string text, int at)
        {
            if (at == 0) return true;
            char c = text[at - 1];
            if (char.IsLetterOrDigit(c) || IsApostrophe(c) || IsPathChar(c)) return false;
            return !(c == '-' && at >= 2 && (IsLatin(text[at - 2]) || char.IsDigit(text[at - 2])));
        }

        /// <summary>После ключа не буква и не цифра; апостроф или дефис — только если дальше не латиница ("Aziz's" — не "Aziz").</summary>
        private static bool FreeAfter(string text, int end)
        {
            if (end >= text.Length) return true;
            char c = text[end];
            if (char.IsLetterOrDigit(c) || IsPathChar(c)) return false;
            if ((IsApostrophe(c) || c == '-') && end + 1 < text.Length) return !(IsLatin(text[end + 1]) || char.IsDigit(text[end + 1]));
            return true;
        }

        private static bool IsApostrophe(char c) => c == '\'' || c == '’';
        // Пути и идентификаторы ("Saves/Obenseuer/", "Mines_Anders_Terva") — служебный текст, а не имя.
        private static bool IsPathChar(char c) => c == '/' || c == '\\' || c == '_';
        private static bool IsLatin(char c) => (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= 'À' && c <= 'ɏ' && char.IsLetter(c));
        private static bool IsCyrillic(char c) => c >= 'Ѐ' && c <= 'ӿ';

        private static bool HasCyrillic(string s)
        {
            foreach (char c in s)
            {
                if (IsCyrillic(c)) return true;
            }
            return false;
        }

        /// <summary>Латинское слово со строчными буквами — обычный английский текст, а не код вроде "OC", "B2".</summary>
        private static bool HasLatinWord(string s)
        {
            int run = 0;
            bool lower = false;
            for (int i = 0; i <= s.Length; i++)
            {
                char c = i < s.Length ? s[i] : ' ';
                if (IsLatin(c))
                {
                    run++;
                    lower |= char.IsLower(c);
                    continue;
                }
                if (run >= 2 && lower) return true;
                run = 0;
                lower = false;
            }
            return false;
        }
    }
}
