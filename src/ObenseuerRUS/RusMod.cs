using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using ObenseuerCore;
using ObenseuerRUS.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ObenseuerRUS
{
    [ModInfo("obenseuerrus", "Русификатор Obenseuer", "0.5.3")]
    [ModDependency("obenseuer.moddingframework", "0.12.0")]
    public sealed class RusMod : FrameworkMod
    {
        internal static RusMod Instance;
        private Catalog catalog;
        private TextTranslator translator;
        private ModSetting<bool> isEnabled, dialogue, ui, people, brands, logMissing, checkUpdates;
        private ModKeybind toggleKey;
        private readonly HashSet<string> missing = new HashSet<string>(StringComparer.Ordinal);
        private string folder;
        private bool missingDirty;
        private float nextFlush;
        private GitHubUpdater updater;
        private string modVersion;
        private GitHubUpdater.ReleaseInfo pendingUpdate;
        private string updateNotice;
        private GUIStyle updateNoticeStyle;

        protected override void OnLoad()
        {
            Instance = this;
            folder = Path.GetDirectoryName(typeof(RusMod).Assembly.Location);
            isEnabled = Config.Bind("General", "Enabled", true, "Включить русский перевод.");
            ui = Config.Bind("General", "TranslateInterface", true, "Переводить интерфейс, предметы, задания, записки.");
            dialogue = Config.Bind("General", "TranslateDialogue", true, "Переводить диалоги.");
            // Ключи новые (были TranslatePeopleNames/TranslateBrandNames со значением false по умолчанию): у обновившихся
            // в конфиге записано старое false, которое никто не выбирал, а теперь кириллица включена по умолчанию.
            people = Config.Bind("General", "CyrillicPeopleNames", true, "Писать кириллицей имена людей (например, Арво Киркастая).");
            brands = Config.Bind("General", "CyrillicBrandNames", true, "Писать кириллицей названия брендов, заведений, организаций и мест (например, Осмо Олут, Сталбург).");
            logMissing = Config.Bind("Debug", "LogUntranslated", false, "Записывать строки интерфейса, где остался английский текст (целиком или частично), в untranslated.txt в папке мода.");
            checkUpdates = Config.Bind("Updates", "CheckForUpdates", true, "При запуске проверять, вышла ли новая версия перевода, и предлагать её установить.");
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
            people.Changed += OnSettingChanged;
            brands.Changed += OnSettingChanged;
            EnsureAll();
            TmpLocalizer.Sweep();
            if (SceneManager.GetActiveScene().name == VersionStamp.Scene) OnSceneLoaded(VersionStamp.Scene);
        }

        // Клавиша переключает General/Enabled; дальше работает тот же OnSettingChanged, что и для меню Mods.
        private void Update()
        {
            if (updater != null)
            {
                string notice;
                while (updater.TryTakeNotice(out notice)) updateNotice = notice;
                GitHubUpdater.ReleaseInfo release;
                if (pendingUpdate == null && updater.TryTakeAvailableUpdate(out release))
                {
                    pendingUpdate = release;
                    updateNotice = "Вышла новая версия перевода — " + release.Version + ".";
                }
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

            float width = Mathf.Max(1f, Mathf.Min(560f, Screen.width - 30f));
            var box = new Rect(15f, 15f, width, pendingUpdate == null ? 50f : 86f);
            GUI.Box(box, GUIContent.none);
            if (pendingUpdate == null)
            {
                GUI.Label(new Rect(box.x + 12f, box.y + 5f, box.width - 54f, box.height - 10f), updateNotice, updateNoticeStyle);
                if (GUI.Button(new Rect(box.xMax - 38f, box.y + 10f, 30f, 28f), "×")) updateNotice = null;
                return;
            }

            GUI.Label(new Rect(box.x + 12f, box.y + 5f, box.width - 24f, 34f), updateNotice, updateNoticeStyle);
            float y = box.yMax - 40f;
            if (GUI.Button(new Rect(box.x + 12f, y, 110f, 30f), "Обновить"))
            {
                GitHubUpdater.ReleaseInfo release = pendingUpdate;
                pendingUpdate = null;
                updateNotice = "Скачиваю перевод " + release.Version + "…";
                updater.DownloadAndStage(release);
            }
            else if (GUI.Button(new Rect(box.x + 130f, y, 90f, 30f), "Позже"))
            {
                pendingUpdate = null;
                updateNotice = null;
            }
            else if (GUI.Button(new Rect(box.x + 228f, y, 190f, 30f), "Больше не спрашивать"))
            {
                pendingUpdate = null;
                checkUpdates.Value = false;
                updateNotice = "Проверка обновлений выключена. Вернуть: «Моды» → Updates → CheckForUpdates.";
            }
        }

        private void StartUpdater()
        {
            AssemblyInformationalVersionAttribute version = (AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                typeof(RusMod).Assembly, typeof(AssemblyInformationalVersionAttribute));
            modVersion = version == null ? typeof(RusMod).Assembly.GetName().Version.ToString(3) : version.InformationalVersion.Split('+')[0];
            GitHubUpdater.DeleteLegacyStaging(folder);
            updater = new GitHubUpdater(folder, modVersion);
            if (checkUpdates.Value) updater.Start();
        }

        private void ShowPreviousUpdateError()
        {
            string path = Path.Combine(folder, ".github-update-error");
            if (!File.Exists(path)) return;
            try
            {
                string detail = File.ReadAllText(path, Encoding.UTF8).Trim();
                updateNotice = "Обновление перевода не установилось. Скачайте архив вручную на GitHub.";
                Log.Warn("автообновление перевода: помощник установки завершился с ошибкой: " + detail);
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
            Log.Info("загружено строк: " + catalog.Used + " (без перевода: " + catalog.Pending + "), точных " + catalog.Exact.Count + ", шаблонов " + catalog.Templates.Count + ", реплик " + catalog.Dialogue.Count + ", имён " + catalog.People.Count + ", названий " + catalog.Brands.Count);
            translator = new TextTranslator(catalog);
            translator.Missed = OnMissed;
            RusPreprocessor.Translator = translator;
            LuaFunctions.Translator = translator;
        }

        private void ApplySettings()
        {
            RusPreprocessor.Enabled = isEnabled.Value && ui.Value;
            // Имена в диалогах меняются там же, где интерфейс: окно диалога — тоже текст TextMeshPro.
            translator.Names = ProperNames.For(catalog, people.Value, brands.Value);
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
            if (scene == VersionStamp.Scene) Guard("версия в меню", () => VersionStamp.Add("Перевод " + modVersion));
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
