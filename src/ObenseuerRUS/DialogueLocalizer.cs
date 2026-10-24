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
            if (DsLocalization.language == wanted) return;
            SetLanguageKeepingPrefs(wanted);
            RefreshActiveConversations();
        }

        /// <summary>
        /// Открытая реплика и варианты ответа — сразу на новом языке, тем же путём, каким их показывает игра:
        /// SetContent берёт короткое имя актёра ("Interviewer", а не "Start/Interviewer") и заново запускает
        /// печатную машинку, которая отрабатывает коды пауз "\,". Реплика печатается заново с начала.
        /// </summary>
        private static void RefreshActiveConversations()
        {
            EmphasisSetting[] emphasis = DialogueManager.masterDatabase != null ? DialogueManager.masterDatabase.emphasisSettings : null;
            foreach (ActiveConversationRecord record in DialogueManager.instance.activeConversations)
            {
                StandardDialogueUI ui = record?.conversationView?.dialogueUI as StandardDialogueUI;
                ConversationState state = record?.conversationController?.currentState;
                Subtitle subtitle = state?.subtitle;
                if (ui == null || subtitle?.dialogueEntry == null) continue;
                subtitle.formattedText = FormattedText.Parse(subtitle.dialogueEntry.subtitleText, emphasis);
                DialogueActor actor;
                StandardUISubtitlePanel panel = ui.conversationUIElements.standardSubtitleControls.GetPanel(subtitle, out actor);
                // Панель с накоплением текста дописала бы реплику второй раз.
                if (panel != null && panel.isOpen && !panel.accumulateText) panel.SetContent(subtitle);
                // Варианты ответа собраны ещё при показе реплики NPC (ConversationModel.GetState), поэтому их текст
                // обновляется всегда, а не только у открытого меню, — иначе меню откроется на прежнем языке.
                if (state.pcResponses == null) continue;
                foreach (Response response in state.pcResponses)
                {
                    if (response?.destinationEntry != null) response.formattedText = ResponseText(record.conversationModel, response, emphasis);
                }
                StandardUIMenuPanel menu = ui.conversationUIElements.defaultMenuPanel;
                if (menu != null && menu.isOpen) menu.ShowResponses(subtitle, state.pcResponses, ui.transform);
            }
        }

        /// <summary>
        /// Текст варианта ответа на текущем языке с теми же пометками, что ставит ConversationModel: уже выбранный и
        /// недоступный вариант обёрнуты в [em]. Условия заново не проверяются: UpdateResponses выполнил бы скрипты групп.
        /// </summary>
        private static FormattedText ResponseText(ConversationModel model, Response response, EmphasisSetting[] emphasis)
        {
            DialogueEntry entry = response.destinationEntry;
            string text = entry.responseButtonText;
            if (model != null && model.emTagForOldResponses != EmTag.None
                && Lua.Run("return Conversation[" + entry.conversationID + "].Dialog[" + entry.id + "].SimStatus").asString == "WasDisplayed")
                text = "[em" + (int)model.emTagForOldResponses + "]" + UITools.StripEmTags(text) + "[/em" + (int)model.emTagForOldResponses + "]";
            if (model != null && model.emTagForInvalidResponses != EmTag.None && !response.enabled)
                text = "[em" + (int)model.emTagForInvalidResponses + "]" + UITools.StripEmTags(text) + "[/em" + (int)model.emTagForInvalidResponses + "]";
            FormattedText formatted = FormattedText.Parse(text, emphasis);
            if (!response.enabled)
            {
                formatted.forceAuto = false;
                formatted.forceMenu = false;
            }
            return formatted;
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
                    // Правка только грамматики в игре (артикли, регистр, знаки) перевод не отменяет: см. Catalog.SameText.
                    if (line.TextTarget != null)
                    {
                        if (Catalog.SameText(Field.LookupValue(entry.fields, "Dialogue Text"), line.TextSource))
                        {
                            Field.SetValue(entry.fields, Language, line.TextTarget);
                            applied++;
                        }
                        else stale++;
                    }
                    if (line.MenuTarget != null)
                    {
                        if (Catalog.SameText(Field.LookupValue(entry.fields, "Menu Text"), line.MenuSource))
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
            // Встроенное обновление открытого разговора пишет в окно сырой текст: с кодами пауз "\," и полным именем актёра
            // ("Start/Interviewer"). Его заменяет RefreshActiveConversations.
            bool refresh = DialogueManager.updateActiveConversationTextWhenLanguageChanges;
            try
            {
                DialogueManager.updateActiveConversationTextWhenLanguageChanges = false;
                DialogueManager.SetLanguage(language);
            }
            finally
            {
                DialogueManager.updateActiveConversationTextWhenLanguageChanges = refresh;
                if (had) PlayerPrefs.SetString(key, previous);
                else PlayerPrefs.DeleteKey(key);
            }
        }
    }
}
