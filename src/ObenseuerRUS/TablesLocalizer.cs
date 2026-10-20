using System;
using System.Collections.Generic;
using ObenseuerRUS.Core;
using UnityEngine.Localization;
using UnityEngine.Localization.Settings;
using UnityEngine.Localization.Tables;

namespace ObenseuerRUS
{
    /// <summary>
    /// В игре есть язык "Russian (ru)" в Unity Localization, но его таблицы пустые.
    /// Заполняем их в памяти теми же вызовами, что и ImportStringTables игры (GetTable + AddEntry),
    /// чтобы выбор "Russian" в настройках не давал пустых надписей. Файлы игры не меняются.
    /// </summary>
    internal static class TablesLocalizer
    {
        private static bool done;

        public static void Ensure(Catalog catalog)
        {
            if (done || catalog.Tables.Count == 0) return;
            if (!LocalizationSettings.InitializationOperation.IsDone) return;
            Locale ru = LocalizationSettings.AvailableLocales.GetLocale("ru");
            if (ru == null)
            {
                done = true;
                RusMod.Instance?.Log.Warn("в игре нет языка ru, таблицы Unity Localization не заполнены");
                return;
            }
            int added = 0;
            foreach (KeyValuePair<string, Dictionary<string, string>> table in catalog.Tables)
            {
                StringTable st = LocalizationSettings.StringDatabase.GetTable(table.Key, ru);
                if (st == null) continue;
                foreach (KeyValuePair<string, string> kv in table.Value)
                {
                    StringTableEntry entry = st.GetEntry(kv.Key);
                    if (entry == null)
                    {
                        if (st.SharedData.GetEntry(kv.Key) == null) continue;
                        st.AddEntry(kv.Key, kv.Value);
                        added++;
                    }
                    else if (string.IsNullOrEmpty(entry.Value))
                    {
                        entry.Value = kv.Value;
                        added++;
                    }
                }
            }
            done = true;
            RusMod.Instance?.Log.Info("таблицы Unity Localization (ru): добавлено " + added + " строк");
        }
    }
}
