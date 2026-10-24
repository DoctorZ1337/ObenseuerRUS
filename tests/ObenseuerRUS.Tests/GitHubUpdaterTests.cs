using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using ObenseuerRUS;
using Xunit;

namespace ObenseuerRUS.Tests
{
    public sealed class GitHubUpdaterTests
    {
        [Fact]
        public void DetectsNewReleaseAndRequiresNamedAsset()
        {
            var release = GitHubUpdater.ParseLatestRelease(ReleaseJson("v0.4.2", "ObenseuerRUS-0.4.2.zip"), new Version(0, 4, 1));

            Assert.Equal(new Version(0, 4, 2), release.Version);
            Assert.Equal("https://github.com/DoctorZ1337/ObenseuerRUS/releases/download/v0.4.2/ObenseuerRUS-0.4.2.zip", release.AssetUrl);
            Assert.Equal(123, release.Size);
        }

        [Fact]
        public void IgnoresCurrentVersionAndReportsMissingAsset()
        {
            Assert.Null(GitHubUpdater.ParseLatestRelease(ReleaseJson("v0.4.1", "ObenseuerRUS-0.4.1.zip"), new Version(0, 4, 1)));

            var release = GitHubUpdater.ParseLatestRelease(ReleaseJson("v0.4.2", "source.zip"), new Version(0, 4, 1));
            Assert.Null(release.AssetUrl);
        }

        [Fact]
        public void AcceptsCompletePackageAndChecksEveryTranslationFormat()
        {
            Dictionary<string, byte[]> files = GitHubUpdater.ReadPackageFiles(ValidPackage());

            Assert.Equal(7, files.Count);
            Assert.Contains("ObenseuerRUS.dll", files.Keys);
            Assert.Contains("translations/ui.json", files.Keys);
        }

        [Fact]
        public void ExtractsTheFullModIntoStaging()
        {
            string stage = TestFolder();
            try
            {
                Dictionary<string, byte[]> files = GitHubUpdater.ReadPackageFiles(ValidPackage());
                GitHubUpdater.ExtractPackage(files, stage);

                Assert.True(File.Exists(Path.Combine(stage, "ObenseuerRUS.dll")));
                Assert.True(File.Exists(Path.Combine(stage, "fonts", "ObenseuerTypewriterCyrillic.ttf")));
                Assert.Equal("{\"format\":\"obenseuer-rus/1\"}", File.ReadAllText(Path.Combine(stage, "translations", "ui.json")));
            }
            finally
            {
                if (Directory.Exists(stage)) Directory.Delete(stage, recursive: true);
            }
        }

        [Fact]
        public void RejectsTraversalUnknownFilesAndInvalidTranslationFormat()
        {
            var traversal = RequiredFiles();
            traversal.Add(Entry("translations/../escape.json", "{}"));
            Assert.Throws<InvalidDataException>(() => GitHubUpdater.ReadPackageFiles(Package(traversal.ToArray())));

            var unknown = RequiredFiles();
            unknown.Add(Entry("update.ps1", "Write-Host bad"));
            Assert.Throws<InvalidDataException>(() => GitHubUpdater.ReadPackageFiles(Package(unknown.ToArray())));

            Assert.Throws<InvalidDataException>(() => GitHubUpdater.ReadPackageFiles(ValidPackage("{\"format\":\"other\"}")));
        }

        [Fact]
        public void DeletesLegacyStagingFromPluginsFolder()
        {
            string plugins = TestFolder();
            try
            {
                string mod = Path.Combine(plugins, "ObenseuerRUS");
                Directory.CreateDirectory(mod);
                Directory.CreateDirectory(Path.Combine(plugins, ".ObenseuerRUS-update-abc"));
                File.WriteAllText(Path.Combine(plugins, ".ObenseuerRUS-update-abc", "ObenseuerRUS.dll"), "old");
                Directory.CreateDirectory(Path.Combine(plugins, ".ObenseuerRUS-update-abc.backup"));
                File.WriteAllText(Path.Combine(plugins, ".ObenseuerRUS-update-abc.ps1"), "script");
                Directory.CreateDirectory(Path.Combine(plugins, "OtherMod"));

                GitHubUpdater.DeleteLegacyStaging(mod);

                Assert.Equal(new[] { "ObenseuerRUS", "OtherMod" }, SortedNames(plugins));
            }
            finally
            {
                if (Directory.Exists(plugins)) Directory.Delete(plugins, recursive: true);
            }
        }

