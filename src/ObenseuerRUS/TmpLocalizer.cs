using System;
using System.Collections.Generic;
using ObenseuerCore;
using ObenseuerRUS.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace ObenseuerRUS
{
    /// <summary>
    /// Перевод на этапе отрисовки TextMeshPro: ITextPreprocessor меняет только то, что видит игрок,
    /// а свойство text остаётся английским. Поэтому код игры, который читает text обратно
    /// (например, режим экрана в OptionsMenu), продолжает работать.
    /// </summary>
    internal sealed class RusPreprocessor : ITextPreprocessor
    {
        public static readonly RusPreprocessor Instance = new RusPreprocessor();
        public static TextTranslator Translator;
        public static bool Enabled;
        private static bool failed;

        public string PreprocessText(string text)
        {
            if (!Enabled || Translator == null) return text;
            try
            {
                return Translator.Translate(text);
            }
            catch (Exception e)
            {
                if (!failed) RusMod.Instance?.Log.Error("ошибка перевода строки, строка оставлена как есть", e);
                failed = true;
                return text;
            }
        }
    }

    /// <summary>
    /// Текст поля ввода. Окно записок (NotesPanel) показывает заголовок и текст записки в полях ввода,
    /// закрытых для правки (interactable = false), поэтому их тоже нужно переводить. Пока игрок правит поле
    /// (свой блокнот), текст не меняется: каретка и выделение считаются по видимому тексту.
    /// </summary>
    internal sealed class ReadOnlyFieldPreprocessor : ITextPreprocessor
    {
        private readonly TMP_InputField field;

        public ReadOnlyFieldPreprocessor(TMP_InputField field)
        {
            this.field = field;
        }

        public string PreprocessText(string text)
        {
            if (field != null && field.interactable && !field.readOnly) return text;
            return RusPreprocessor.Instance.PreprocessText(text);
        }
    }

    internal static class TmpLocalizer
    {
        private static readonly Dictionary<int, KeyValuePair<string, string>> legacyOriginals = new Dictionary<int, KeyValuePair<string, string>>();

        public static bool Install(CoreMod mod)
        {
            bool ui = mod.Postfix(typeof(TextMeshProUGUI), "Awake", Type.EmptyTypes, typeof(TmpLocalizer), nameof(AfterAwake));
            bool world = mod.Postfix(typeof(TextMeshPro), "Awake", Type.EmptyTypes, typeof(TmpLocalizer), nameof(AfterAwake));
            return ui && world;
        }

        private static void AfterAwake(TMP_Text __instance)
        {
            Attach(__instance);
        }

        private static void Attach(TMP_Text text)
        {
            if (text == null) return;
            if (text.textPreprocessor != null) return;
            TMP_InputField input = text.GetComponentInParent<TMP_InputField>(true);
            text.textPreprocessor = input != null && input.textComponent == text ? new ReadOnlyFieldPreprocessor(input) : (ITextPreprocessor)RusPreprocessor.Instance;
        }

        private static bool IsOurs(ITextPreprocessor p) => p == RusPreprocessor.Instance || p is ReadOnlyFieldPreprocessor;

        /// <summary>Подключает перевод к уже созданным объектам и просит перерисовать видимые.</summary>
        public static void Sweep()
        {
            foreach (TMP_Text text in Resources.FindObjectsOfTypeAll<TMP_Text>())
            {
                if (text == null || !text.gameObject.scene.IsValid()) continue;
                Attach(text);
                if (text.isActiveAndEnabled && IsOurs(text.textPreprocessor))
                {
                    text.havePropertiesChanged = true;
                    text.SetVerticesDirty();
                    text.SetLayoutDirty();
                }
            }
            SweepLegacy();
        }

        /// <summary>Старые UnityEngine.UI.Text: в игре это в основном отладочные панели и надпись "Saving...".</summary>
        private static void SweepLegacy()
        {
            foreach (Text text in Resources.FindObjectsOfTypeAll<Text>())
            {
                if (text == null || !text.gameObject.scene.IsValid()) continue;
                int id = text.GetInstanceID();
                KeyValuePair<string, string> last;
                // Если игра сменила текст после нас, новым оригиналом считается то, что она записала.
                string original = legacyOriginals.TryGetValue(id, out last) && last.Value == text.text ? last.Key : text.text;
                string shown = RusPreprocessor.Enabled && RusPreprocessor.Translator != null ? RusPreprocessor.Translator.Translate(original) : original;
                legacyOriginals[id] = new KeyValuePair<string, string>(original, shown);
                if (shown != text.text) text.text = shown;
            }
        }
    }
}
