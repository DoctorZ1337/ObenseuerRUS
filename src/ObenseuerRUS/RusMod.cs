using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using ObenseuerCore;
using ObenseuerRUS.Core;
using UnityEngine;

namespace ObenseuerRUS
{
    [ModInfo("obenseuerrus", "Русификатор Obenseuer", "0.4.2")]
    [ModDependency("obenseuer.moddingframework", "0.12.0")]
    public sealed class RusMod : FrameworkMod
    {
        internal static RusMod Instance;
        private Catalog catalog;
        private TextTranslator translator;
        private ModSetting<bool> isEnabled, dialogue, ui, logMissing;
        private ModKeybind toggleKey;
        private readonly HashSet<string> missing = new HashSet<string>(StringComparer.Ordinal);
        private string folder;
        private bool missingDirty;
        private float nextFlush;
        private GitHubUpdater updater;
        private string updateNotice;
        private GUIStyle updateNoticeStyle;

        protected override void OnLoad()
        {
            Instance = this;
            folder = Path.GetDirectoryName(typeof(RusMod).Assembly.Location);
            isEnabled = Config.Bind("General", "Enabled", true, "Включить русский перевод.");
            ui = Config.Bind("General", "TranslateInterface", true, "Переводить интерфейс, предметы, задания, записки.");
            dialogue = Config.Bind("General", "TranslateDialogue", true, "Переводить диалоги.");
            logMissing = Config.Bind("Debug", "LogUntranslated", false, "Записывать строки интерфейса, где остался английский текст (целиком или частично), в untranslated.txt в папке мода.");
            toggleKey = Mod.Key("ToggleLanguage", "Русский / английский", KeyCode.F9);
            LoadCatalog();
            ShowPreviousUpdateError();
            StartUpdater();
            ApplySettings();

            if (!TmpLocalizer.Install(Mod)) Log.Warn("перехват TextMeshPro не установлен, интерфейс останется на английском");
            Events.SceneLoaded += OnSceneLoaded;
            Dialogue.Intercept(Mod.Id, args => EnsureAll());
            isEnabled.Changed += OnSettingChanged;
            ui.Changed += OnSettingChanged;
            dialogue.Changed += OnSettingChanged;
            EnsureAll();
            TmpLocalizer.Sweep();
        }

        // Клавиша переключает General/Enabled; дальше работает тот же OnSettingChanged, что и для меню Mods.
        private void Update()
        {
            if (updater != null)
            {
                string notice;
                while (updater.TryTakeNotice(out notice)) updateNotice = notice;
                string error = updater.TakeError();
                if (!string.IsNullOrEmpty(error)) Log.Warn(error);
            }
            // Журнал непереведённого пишется и без смены сцены: окна интерфейса открываются в одной сцене.
            if (missingDirty && Time.unscaledTime >= nextFlush)
            {
                nextFlush = Time.unscaledTime + 5f;
                FlushMissing();
            }
            if (UiState.IsTyping) return;
            if (toggleKey == null || !toggleKey.Down()) return;
            isEnabled.Value = !isEnabled.Value;
            Log.Info(isEnabled.Value ? "перевод включён клавишей" : "перевод выключен клавишей");
        }

        private void OnGUI()
        {
            if (string.IsNullOrEmpty(updateNotice)) return;
            if (updateNoticeStyle == null)
                updateNoticeStyle = new GUIStyle(GUI.skin.label) { fontSize = 15, wordWrap = true, alignment = TextAnchor.MiddleLeft };

            float width = Mathf.Max(1f, Mathf.Min(660f, Screen.width - 30f));
            const float height = 76f;
            var box = new Rect(15f, 15f, width, height);
            GUI.Box(box, GUIContent.none);
            GUI.Label(new Rect(box.x + 12f, box.y + 5f, box.width - 54f, box.height - 10f), updateNotice, updateNoticeStyle);
            if (GUI.Button(new Rect(box.xMax - 38f, box.y + 8f, 30f, 28f), "×")) updateNotice = null;
        }

        private void StartUpdater()
        {
            AssemblyInformationalVersionAttribute version = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                typeof(RusMod).Assembly, typeof(AssemblyInformationalVersionAttribute));
            string current = version == null ? "0.4.0" : version.InformationalVersion.Split('+')[0];
            updater = new GitHubUpdater(folder, current);
            updater.Start();
        }

        private void ShowPreviousUpdateError()
        {
            string path = Path.Combine(folder, ".github-update-error");
            if (!File.Exists(path)) return;
            try
            {
                string detail = File.ReadAllText(path, Encoding.UTF8).Trim();
                updateNotice = "Предыдущее автообновление не установилось. Установите архив вручную или попробуйте позже.";
                Log.Warn("автообновление мода: помощник установки завершился с ошибкой: " + detail);
                File.Delete(path);
            }
            catch (Exception e)
            {
                Log.Warn("не удалось прочитать результат автообновления: " + e.Message);
            }
        }

        private void LoadCatalog()
        {
            catalog = Catalog.LoadDirectory(Path.Combine(folder, "translations"));
            foreach (string problem in catalog.Problems) Log.Warn("перевод: " + problem);
            Log.Info("загружено строк: " + catalog.Used + " (без перевода: " + catalog.Pending + "), точных " + catalog.Exact.Count + ", шаблонов " + catalog.Templates.Count + ", реплик " + catalog.Dialogue.Count);
            translator = new TextTranslator(catalog);
            translator.Missed = OnMissed;
            RusPreprocessor.Translator = translator;
            LuaFunctions.Translator = new TextTranslator(catalog, forLua: true);
        }

        private void ApplySettings()
        {
            RusPreprocessor.Enabled = isEnabled.Value && ui.Value;
        }

        private void OnSettingChanged()
        {
            ApplySettings();
            translator.ClearCache();
            EnsureAll();
            TmpLocalizer.Sweep();
        }

        private void OnSceneLoaded(string scene)
        {
            EnsureAll();
            TmpLocalizer.Sweep();
            FlushMissing();
        }

        private void EnsureAll()
        {
            Guard("шрифты", () => FontFallback.Ensure(Path.Combine(folder, "fonts")));
            Guard("таблицы", () => TablesLocalizer.Ensure(catalog));
            Guard("lua", () => LuaFunctions.Register(Mod.Id));
            Guard("диалоги", () => DialogueLocalizer.Ensure(catalog, isEnabled.Value && dialogue.Value));
        }

        private void Guard(string what, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Log.Error(what + ": ошибка, эта часть перевода пропущена", e);
            }
        }

        private void OnMissed(string text)
        {
            if (!logMissing.Value || text.Length < 2 || missing.Count > 20000) return;
            if (missing.Add(text)) missingDirty = true;
        }

        private void FlushMissing()
        {
            if (!missingDirty) return;
            missingDirty = false;
            try
            {
                var lines = new List<string>(missing);
                lines.Sort(StringComparer.Ordinal);
                var sb = new StringBuilder();
                foreach (string line in lines) sb.Append(line.Replace("\r", "\\r").Replace("\n", "\\n")).Append('\n');
                File.WriteAllText(Path.Combine(folder, "untranslated.txt"), sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception e)
            {
                Log.Warn("не удалось записать untranslated.txt: " + e.Message);
            }
        }
    }
}
