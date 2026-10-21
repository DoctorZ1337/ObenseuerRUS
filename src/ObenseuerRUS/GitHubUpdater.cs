using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
    /// <summary>Проверяет публичный GitHub-релиз и устанавливает из него только JSON-файлы перевода.</summary>
    internal sealed class GitHubUpdater
    {
        private const string ApiUrl = "https://api.github.com/repos/DoctorZ1337/ObenseuerRUS/releases/latest";
        private const string DownloadPrefix = "https://github.com/DoctorZ1337/ObenseuerRUS/releases/download/";
        private const string ReleasePrefix = "https://github.com/DoctorZ1337/ObenseuerRUS/releases/";
        private const string AssetPrefix = "BepInEx/plugins/ObenseuerRUS/translations/";
        private const int MaxArchiveBytes = 64 * 1024 * 1024;
        private readonly string pluginFolder;
        private readonly Version currentVersion;
        private readonly ConcurrentQueue<string> notices = new ConcurrentQueue<string>();
        private string error;

        internal GitHubUpdater(string pluginFolder, string currentVersion)
        {
            this.pluginFolder = pluginFolder;
            this.currentVersion = GetEffectiveVersion(pluginFolder, currentVersion);
        }

        internal static Version GetEffectiveVersion(string pluginFolder, string currentVersion)
        {
            Version installedVersion = ParseVersion(currentVersion);
            string marker = Path.Combine(pluginFolder, ".github-update-version");
            try
            {
                Version applied = ParseVersion(File.ReadAllText(marker, Encoding.UTF8).Trim());
                if (applied > installedVersion) installedVersion = applied;
            }
            catch { }
            return installedVersion;
        }

        internal void Start()
        {
            ThreadPool.QueueUserWorkItem(_ => Run());
        }

        internal bool TryTakeNotice(out string notice) => notices.TryDequeue(out notice);

        internal string TakeError() => Interlocked.Exchange(ref error, null);

        private void Run()
        {
            try
            {
                ReleaseInfo release = ParseLatestRelease(GetText(ApiUrl), currentVersion);
                if (release == null) return;

                if (release.AssetUrl == null)
                {
                    notices.Enqueue("Найдена версия перевода " + release.Version + ", но архив ObenseuerRUS-" + release.Version + ".zip ещё не приложен к GitHub-релизу.");
                    return;
                }

                notices.Enqueue("Найдена версия перевода " + release.Version + ". Скачиваю и устанавливаю обновление…");
                byte[] archive = GetBytes(release.AssetUrl, release.Size);
                VerifySha256(archive, release.Sha256);
                Dictionary<string, byte[]> files = ReadTranslationFiles(archive);
                InstallTranslations(files, Path.Combine(pluginFolder, "translations"));
                File.WriteAllText(Path.Combine(pluginFolder, ".github-update-version"), release.Version.ToString(3), new UTF8Encoding(false));
                notices.Enqueue("Перевод обновлён до " + release.Version + ". Перезапустите игру, чтобы применить его.");
            }
            catch (Exception e)
            {
                Interlocked.Exchange(ref error, "автообновление перевода: " + e.Message);
                notices.Enqueue("Не удалось завершить автообновление перевода. Проверьте подключение и журнал игры.");
            }
        }

        internal static ReleaseInfo ParseLatestRelease(string json, Version currentVersion)
        {
            JObject release = JObject.Parse(json);
            if ((bool?)release["draft"] == true || (bool?)release["prerelease"] == true) return null;

            string tag = (string)release["tag_name"];
            Version version = ParseVersion(tag);
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

                string digest = (string)asset["digest"];
                Match match = Regex.Match(digest ?? "", "^sha256:([A-Fa-f0-9]{64})$");
                if (!match.Success) throw new InvalidDataException("Для архива GitHub не указал SHA-256.");

                long size = (long?)asset["size"] ?? 0;
                if (size <= 0 || size > MaxArchiveBytes) throw new InvalidDataException("Недопустимый размер архива обновления.");

                return new ReleaseInfo(version, url, match.Groups[1].Value, size);
            }

            string releaseUrl = (string)release["html_url"];
            if (!string.IsNullOrEmpty(releaseUrl) && !releaseUrl.StartsWith(ReleasePrefix, StringComparison.Ordinal))
                throw new InvalidDataException("Ссылка на GitHub-релиз некорректна.");
            return new ReleaseInfo(version, null, null, 0);
        }

        internal static Dictionary<string, byte[]> ReadTranslationFiles(byte[] archive)
        {
            var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            using (var stream = new MemoryStream(archive, writable: false))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Read))
            {
                long total = 0;
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    if (!entry.FullName.StartsWith(AssetPrefix, StringComparison.Ordinal)) continue;

                    string name = entry.FullName.Substring(AssetPrefix.Length);
                    if (!Regex.IsMatch(name, "^[A-Za-z0-9._-]+\\.json$"))
                        throw new InvalidDataException("В архиве найден недопустимый путь перевода.");
                    if (entry.Length <= 0 || entry.Length > 10 * 1024 * 1024)
                        throw new InvalidDataException("Недопустимый размер файла перевода " + name + ".");
                    if (files.ContainsKey(name)) throw new InvalidDataException("В архиве повторяется файл " + name + ".");

                    byte[] bytes;
                    using (Stream input = entry.Open())
                    using (var output = new MemoryStream((int)entry.Length))
                    {
                        byte[] buffer = new byte[81920];
                        int read;
                        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            if (output.Length + read > 10 * 1024 * 1024)
                                throw new InvalidDataException("Файл перевода " + name + " превышает допустимый размер.");
                            output.Write(buffer, 0, read);
                        }
                        bytes = output.ToArray();
                    }
                    if (bytes.LongLength != entry.Length) throw new InvalidDataException("Размер файла " + name + " в архиве не совпадает с заголовком.");
                    total += bytes.LongLength;
                    if (total > 32 * 1024 * 1024) throw new InvalidDataException("Файлы перевода в архиве слишком велики.");

                    string json = new UTF8Encoding(false, true).GetString(bytes);
                    if (json.Length > 0 && json[0] == '\uFEFF') json = json.Substring(1);
                    CatalogFile catalog = Newtonsoft.Json.JsonConvert.DeserializeObject<CatalogFile>(json);
                    if (catalog == null || catalog.Format != CatalogFile.CurrentFormat)
                        throw new InvalidDataException("Файл " + name + " имеет неподдерживаемый формат перевода.");
                    files.Add(name, bytes);
                }
            }

            if (files.Count == 0) throw new InvalidDataException("В архиве не найдено ни одного файла перевода.");
            return files;
        }

        internal static void InstallTranslations(Dictionary<string, byte[]> files, string directory)
        {
            string token = Guid.NewGuid().ToString("N");
            var staged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var previous = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
            var missingBefore = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                foreach (KeyValuePair<string, byte[]> file in files)
                {
                    string destination = Path.Combine(directory, file.Key);
                    string temporary = destination + "." + token + ".tmp";
                    if (File.Exists(destination)) previous.Add(destination, File.ReadAllBytes(destination));
                    else missingBefore.Add(destination);
                    staged.Add(destination, temporary);
                    File.WriteAllBytes(temporary, file.Value);
                }

                foreach (KeyValuePair<string, string> item in staged)
                {
                    if (File.Exists(item.Key)) File.Copy(item.Value, item.Key, overwrite: true);
                    else File.Move(item.Value, item.Key);
                }
            }
            catch
            {
                foreach (KeyValuePair<string, byte[]> item in previous)
                {
                    try { File.WriteAllBytes(item.Key, item.Value); }
                    catch { }
                }
                foreach (string path in missingBefore)
                {
                    try { if (File.Exists(path)) File.Delete(path); }
                    catch { }
                }
                throw;
            }
            finally
            {
                foreach (string temporary in staged.Values)
                {
                    try { if (File.Exists(temporary)) File.Delete(temporary); }
                    catch { }
                }
            }
        }

        private static string GetText(string url)
        {
            byte[] bytes = GetBytes(url, 1024 * 1024);
            return new UTF8Encoding(false, true).GetString(bytes);
        }

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