        [Fact]
        public void InstallerReplacesFilesAndRemovesObsoleteTranslations()
        {
            if (!OperatingSystem.IsWindows()) return;
            string root = TestFolder();
            try
            {
                string dest = Path.Combine(root, "ObenseuerRUS"), stage = Path.Combine(root, "stage"), script = stage + ".ps1";
                Directory.CreateDirectory(Path.Combine(dest, "translations"));
                File.WriteAllText(Path.Combine(dest, "ObenseuerRUS.dll"), "old dll");
                File.WriteAllText(Path.Combine(dest, "translations", "ui.json"), "old ui");
                File.WriteAllText(Path.Combine(dest, "translations", "removed.json"), "obsolete");
                File.WriteAllText(Path.Combine(dest, "untranslated.txt"), "player log");
                GitHubUpdater.ExtractPackage(GitHubUpdater.ReadPackageFiles(ValidPackage()), stage);

                Assert.Equal(0, RunInstaller(script, stage, dest));

                Assert.Equal("dll", File.ReadAllText(Path.Combine(dest, "ObenseuerRUS.dll")));
                Assert.Equal(new[] { "ui.json" }, SortedNames(Path.Combine(dest, "translations")));
                Assert.True(File.Exists(Path.Combine(dest, "untranslated.txt")));
                Assert.False(File.Exists(Path.Combine(dest, ".github-update-error")));
                Assert.Equal(new[] { "ObenseuerRUS" }, SortedNames(root));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void InstallerKeepsOldFilesAndReportsFailure()
        {
            if (!OperatingSystem.IsWindows()) return;
            string root = TestFolder();
            try
            {
                string dest = Path.Combine(root, "ObenseuerRUS"), stage = Path.Combine(root, "stage"), script = stage + ".ps1";
                Directory.CreateDirectory(Path.Combine(dest, "translations"));
                File.WriteAllText(Path.Combine(dest, "ObenseuerRUS.dll"), "old dll");
                File.WriteAllText(Path.Combine(dest, "translations", "removed.json"), "obsolete");
                GitHubUpdater.ExtractPackage(GitHubUpdater.ReadPackageFiles(ValidPackage()), stage);
                File.WriteAllText(Path.Combine(stage, "evil.ps1"), "not allowed");

                Assert.Equal(1, RunInstaller(script, stage, dest));

                Assert.Equal("old dll", File.ReadAllText(Path.Combine(dest, "ObenseuerRUS.dll")));
                Assert.True(File.Exists(Path.Combine(dest, "translations", "removed.json")));
                Assert.True(File.Exists(Path.Combine(dest, ".github-update-error")));
                Assert.False(Directory.Exists(stage));
            }
            finally
            {
                if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
            }
        }

        private static int RunInstaller(string script, string stage, string dest)
        {
            File.WriteAllText(script, GitHubUpdater.InstallerScript, new UTF8Encoding(true));
            int finishedPid;
            using (var game = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c exit") { CreateNoWindow = true, UseShellExecute = false }))
            {
                game.WaitForExit();
                finishedPid = game.Id;
            }
            using (var installer = GitHubUpdater.RunInstaller(script, finishedPid, stage, dest))
            {
                Assert.True(installer.WaitForExit(60000));
                return installer.ExitCode;
            }
        }

        private static string[] SortedNames(string folder)
        {
            string[] names = Array.ConvertAll(Directory.GetFileSystemEntries(folder), Path.GetFileName);
            Array.Sort(names, StringComparer.Ordinal);
            return names;
        }

        private static string ReleaseJson(string tag, string assetName)
        {
            return "{\"tag_name\":\"" + tag + "\",\"draft\":false,\"prerelease\":false,\"html_url\":\"https://github.com/DoctorZ1337/ObenseuerRUS/releases/tag/" + tag + "\",\"assets\":[{\"name\":\"" + assetName + "\",\"browser_download_url\":\"https://github.com/DoctorZ1337/ObenseuerRUS/releases/download/" + tag + "/" + assetName + "\",\"digest\":\"sha256:" + new string('a', 64) + "\",\"size\":123}]}";
        }

        private static byte[] ValidPackage(string uiJson = "{\"format\":\"obenseuer-rus/1\"}")
        {
            return Package(RequiredFiles(uiJson).ToArray());
        }

        private static List<KeyValuePair<string, string>> RequiredFiles(string uiJson = "{\"format\":\"obenseuer-rus/1\"}")
        {
            return new List<KeyValuePair<string, string>>
            {
                Entry("ObenseuerRUS.dll", "dll"),
                Entry("README.md", "readme"),
                Entry("translations/ui.json", uiJson),
                Entry("fonts/ObenseuerTypewriterCyrillic.ttf", "font"),
                Entry("fonts/COPYING-FreeFont.txt", "license"),
                Entry("fonts/FONT-NOTICE.txt", "notice"),
                Entry("fonts/make_cyrillic_font.py", "source")
            };
        }

        private static KeyValuePair<string, string> Entry(string path, string content)
        {
            return new KeyValuePair<string, string>(path, content);
        }

        private static byte[] Package(params KeyValuePair<string, string>[] files)
        {
            using (var stream = new MemoryStream())
            {
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (KeyValuePair<string, string> file in files)
                    using (var writer = new StreamWriter(zip.CreateEntry("BepInEx/plugins/ObenseuerRUS/" + file.Key).Open(), new UTF8Encoding(false)))
                        writer.Write(file.Value);
                }
                return stream.ToArray();
            }
        }

        private static string TestFolder()
        {
            return Path.Combine(AppContext.BaseDirectory, "updater-test-" + Guid.NewGuid().ToString("N"));
        }
    }
}
