using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json.Linq;
using ObenseuerRUS.Core;

namespace ObenseuerRUS
{
    /// <summary>Загружает и целиком устанавливает опубликованный пакет перевода после выхода из игры.</summary>
    internal sealed class GitHubUpdater
    {
        private const string ApiUrl = "https://api.github.com/repos/DoctorZ1337/ObenseuerRUS/releases/latest";
        private const string DownloadPrefix = "https://github.com/DoctorZ1337/ObenseuerRUS/releases/download/";
        private const string ReleasePrefix = "https://github.com/DoctorZ1337/ObenseuerRUS/releases/";
        private const string AssetPrefix = "BepInEx/plugins/ObenseuerRUS/";
        private const string StagePrefix = "ObenseuerRUS-update-";
        private const int MaxArchiveBytes = 64 * 1024 * 1024;
        // Помощник установки: ждёт выхода из игры, копирует пакет с резервной копией заменяемых файлов,
        // удаляет переводы, которых больше нет в пакете (иначе их загрузит Catalog), при ошибке всё возвращает.
        internal const string InstallerScript = @"
param([int]$GamePid, [string]$SourceDir, [string]$DestinationDir, [string]$BackupDir)
$ErrorActionPreference = 'Stop'
$errorFile = Join-Path $DestinationDir '.github-update-error'
$created = @()
$previous = @{}
$failed = $false
try {
    while (Get-Process -Id $GamePid -ErrorAction SilentlyContinue) { Start-Sleep -Seconds 2 }
    $sourceRoot = [IO.Path]::GetFullPath($SourceDir).TrimEnd('\') + '\'
    $files = @(Get-ChildItem -LiteralPath $SourceDir -File -Recurse)
    $relativePaths = @()
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($sourceRoot.Length).Replace('\', '/')
        if ($relative -notmatch '^(ObenseuerRUS\.dll|README\.md|translations/[A-Za-z0-9._-]+\.json|fonts/(ObenseuerTypewriterCyrillic\.ttf|COPYING-FreeFont\.txt|FONT-NOTICE\.txt|make_cyrillic_font\.py))$') { throw 'Путь в подготовленном пакете не разрешён.' }
        $relativePaths += $relative
    }
    foreach ($required in @('ObenseuerRUS.dll', 'README.md', 'translations/ui.json', 'fonts/ObenseuerTypewriterCyrillic.ttf', 'fonts/COPYING-FreeFont.txt', 'fonts/FONT-NOTICE.txt', 'fonts/make_cyrillic_font.py')) {
        if ($relativePaths -notcontains $required) { throw ""Не найден обязательный файл $required."" }
    }
    $obsolete = @()
    $translationsDir = Join-Path $DestinationDir 'translations'
    if (Test-Path -LiteralPath $translationsDir) {
        $obsolete = @(Get-ChildItem -LiteralPath $translationsDir -Filter '*.json' -File | Where-Object { $relativePaths -notcontains ('translations/' + $_.Name) })
    }
    New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null
    $targets = @($files | ForEach-Object { Join-Path $DestinationDir $_.FullName.Substring($sourceRoot.Length) }) + @($obsolete | ForEach-Object { $_.FullName })
    foreach ($target in $targets) {
        if (Test-Path -LiteralPath $target -PathType Leaf) {
            $backup = Join-Path $BackupDir ([IO.Path]::GetFullPath($target).Substring([IO.Path]::GetFullPath($DestinationDir).TrimEnd('\').Length + 1))
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $backup) | Out-Null
            Copy-Item -LiteralPath $target -Destination $backup -Force
            $previous[$target] = $backup
        } else { $created += $target }
    }
    foreach ($file in $files) {
        $target = Join-Path $DestinationDir $file.FullName.Substring($sourceRoot.Length)
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    }
    foreach ($file in $obsolete) { Remove-Item -LiteralPath $file.FullName -Force }
} catch {
    $failed = $true
    foreach ($target in $previous.Keys) { try { Copy-Item -LiteralPath $previous[$target] -Destination $target -Force } catch {} }
    foreach ($target in $created) { try { Remove-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue } catch {} }
    try { Set-Content -LiteralPath $errorFile -Value $_.Exception.Message -Encoding UTF8 } catch {}
}
Remove-Item -LiteralPath $SourceDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
if ($failed) { exit 1 }
# Резервная копия нужна только при неудаче: если вернуть файлы не удалось, она остаётся во временной папке.
Remove-Item -LiteralPath $BackupDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $errorFile -Force -ErrorAction SilentlyContinue
";

        private readonly string pluginFolder;
        private readonly Version currentVersion;
        private readonly ConcurrentQueue<string> notices = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<ReleaseInfo> availableUpdates = new ConcurrentQueue<ReleaseInfo>();
        private int isDownloading;
        private string error;

        internal GitHubUpdater(string pluginFolder, string currentVersion)
        {
            this.pluginFolder = pluginFolder;
            this.currentVersion = ParseVersion(currentVersion);
        }

        internal void Start()
        {
            ThreadPool.QueueUserWorkItem(_ => CheckForUpdate());
        }

        internal bool TryTakeNotice(out string notice) => notices.TryDequeue(out notice);

        internal bool TryTakeAvailableUpdate(out ReleaseInfo release) => availableUpdates.TryDequeue(out release);

        internal void DownloadAndStage(ReleaseInfo release)
        {
            if (release == null || release.AssetUrl == null || Interlocked.CompareExchange(ref isDownloading, 1, 0) != 0) return;
            ThreadPool.QueueUserWorkItem(_ => DownloadAndStageWorker(release));
        }

        internal string TakeError() => Interlocked.Exchange(ref error, null);

        // Сбой проверки (нет сети, лимит GitHub) и релиз без архива окно не показывают: игрок ничего не может с этим сделать.
        private void CheckForUpdate()
        {
            try
            {
                ReleaseInfo release = ParseLatestRelease(GetText(ApiUrl), currentVersion);
                if (release == null) return;
                if (release.AssetUrl == null)
                {
                    Interlocked.Exchange(ref error, "автообновление перевода: у релиза " + release.Version + " нет архива ObenseuerRUS-" + release.Version + ".zip");
                    return;
                }
                availableUpdates.Enqueue(release);
            }
            catch (Exception e)
            {
                Interlocked.Exchange(ref error, "проверка обновлений перевода: " + e.Message);
            }
        }

        private void DownloadAndStageWorker(ReleaseInfo release)
        {
            string stageDirectory = null;
            string scriptPath = null;
            try
            {
                byte[] archive = GetBytes(release.AssetUrl, release.Size);
                VerifySha256(archive, release.Sha256);
                Dictionary<string, byte[]> files = ReadPackageFiles(archive);

                // Пакет и резервная копия — во временной папке: фреймворк загружает моды из всех DLL под BepInEx\plugins,
                // и копия ObenseuerRUS.dll, оставшаяся там после сбоя, загрузилась бы вместо настоящей.
                stageDirectory = Path.Combine(Path.GetTempPath(), StagePrefix + Guid.NewGuid().ToString("N"));
                scriptPath = stageDirectory + ".ps1";
                ExtractPackage(files, stageDirectory);
                File.WriteAllText(scriptPath, InstallerScript, new UTF8Encoding(true));
                StartInstaller(scriptPath, stageDirectory, pluginFolder);
                stageDirectory = null; // Помощник установит и удалит пакет после выхода из игры.
                scriptPath = null;
                notices.Enqueue("Перевод " + release.Version + " скачан и установится, когда вы выйдете из игры.");
            }
            catch (Exception e)
            {
                Interlocked.Exchange(ref error, "автообновление перевода: " + e.Message);
                notices.Enqueue("Не удалось скачать перевод " + release.Version + ". Подробности — в журнале BepInEx.");
                DeleteStage(stageDirectory);
                DeleteFile(scriptPath);
            }
            finally
            {
                Interlocked.Exchange(ref isDownloading, 0);
            }
        }

        /// <summary>
        /// Остатки обновлений до 0.4.9: пакет и резервная копия лежали в BepInEx\plugins\.ObenseuerRUS-update-*.
        /// После сбоя установки фреймворк нашёл бы там вторую ObenseuerRUS.dll.
        /// Если он уже загрузил перевод из такой папки (она идёт раньше ObenseuerRUS), её не трогаем:
        /// занятая DLL осталась бы, а переводы удалились, и следующий запуск прошёл бы без перевода.
        /// </summary>
        internal static void DeleteLegacyStaging(string pluginFolder)
        {
            DirectoryInfo plugins = Directory.GetParent(pluginFolder);
            if (plugins == null || !plugins.Exists) return;
            string running = Path.GetFullPath(pluginFolder).TrimEnd(Path.DirectorySeparatorChar);
            foreach (FileSystemInfo item in plugins.GetFileSystemInfos("." + StagePrefix + "*"))
            {
                if (string.Equals(Path.GetFullPath(item.FullName).TrimEnd(Path.DirectorySeparatorChar), running, StringComparison.OrdinalIgnoreCase)) continue;
                if (item is DirectoryInfo) DeleteStage(item.FullName);
                else DeleteFile(item.FullName);
            }
        }

        internal static ReleaseInfo ParseLatestRelease(string json, Version currentVersion)
        {
            JObject release = JObject.Parse(json);
            if ((bool?)release["draft"] == true || (bool?)release["prerelease"] == true) return null;

            Version version = ParseVersion((string)release["tag_name"]);
            if (version <= currentVersion) return null;

            string expectedAsset = "ObenseuerRUS-" + version + ".zip";
            JArray assets = release["assets"] as JArray;
            if (assets == null) throw new InvalidDataException("GitHub-релиз не содержит списка файлов.");

            foreach (JObject asset in assets)
            {
                if (!string.Equals((string)asset["name"], expectedAsset, StringComparison.Ordinal)) continue;

                string url = (string)asset["browser_download_url"];
                if (string.IsNullOrEmpty(url) || !url.StartsWith(DownloadPrefix, StringComparison.Ordinal))
                    throw new InvalidDataException("Ссылка на архив ведёт за пределы репозитория.");

                Match digest = Regex.Match((string)asset["digest"] ?? "", "^sha256:([A-Fa-f0-9]{64})$");
                if (!digest.Success) throw new InvalidDataException("Для архива GitHub не указал SHA-256.");

                long size = (long?)asset["size"] ?? 0;
                if (size <= 0 || size > MaxArchiveBytes) throw new InvalidDataException("Недопустимый размер архива обновления.");
                return new ReleaseInfo(version, url, digest.Groups[1].Value, size);
            }

            string releaseUrl = (string)release["html_url"];
            if (!string.IsNullOrEmpty(releaseUrl) && !releaseUrl.StartsWith(ReleasePrefix, StringComparison.Ordinal))
                throw new InvalidDataException("Ссылка на GitHub-релиз некорректна.");
            return new ReleaseInfo(version, null, null, 0);
        }

        internal static Dictionary<string, byte[]> ReadPackageFiles(byte[] archive)
        {
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            using (var stream = new MemoryStream(archive, writable: false))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                long total = 0;
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    if (!entry.FullName.StartsWith(AssetPrefix, StringComparison.Ordinal))
                        throw new InvalidDataException("Архив содержит файл вне папки мода.");
                    string name = entry.FullName.Substring(AssetPrefix.Length);
                    if (!IsAllowedPackagePath(name)) throw new InvalidDataException("В архиве найден недопустимый путь или файл.");
                    if (entry.Length <= 0 || entry.Length > 32 * 1024 * 1024)
                        throw new InvalidDataException("Недопустимый размер файла " + name + ".");
                    if (files.ContainsKey(name)) throw new InvalidDataException("В архиве повторяется файл " + name + ".");

                    byte[] bytes;
                    using (Stream input = entry.Open())
                    using (var output = new MemoryStream((int)entry.Length))
                    {
                        byte[] buffer = new byte[81920];
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            if (output.Length + read > 32 * 1024 * 1024)
                                throw new InvalidDataException("Файл " + name + " превышает допустимый размер.");
                            output.Write(buffer, 0, read);
                        }
                        bytes = output.ToArray();
                    }
                    if (bytes.LongLength != entry.Length) throw new InvalidDataException("Размер файла " + name + " в архиве не совпадает с заголовком.");
                    total += bytes.LongLength;
                    if (total > MaxArchiveBytes) throw new InvalidDataException("Файлы в архиве слишком велики.");
                    if (name.StartsWith("translations/", StringComparison.Ordinal)) ValidateTranslation(name, bytes);
                    files.Add(name, bytes);
                }
            }

            string[] required =
            {
                "ObenseuerRUS.dll", "README.md", "translations/ui.json",
                "fonts/ObenseuerTypewriterCyrillic.ttf", "fonts/COPYING-FreeFont.txt",
                "fonts/FONT-NOTICE.txt", "fonts/make_cyrillic_font.py"
            };
            foreach (string name in required)
                if (!files.ContainsKey(name)) throw new InvalidDataException("В полном пакете отсутствует " + name + ".");
            return files;
        }

