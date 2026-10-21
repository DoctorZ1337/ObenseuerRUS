using System;
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
            var release = GitHubUpdater.ParseLatestRelease(ReleaseJson("v0.4.1", "ObenseuerRUS-0.4.1.zip"), new Version(0, 4, 0));

            Assert.Equal(new Version(0, 4, 1), release.Version);
            Assert.Equal("https://github.com/DoctorZ1337/ObenseuerRUS/releases/download/v0.4.1/ObenseuerRUS-0.4.1.zip", release.AssetUrl);
            Assert.Equal(123, release.Size);
        }

        [Fact]
        public void IgnoresCurrentVersionAndReportsMissingAsset()
        {
            Assert.Null(GitHubUpdater.ParseLatestRelease(ReleaseJson("v0.4.0", "ObenseuerRUS-0.4.0.zip"), new Version(0, 4, 0)));

            var release = GitHubUpdater.ParseLatestRelease(ReleaseJson("v0.4.1", "source.zip"), new Version(0, 4, 0));
            Assert.Null(release.AssetUrl);
        }

        [Fact]
        public void RemembersTheTranslationVersionAlreadyApplied()
        {
            string folder = TestFolder();
            Directory.CreateDirectory(folder);
            try
            {
                File.WriteAllText(Path.Combine(folder, ".github-update-version"), "0.4.1", new UTF8Encoding(false));
                Assert.Equal(new Version(0, 4, 1), GitHubUpdater.GetEffectiveVersion(folder, "0.4.0"));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void AcceptsOnlyValidTranslationFilesFromPackage()
        {
            var files = GitHubUpdater.ReadTranslationFiles(Package("translations/ui.json", "{\"format\":\"obenseuer-rus/1\"}"));

            Assert.Single(files);
            Assert.Contains("ui.json", files.Keys);
        }

        [Fact]
        public void InstallsUpdatedFilesAndLeavesUnrelatedFilesAlone()
        {
            string folder = TestFolder();
            string translations = Path.Combine(folder, "translations");
            Directory.CreateDirectory(translations);
            File.WriteAllText(Path.Combine(translations, "ui.json"), "old", new UTF8Encoding(false));
            File.WriteAllText(Path.Combine(translations, "custom.json"), "keep", new UTF8Encoding(false));
            try
            {
                GitHubUpdater.InstallTranslations(
                    new System.Collections.Generic.Dictionary<string, byte[]> { ["ui.json"] = Encoding.UTF8.GetBytes("new") }, translations);

                Assert.Equal("new", File.ReadAllText(Path.Combine(translations, "ui.json")));
                Assert.Equal("keep", File.ReadAllText(Path.Combine(translations, "custom.json")));
                Assert.Empty(Directory.GetFiles(translations, "*.tmp"));
            }
            finally
            {
                Directory.Delete(folder, recursive: true);
            }
        }

        [Fact]
        public void RejectsTraversalAndUnknownTranslationFormat()
        {
            Assert.Throws<InvalidDataException>(() => GitHubUpdater.ReadTranslationFiles(Package("translations/../escape.json", "{}")));
            Assert.Throws<InvalidDataException>(() => GitHubUpdater.ReadTranslationFiles(Package("translations/ui.json", "{\"format\":\"other\"}")));
        }

        private static string ReleaseJson(string tag, string assetName)
        {
            return "{\"tag_name\":\"" + tag + "\",\"draft\":false,\"prerelease\":false,\"html_url\":\"https://github.com/DoctorZ1337/ObenseuerRUS/releases/tag/" + tag + "\",\"assets\":[{\"name\":\"" + assetName + "\",\"browser_download_url\":\"https://github.com/DoctorZ1337/ObenseuerRUS/releases/download/" + tag + "/" + assetName + "\",\"digest\":\"sha256:" + new string('a', 64) + "\",\"size\":123}]}";
        }

        private static byte[] Package(string path, string json)
        {
            using (var stream = new MemoryStream())
            {
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
                using (var writer = new StreamWriter(zip.CreateEntry("BepInEx/plugins/ObenseuerRUS/" + path).Open(), new UTF8Encoding(false)))
                    writer.Write(json);
                return stream.ToArray();
            }
        }

        private static string TestFolder()
        {
            return Path.Combine(AppContext.BaseDirectory, "updater-test-" + Guid.NewGuid().ToString("N"));
        }
    }
}
