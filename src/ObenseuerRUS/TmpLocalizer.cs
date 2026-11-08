using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
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
        public static readonly RusPreprocessor Instance = new RusPreprocessor(null);
        private static readonly Dictionary<string, RusPreprocessor> byScope = new Dictionary<string, RusPreprocessor>(StringComparer.Ordinal);
        public static TextTranslator Translator;
        public static bool Enabled;
        private static bool failed;
        private readonly string scope;

        private RusPreprocessor(string scope)
        {
            this.scope = scope;
        }

        /// <summary>Перевод, где сначала ищутся строки этого scope (Entry.Scope), затем общие.</summary>
        public static RusPreprocessor For(string scope)
        {
            RusPreprocessor p;
            if (!byScope.TryGetValue(scope, out p)) byScope[scope] = p = new RusPreprocessor(scope);
            return p;
        }

        public string PreprocessText(string text)
        {
            if (!Enabled || Translator == null) return text;
            try
            {
                return Translator.Translate(text, scope);
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
    /// Надпись в мире (вывеска, табличка): табличка нарисована под английский текст, а рамка TextMeshPro у вывесок
    /// условная (100×100 при кегле 2), поэтому перенос строк не спасает. Если перевод шире оригинала,
    /// он уменьшается тегом size до ширины оригинала. Автоподбор размера игры (таблички с фамилиями) делает это сам.
    /// </summary>
    internal sealed class SignPreprocessor : ITextPreprocessor
    {
        private const float MinScale = 0.55f;
        private static readonly Regex Tag = new Regex("<[^<>]*>", RegexOptions.CultureInvariant);
        private readonly TMP_Text text;
        private readonly RusPreprocessor inner;

        public SignPreprocessor(TMP_Text text, RusPreprocessor inner)
        {
            this.text = text;
            this.inner = inner;
        }

        public string PreprocessText(string source)
        {
            string shown = inner.PreprocessText(source);
            if (shown == source || text == null || text.enableAutoSizing || text.font == null) return shown;
            try
            {
                float before = Width(source), after = Width(shown);
                if (before <= 0f || after <= before * 1.02f) return shown;
                // Оригинал не помещается в рамку и переносится (подписи над NPC): это текстовое поле, а не вывеска.
                float box = text.rectTransform.rect.width - text.margin.x - text.margin.z;
                float unit = text.fontSize * (text.isOrthographic ? 1f : 0.1f);
                if (text.textWrappingMode != TextWrappingModes.NoWrap && before * unit > box) return shown;
                float scale = Mathf.Max(MinScale, before / after);
                return "<size=" + Mathf.FloorToInt(scale * 100f).ToString(CultureInfo.InvariantCulture) + "%>" + shown;
            }
            catch (Exception)
            {
                return shown;
            }
        }

        /// <summary>Ширина самой длинной строки в долях кегля — так же, как её считает TextMeshPro (без тегов).</summary>
        private float Width(string s)
        {
            s = Tag.Replace(s, "");
            FontStyles style = text.fontStyle;
            if ((style & (FontStyles.UpperCase | FontStyles.SmallCaps)) != 0) s = s.ToUpperInvariant();
            else if ((style & FontStyles.LowerCase) != 0) s = s.ToLowerInvariant();
            float spacing = text.characterSpacing * 0.01f, max = 0f, line = 0f;
            foreach (char c in s)
            {
                if (c == '\n')
                {
                    max = Mathf.Max(max, line);
                    line = 0f;
                    continue;
                }
                line += Advance(c) + spacing;
            }
            return Mathf.Max(max, line);
        }

        private float Advance(char c)
        {
            bool alternative;
            TMP_FontAsset font = text.font;
            TMP_Character ch = TMP_FontAssetUtilities.GetCharacterFromFontAsset(c, font, true, FontStyles.Normal, FontWeight.Regular, out alternative);
            if (ch == null && TMP_Settings.fallbackFontAssets != null)
            {
                ch = TMP_FontAssetUtilities.GetCharacterFromFontAssets(c, font, TMP_Settings.fallbackFontAssets, true, FontStyles.Normal, FontWeight.Regular, out alternative);
            }
            if (ch == null || ch.glyph == null) return 0f;
            TMP_FontAsset owner = ch.textAsset as TMP_FontAsset ?? font;
            return ch.glyph.metrics.horizontalAdvance * ch.scale * owner.faceInfo.scale / owner.faceInfo.pointSize;
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
        public const string ActionScope = "action";
        // Заголовок объекта в подсказке взаимодействия: "Lock" у кнопки автомата — «Фиксация», а не глагол «Запереть».
        public const string UseTitleScope = "usetitle";
        private static readonly Dictionary<int, KeyValuePair<string, string>> legacyOriginals = new Dictionary<int, KeyValuePair<string, string>>();

        public static bool Install(CoreMod mod)
        {
            bool ui = mod.Postfix(typeof(TextMeshProUGUI), "Awake", Type.EmptyTypes, typeof(TmpLocalizer), nameof(AfterAwake));
            bool world = mod.Postfix(typeof(TextMeshPro), "Awake", Type.EmptyTypes, typeof(TmpLocalizer), nameof(AfterAwake));
            // Подписи действий ("Drink", "Smoke") совпадают с типами предметов ("Напиток", "Курение"): у кнопок окна предмета
            // и у подсказки взаимодействия ("(Hold - Drink)") свой словарь глаголов.
            bool useText = mod.Postfix(typeof(UseText), "Awake", typeof(TmpLocalizer), nameof(AfterUseTextAwake));
            bool buttons = mod.Postfix(typeof(ItemInfoPanel), "SetButtonStatusAndText", typeof(TmpLocalizer), nameof(AfterItemButtons));
            if (!useText || !buttons) RusMod.Instance?.Log.Warn("подписи действий предметов переводятся общим словарём: перехват UseText/ItemInfoPanel не установлен");
            return ui && world;
        }

        private static void AfterAwake(TMP_Text __instance)
        {
            Attach(__instance);
        }

        private static void AfterUseTextAwake(UseText __instance, TextMeshProUGUI ___objectName)
        {
            UseScope(__instance.useText, ActionScope);
            UseScope(___objectName, UseTitleScope);
        }

        private static void AfterItemButtons(TextMeshProUGUI buttonText, TextMeshProUGUI cantUseButtonText)
        {
            UseScope(buttonText, ActionScope);
            UseScope(cantUseButtonText, ActionScope);
        }

        private static void UseScope(TMP_Text text, string scope)
        {
            RusPreprocessor p = RusPreprocessor.For(scope);
            if (text == null || text.textPreprocessor == p || text.textPreprocessor is ReadOnlyFieldPreprocessor) return;
            text.textPreprocessor = p;
            text.havePropertiesChanged = true;
            text.SetVerticesDirty();
        }

        private static void Attach(TMP_Text text)
        {
            if (text == null) return;
            if (text.textPreprocessor != null) return;
            TMP_InputField input = text.GetComponentInParent<TMP_InputField>(true);
            if (input != null && input.textComponent == text)
            {
                text.textPreprocessor = new ReadOnlyFieldPreprocessor(input);
                return;
            }
            string scope = ObjectScope(text);
            RusPreprocessor p = scope != null ? RusPreprocessor.For(scope) : RusPreprocessor.Instance;
            text.textPreprocessor = text is TextMeshPro ? new SignPreprocessor(text, p) : (ITextPreprocessor)p;
        }

        /// <summary>
        /// "object:Text Screen/OPEN" — перевод одного объекта, если он задан в каталоге: у такого текста есть двойник с другим смыслом
        /// (половинка заставки "OPEN SEWER" и вывеска "OPEN") или ему мало места (круглая кнопка "Drop").
        /// </summary>
        private static string ObjectScope(TMP_Text text)
        {
            if (RusPreprocessor.Translator == null) return null;
            Transform parent = text.transform.parent;
            string scope = "object:" + (parent != null ? parent.name + "/" : "") + text.gameObject.name;
            return RusPreprocessor.Translator.HasScope(scope) ? scope : null;
        }

        private static bool IsOurs(ITextPreprocessor p) => p is RusPreprocessor || p is ReadOnlyFieldPreprocessor || p is SignPreprocessor;

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
