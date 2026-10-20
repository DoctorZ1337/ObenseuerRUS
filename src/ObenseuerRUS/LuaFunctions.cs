using System.Reflection;
using ObenseuerCore;
using ObenseuerRUS.Core;

namespace ObenseuerRUS
{
    /// <summary>
    /// Lua-функции для русских реплик (документированный Dialogue.RegisterFunction фреймворка).
    /// Некоторые вставки игры возвращают английский текст: Actor["Player"].AgeName ("boy", "lady"...),
    /// Pronoun ("he"/"she"), TellTimeResponse(...). В русских полях реплик такие вставки заменены
    /// на эквивалентные вызовы этих функций; оригинальные английские поля не меняются.
    /// Имена в Lua: Mod_obenseuerrus_Tr, Mod_obenseuerrus_AgeName и т.д.
    /// </summary>
    public static class LuaFunctions
    {
        internal static TextTranslator Translator;
        private static readonly string[] Names = { nameof(Tr), nameof(AgeName), nameof(AgeNameFormalShort), nameof(Pronoun), nameof(PronounCapitalized) };

        internal static void Register(string modId)
        {
            if (!Dialogue.IsAvailable) return;
            foreach (string name in Names)
            {
                MethodInfo method = typeof(LuaFunctions).GetMethod(name, BindingFlags.Public | BindingFlags.Static);
                Dialogue.RegisterFunction(modId, name, null, method);
            }
        }

        /// <summary>Перевод значения, которое вернула игровая Lua-вставка: точные строки и шаблоны каталога.</summary>
        public static string Tr(string text)
        {
            if (string.IsNullOrEmpty(text) || Translator == null) return text ?? string.Empty;
            return Translator.Translate(text);
        }

        private static bool Male => Dialogue.GetVariableString("PlayerGender", "male") != "female";
        private static float Age => Dialogue.GetVariableNumber("PlayerAge", 30f);

        /// <summary>Как PlayerIdentity игры: boy/girl, man/lady (старше 22), grandpa/grandma (старше 70).</summary>
        public static string AgeName()
        {
            if (Age > 70f) return Male ? "дед" : "бабуля";
            if (Age > 22f) return Male ? "мужик" : "дамочка";
            return Male ? "парень" : "девчонка";
        }

        /// <summary>sir/lady/madam.</summary>
        public static string AgeNameFormalShort()
        {
            return Male ? "господин" : "госпожа";
        }

        public static string Pronoun()
        {
            return Male ? "он" : "она";
        }

        public static string PronounCapitalized()
        {
            return Male ? "Он" : "Она";
        }
    }
}
