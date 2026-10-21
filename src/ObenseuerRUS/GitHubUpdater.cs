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
    /// <summary>Загружает и целиком устанавливает опубликованный пакет мода после выхода из игры.</summary>
    internal sealed class GitHubUpdater
    {
        private const string ApiUrl = "https://api.github.com/repos/DoctorZ1337/ObenseuerRUS/releases/latest";
        private const string DownloadPrefix = "https://github.com/DoctorZ1337/ObenseuerRUS/releases/download/";
        private const string ReleasePrefix = "https://github.com/DoctorZ1337/ObenseuerRUS/releases/";
        private const string AssetPrefix = "BepInEx/plugins/ObenseuerRUS/";
        private const int MaxArchiveBytes = 64 * 1024 * 1024;
        private const string InstallerScript = @"
param([int]$GamePid, [string]$SourceDir, [string]$DestinationDir, [string]$BackupDir)
$ErrorActionPreference = 'Stop'
$errorFile = Join-Path $DestinationDir '.github-update-error'
$created = @()
$previous = @{}
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
    New-Item -ItemType Directory -Force -Path $BackupDir | Out-Null
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($sourceRoot.Length).Replace('\', '/')
        $target = Join-Path $DestinationDir ($relative.Replace('/', '\'))
        if (Test-Path -LiteralPath $target -PathType Leaf) {
            $backup = Join-Path $BackupDir ($relative.Replace('/', '\'))
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $backup) | Out-Null
            Copy-Item -LiteralPath $target -Destination $backup -Force
            $previous[$target] = $backup
        } else { $created += $target }
    }
    foreach ($file in $files) {
        $relative = $file.FullName.Substring($sourceRoot.Length).Replace('\', '/')
        $target = Join-Path $DestinationDir ($relative.Replace('/', '\'))
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $target) | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $target -Force
    }
} catch {
    foreach ($target in $previous.Keys) { try { Copy-Item -LiteralPath $previous[$target] -Destination $target -Force } catch {} }
    foreach ($target in $created) { try { Remove-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue } catch {} }
    try { Set-Content -LiteralPath $errorFile -Value $_.Exception.Message -Encoding UTF8 } catch {}
    exit 1
}
Remove-Item -LiteralPath $BackupDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $SourceDir -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $errorFile -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath $PSCommandPath -Force -ErrorAction SilentlyContinue
";

        private readonly string pluginFolder;
        private readonly Version currentVersion;
        private readonly ConcurrentQueue<string> notices = new ConcurrentQueue<string>();
        private string error;

        internal GitHubUpdater(string pluginFolder, string currentVersion)
        {
            this.pluginFolder = pluginFolder;
            this.currentVersion = ParseVersion(currentVersion);
        }

        internal void Start()
        {
            ThreadPool.QueueUserWorkItem(_ => Run());
        }

        internal bool TryTakeNotice(out string notice) => notices.TryDequeue(out notice);

        internal string TakeError() => Interlocked.Exchange(ref error, null);

        private void Run()
        {
            string stageDirectory = null;
            string scriptPath = null;
            try
            {
                ReleaseInfo release = ParseLatestRelease(GetText(ApiUrl), currentVersion);
                if (release == null) return;

                if (release.AssetUrl == null)
                {
                    notices.Enqueue("Найдена версия мода " + release.Version + ", но архив ObenseuerRUS-" + release.Version + ".zip ещё не приложен к GitHub-релизу.");
                    return;
                }

                notices.Enqueue("Найдена версия мода " + release.Version + ". Скачиваю полный пакет…");
                byte[] archive = GetBytes(release.AssetUrl, release.Size);
                VerifySha256(archive, release.Sha256);
                Dictionary<string, byte[]> files = ReadPackageFiles(archive);

                string pluginsDirectory = Directory.GetParent(pluginFolder).FullName;
                string token = Guid.NewGuid().ToString("N");
                stageDirectory = Path.Combine(pluginsDirectory, ".ObenseuerRUS-update-" + token);
                scriptPath = Path.Combine(pluginsDirectory, ".ObenseuerRUS-update-" + token + ".ps1");
                ExtractPackage(files, stageDirectory);
                File.WriteAllText(scriptPath, InstallerScript, new UTF8Encoding(true));
                StartInstaller(scriptPath, stageDirectory, pluginFolder);
                stageDirectory = null; // Помощник установит и удалит staging после закрытия игры.
                scriptPath = null;
                notices.Enqueue("Полный мод " + release.Version + " скачан. Он установится после закрытия игры; затем запустите игру снова.");
            }
            catch (Exception e)
            {
                Interlocked.Exchange(ref error, "автообновление мода: " + e.Message);
                notices.Enqueue("Не удалось подготовить автообновление мода. Проверьте подключение и журнал игры.");
                DeleteStage(stageDirectory);
                DeleteFile(scriptPath);
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

        private void StartInstaller(string scriptPath, string stageDirectory, string destinationDirectory)
        {
            string backupDirectory = stageDirectory + ".backup";
            var start = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -WindowStyle Hidden -File " + QuoteArgument(scriptPath)
                    + " -GamePid " + Process.GetCurrentProcess().Id
                    + " -SourceDir " + QuoteArgument(stageDirectory)
                    + " -DestinationDir " + QuoteArgument(destinationDirectory)
                    + " -BackupDir " + QuoteArgument(backupDirectory),
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            using (Process process = Process.Start(start))
            {
                if (process == null) throw new InvalidOperationException("Windows не запустила помощник установки.");
            }
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