        /// <summary>Запускает помощник установки (для теста — ждёт процесс gamePid, а не игру).</summary>
        internal static Process RunInstaller(string scriptPath, int gamePid, string stageDirectory, string destinationDirectory)
        {
            var start = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File " + QuoteArgument(scriptPath)
                    + " -GamePid " + gamePid
                    + " -SourceDir " + QuoteArgument(stageDirectory)
                    + " -DestinationDir " + QuoteArgument(destinationDirectory)
                    + " -BackupDir " + QuoteArgument(stageDirectory + ".backup"),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            Process process = Process.Start(start);
            if (process == null) throw new InvalidOperationException("Windows не запустила помощник установки.");
            return process;
        }

        internal static void ExtractPackage(Dictionary<string, byte[]> files, string stageDirectory)
        {
            Directory.CreateDirectory(stageDirectory);
            foreach (KeyValuePair<string, byte[]> file in files)
            {
                if (!IsAllowedPackagePath(file.Key)) throw new InvalidDataException("Недопустимый путь файла пакета.");
                string destination = Path.Combine(stageDirectory, file.Key.Replace('/', Path.DirectorySeparatorChar));
                string directory = Path.GetDirectoryName(destination);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                File.WriteAllBytes(destination, file.Value);
            }
        }

        private static void StartInstaller(string scriptPath, string stageDirectory, string destinationDirectory)
        {
            using (RunInstaller(scriptPath, Process.GetCurrentProcess().Id, stageDirectory, destinationDirectory)) { }
        }

