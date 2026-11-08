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
    /// Roboto (вывески и таблички в мире) — кириллицу из самого Roboto 2.137 (Apache 2.0): она вшита в DLL
    /// (src/ObenseuerRUS/Fonts) и при запуске записывается во временную папку игры.
    /// Латиница по-прежнему берётся из шрифтов игры: запасной шрифт используется только для знаков, которых нет в основном.
    /// Для остальных шрифтов без кириллицы (VT323, Swansea и др.) в общий список запасных TMP добавляются
    /// "LiberationSans SDF - Fallback" и "NotoSans-Bold SDF" из самой игры (SIL OFL 1.1, весь русский алфавит).
    /// </summary>
    internal static class FontFallback
    {
        private const string TypewriterFile = "ObenseuerTypewriterCyrillic.ttf";
        private const string TypewriterTarget = "Erika Ormig";
        private const string RobotoResource = "RobotoCyrillic-Regular.ttf";
        private const string RobotoTarget = "Roboto";
        private static readonly string[] Preferred = { "LiberationSans SDF - Fallback", "NotoSans-Bold SDF" };
        private static bool globalDone, typewriterFailed, robotoFailed;
        private static TMP_FontAsset typewriter, roboto;

        public static void Ensure(string fontsDir)
        {
            TMP_FontAsset[] loaded = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            if (!globalDone) AddGlobal(loaded);
            if (typewriter == null && !typewriterFailed)
            {
                typewriter = Load(Path.Combine(fontsDir, TypewriterFile), "Obenseuer Typewriter Cyrillic SDF");
                typewriterFailed = typewriter == null;
                if (typewriterFailed) RusMod.Instance?.Log.Warn("машинописная кириллица не загружена (" + Path.Combine(fontsDir, TypewriterFile) + "), останется Noto Sans");
            }
            if (roboto == null && !robotoFailed)
            {
                roboto = Load(ExtractResource(RobotoResource), "ObenseuerRUS Roboto Cyrillic SDF");
                robotoFailed = roboto == null;
                if (robotoFailed) RusMod.Instance?.Log.Warn("кириллица Roboto не загружена, вывески покажут Liberation Sans");
            }
            // Erika Ormig SDF / Menu / Plain и Roboto-Regular SDF грузятся вместе со сценами, поэтому проверка повторяется при каждой загрузке.
            AttachFirst(loaded, typewriter, TypewriterTarget);
            AttachFirst(loaded, roboto, RobotoTarget);
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

        private static TMP_FontAsset Load(string path, string name)
        {
            if (path == null || !File.Exists(path)) return null;
            TMP_FontAsset font = TMP_FontAsset.CreateFontAsset(path, 0, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024);
            if (font == null || !font.HasCharacter('Ж', false, true)) return null;
            font.name = name;
            return font;
        }

        /// <summary>Шрифт из ресурсов DLL — в файл: динамический шрифт TMP дочитывает глифы из файла по мере надобности.</summary>
        private static string ExtractResource(string resource)
        {
            try
            {
                string path = Path.Combine(Path.Combine(Application.temporaryCachePath, "ObenseuerRUS"), resource);
                using (Stream source = typeof(FontFallback).Assembly.GetManifestResourceStream(resource))
                {
                    if (source == null) return null;
                    var bytes = new byte[source.Length];
                    int read = 0;
                    while (read < bytes.Length)
                    {
                        int n = source.Read(bytes, read, bytes.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    if (!File.Exists(path) || new FileInfo(path).Length != bytes.Length)
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(path));
                        File.WriteAllBytes(path, bytes);
                    }
                }
                return path;
            }
            catch (IOException e)
            {
                RusMod.Instance?.Log.Warn("не удалось записать " + resource + ": " + e.Message);
                return null;
            }
            catch (System.UnauthorizedAccessException e)
            {
                RusMod.Instance?.Log.Warn("не удалось записать " + resource + ": " + e.Message);
                return null;
            }
        }

        /// <summary>Кириллица — первым запасным шрифтом у всех шрифтов игры, чьё имя начинается с target.</summary>
        private static void AttachFirst(TMP_FontAsset[] loaded, TMP_FontAsset cyrillic, string target)
        {
            if (cyrillic == null) return;
            foreach (TMP_FontAsset font in loaded)
            {
                if (font == null || font == cyrillic || font == typewriter || font == roboto || !font.name.StartsWith(target)) continue;
                List<TMP_FontAsset> table = font.fallbackFontAssetTable;
                if (table == null) font.fallbackFontAssetTable = table = new List<TMP_FontAsset>();
                if (table.Contains(cyrillic)) continue;
                table.Insert(0, cyrillic);
                RusMod.Instance?.Log.Info("кириллица " + cyrillic.name + " подключена к " + font.name);
            }
        }
    }
}
