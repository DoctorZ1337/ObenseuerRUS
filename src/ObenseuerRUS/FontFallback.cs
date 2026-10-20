using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;

namespace ObenseuerRUS
{
    /// <summary>
    /// Кириллица для шрифтов игры.
    /// Машинописный Erika Ormig (основной шрифт текста) получает первым запасным шрифтом
    /// fonts/ObenseuerTypewriterCyrillic.ttf — кириллицу в том же стиле, собранную из GNU FreeFont
    /// (tools/font/make_cyrillic_font.py, лицензия GPL-3.0+ с исключением для шрифтов, см. fonts/FONT-NOTICE.txt).
    /// Латиница по-прежнему берётся из Erika Ormig: запасной шрифт используется только для знаков, которых нет в основном.
    /// Для остальных шрифтов без кириллицы (VT323, Swansea, Roboto и др.) в общий список запасных TMP добавляются
    /// "LiberationSans SDF - Fallback" и "NotoSans-Bold SDF" из самой игры (SIL OFL 1.1, весь русский алфавит).
    /// </summary>
    internal static class FontFallback
    {
        private const string TypewriterFile = "ObenseuerTypewriterCyrillic.ttf";
        private const string TypewriterTarget = "Erika Ormig";
        private static readonly string[] Preferred = { "LiberationSans SDF - Fallback", "NotoSans-Bold SDF" };
        private static bool globalDone, typewriterFailed;
        private static TMP_FontAsset typewriter;

        public static void Ensure(string fontsDir)
        {
            TMP_FontAsset[] loaded = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            if (!globalDone) AddGlobal(loaded);
            AddTypewriter(loaded, fontsDir);
        }

        private static void AddGlobal(TMP_FontAsset[] loaded)
        {
            var found = new List<TMP_FontAsset>();
            foreach (string name in Preferred)
            {
                foreach (TMP_FontAsset font in loaded)
                {
                    if (font != null && font.name == name && font.HasCharacter('Ж', false, true))
                    {
                        found.Add(font);
                        break;
                    }
                }
            }
            if (found.Count == 0) return;
            List<TMP_FontAsset> list = TMP_Settings.fallbackFontAssets;
            if (list == null)
            {
                list = new List<TMP_FontAsset>();
                TMP_Settings.fallbackFontAssets = list;
            }
            foreach (TMP_FontAsset font in found)
            {
                if (!list.Contains(font)) list.Add(font);
            }
            globalDone = found.Count == Preferred.Length;
            RusMod.Instance?.Log.Info("запасные шрифты с кириллицей: " + string.Join(", ", found.ConvertAll(f => f.name).ToArray()));
        }

        /// <summary>Erika Ormig SDF / Menu / Plain грузятся вместе со сценами, поэтому проверка повторяется при каждой загрузке.</summary>
        private static void AddTypewriter(TMP_FontAsset[] loaded, string fontsDir)
        {
            if (typewriter == null && !typewriterFailed)
            {
                string path = Path.Combine(fontsDir, TypewriterFile);
                typewriter = File.Exists(path) ? TMP_FontAsset.CreateFontAsset(path, 0, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024) : null;
                if (typewriter == null || !typewriter.HasCharacter('Ж', false, true))
                {
                    typewriterFailed = true;
                    typewriter = null;
                    RusMod.Instance?.Log.Warn("машинописная кириллица не загружена (" + path + "), останется Noto Sans");
                    return;
                }
                typewriter.name = "Obenseuer Typewriter Cyrillic SDF";
            }
            if (typewriter == null) return;
            foreach (TMP_FontAsset font in loaded)
            {
                if (font == null || font == typewriter || !font.name.StartsWith(TypewriterTarget)) continue;
                List<TMP_FontAsset> table = font.fallbackFontAssetTable;
                if (table == null) font.fallbackFontAssetTable = table = new List<TMP_FontAsset>();
                if (table.Contains(typewriter)) continue;
                table.Insert(0, typewriter);
                RusMod.Instance?.Log.Info("машинописная кириллица подключена к " + font.name);
            }
        }
    }
}
