using System;
using ObenseuerRUS.Core;
using PixelCrushers;
using PixelCrushers.DialogueSystem;
using UnityEngine;
using DsLocalization = PixelCrushers.DialogueSystem.Localization;

namespace ObenseuerRUS
{
    /// <summary>
    /// Встроенная локализация Dialogue System (PixelCrushers), которой пользуется игра:
    /// при языке "ru" реплика берётся из поля "ru", вариант ответа — из "Menu Text ru",
    /// а при пустом поле — из оригинального "Dialogue Text"/"Menu Text".
    /// Оригинальные поля не меняются, поэтому привязка ответов других модов по тексту реплики не ломается.
    /// </summary>
    internal static class DialogueLocalizer
    {
        public const string Language = "ru";
        private static DialogueDatabase lastDatabase;
        private static int lastConversations = -1;

        public static int Applied { get; private set; }
        public static int Stale { get; private set; }

        public static void Ensure(Catalog catalog, bool enabled)
        {
            if (!DialogueManager.hasInstance) return;
            DialogueDatabase db = DialogueManager.masterDatabase;
            if (db == null) return;
            if (enabled && (!ReferenceEquals(db, lastDatabase) || db.conversations.Count != lastConversations)) Inject(db, catalog);
            string wanted = enabled ? Language : string.Empty;
            if (DsLocalization.language != wanted) SetLanguageKeepingPrefs(wanted);
        }

        private static void Inject(DialogueDatabase db, Catalog catalog)
        {
            int applied = 0, stale = 0;
            foreach (Conversation conversation in db.conversations)
            {
                foreach (DialogueEntry entry in conversation.dialogueEntries)
                {
                    DialogueLine line;
                    if (!catalog.Dialogue.TryGetValue(Catalog.DialogueKey(conversation.id, entry.id), out line)) continue;
                    if (line.TextTarget != null)
                    {
                        if (Field.LookupValue(entry.fields, "Dialogue Text") == line.TextSource)
                        {
                            Field.SetValue(entry.fields, Language, line.TextTarget);
                            applied++;
                        }
                        else stale++;
                    }
                    if (line.MenuTarget != null)
                    {
                        if (Field.LookupValue(entry.fields, "Menu Text") == line.MenuSource)
                        {
                            Field.SetValue(entry.fields, "Menu Text " + Language, line.MenuTarget);
                            applied++;
                        }
                        else stale++;
                    }
                }
            }
            lastDatabase = db;
            lastConversations = db.conversations.Count;
            Applied = applied;
            Stale = stale;
            RusMod.Instance?.Log.Info("диалоги: применено " + applied + ", пропущено из-за изменённого оригинала " + stale);
        }

        /// <summary>
        /// SetLanguage через UILocalizationManager пишет язык в PlayerPrefs ("Language").
        /// Возвращаем прежнее значение, чтобы мод не оставлял следов в настройках игры.
        /// </summary>
        private static void SetLanguageKeepingPrefs(string language)
        {
            string key = "Language";
            UILocalizationManager manager = DialogueManager.instance != null ? DialogueManager.instance.GetComponent<UILocalizationManager>() : null;
            if (manager != null && !string.IsNullOrEmpty(manager.currentLanguagePlayerPrefsKey)) key = manager.currentLanguagePlayerPrefsKey;
            bool had = PlayerPrefs.HasKey(key);
            string previous = had ? PlayerPrefs.GetString(key) : null;
            try
            {
                DialogueManager.SetLanguage(language);
            }
            finally
            {
                if (had) PlayerPrefs.SetString(key, previous);
                else PlayerPrefs.DeleteKey(key);
            }
        }
    }
}
