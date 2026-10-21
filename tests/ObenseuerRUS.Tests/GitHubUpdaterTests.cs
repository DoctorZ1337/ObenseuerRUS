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