        private static bool IsAllowedPackagePath(string path)
        {
            return path == "ObenseuerRUS.dll" || path == "README.md"
                || Regex.IsMatch(path, "^translations/[A-Za-z0-9._-]+\\.json$")
                || Regex.IsMatch(path, "^fonts/(ObenseuerTypewriterCyrillic\\.ttf|COPYING-FreeFont\\.txt|FONT-NOTICE\\.txt|make_cyrillic_font\\.py)$");
        }

        private static void ValidateTranslation(string name, byte[] bytes)
        {
            string json = new UTF8Encoding(false, true).GetString(bytes);
            if (json.Length > 0 && json[0] == '\uFEFF') json = json.Substring(1);
            CatalogFile catalog = Newtonsoft.Json.JsonConvert.DeserializeObject<CatalogFile>(json);
            if (catalog == null || catalog.Format != CatalogFile.CurrentFormat)
                throw new InvalidDataException("Файл " + name + " имеет неподдерживаемый формат перевода.");
        }

        private static string QuoteArgument(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";

        private static void DeleteStage(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && Directory.Exists(path)) Directory.Delete(path, recursive: true); }
            catch { }
        }

        private static void DeleteFile(string path)
        {
            try { if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path); }
            catch { }
        }

        private static string GetText(string url) => new UTF8Encoding(false, true).GetString(GetBytes(url, 1024 * 1024));

        private static byte[] GetBytes(string url, long maxBytes)
        {
#pragma warning disable SYSLIB0014 // HttpWebRequest нужен для совместимости со штатным Mono/.NET Framework игры.
            var request = (HttpWebRequest)WebRequest.Create(url);
#pragma warning restore SYSLIB0014
            request.Method = "GET";
            request.UserAgent = "ObenseuerRUS-Updater";
            request.Timeout = 15000;
            request.ReadWriteTimeout = 30000;
            request.Accept = url == ApiUrl ? "application/vnd.github+json" : "application/octet-stream";
            using (var response = (HttpWebResponse)request.GetResponse())
            using (Stream input = response.GetResponseStream())
            using (var output = new MemoryStream())
            {
                if (response.ContentLength > maxBytes) throw new InvalidDataException("Ответ GitHub превышает допустимый размер.");
                byte[] buffer = new byte[81920];
                int read;
                while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + read > maxBytes) throw new InvalidDataException("Ответ GitHub превышает допустимый размер.");
                    output.Write(buffer, 0, read);
                }
                return output.ToArray();
            }
        }

        private static void VerifySha256(byte[] archive, string expected)
        {
            using (SHA256 sha = SHA256.Create())
            {
                string actual = BitConverter.ToString(sha.ComputeHash(archive)).Replace("-", "");
                if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Контрольная сумма архива не совпадает.");
            }
        }

        private static Version ParseVersion(string value)
        {
            Match match = Regex.Match(value ?? "", "^v?(\\d+)\\.(\\d+)\\.(\\d+)(?:\\+[0-9A-Za-z.-]+)?$");
            if (!match.Success) throw new InvalidDataException("Не удалось разобрать версию " + value + ".");
            return new Version(int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value));
        }

        internal sealed class ReleaseInfo
        {
            internal readonly Version Version;
            internal readonly string AssetUrl, Sha256;
            internal readonly long Size;

            internal ReleaseInfo(Version version, string assetUrl, string sha256, long size)
            {
                Version = version;
                AssetUrl = assetUrl;
                Sha256 = sha256;
                Size = size;
            }
        }
    }
}
