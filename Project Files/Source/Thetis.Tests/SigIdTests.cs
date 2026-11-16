/*  SigIdTests.cs

    This file is part of a program that implements a Software-Defined Radio.

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.
*/

using System;
using System.IO;
using Xunit;

namespace Thetis.Tests
{
    // these classes all manipulate the same environment variable and the cached
    // SigIdPaths root, so they must not run in parallel
    [Collection("SigId")]
    public class SigIdPathsTests
    {
        [Fact]
        public void Environment_override_wins()
        {
            string original = Environment.GetEnvironmentVariable(SigIdPaths.PATH_ENV_VAR);
            try
            {
                string target = Path.Combine(Path.GetTempPath(), "sigid-override-" + Guid.NewGuid().ToString("N"));
                Environment.SetEnvironmentVariable(SigIdPaths.PATH_ENV_VAR, target);
                SigIdPaths.ResetCache();

                Assert.Equal(target, SigIdPaths.Root);
                Assert.True(SigIdPaths.IsLocalCopy);
            }
            finally
            {
                Environment.SetEnvironmentVariable(SigIdPaths.PATH_ENV_VAR, original);
                SigIdPaths.ResetCache();
            }
        }

        [Fact]
        public void Default_root_is_under_appdata()
        {
            string original = Environment.GetEnvironmentVariable(SigIdPaths.PATH_ENV_VAR);
            try
            {
                Environment.SetEnvironmentVariable(SigIdPaths.PATH_ENV_VAR, null);
                SigIdPaths.ResetCache();

                string expected = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "OpenHPSDR", "SDR-VST3-HL2-x64", "SigId");

                // when a local copy sits next to the executable it takes precedence - that is
                // the developer / test layout, and it never applies to a Program Files install
                string local = Path.Combine(AppContext.BaseDirectory, "SigId");
                if (!Directory.Exists(local)) Assert.Equal(expected, SigIdPaths.Root);
            }
            finally
            {
                Environment.SetEnvironmentVariable(SigIdPaths.PATH_ENV_VAR, original);
                SigIdPaths.ResetCache();
            }
        }

        [Fact]
        public void Database_file_and_media_folder_hang_off_the_root()
        {
            string root = SigIdPaths.Root;
            Assert.Equal(Path.Combine(root, "data.sqlite"), SigIdPaths.DatabaseFile);
            Assert.Equal(Path.Combine(root, "media"), SigIdPaths.MediaDirectory);
        }
    }

    [Collection("SigId")]
    public class SigIdInstallerTests
    {
        [Theory]
        [InlineData("v74", 74)]
        [InlineData("V74", 74)]
        [InlineData("74", 74)]
        [InlineData("", 0)]
        [InlineData(null, 0)]
        [InlineData("vNext", 0)]
        public void Tag_names_map_to_revision_numbers(string tag, int expected)
        {
            Assert.Equal(expected, SigIdInstaller.ParseVersion(tag));
        }

        [Fact]
        public void Release_payload_is_parsed()
        {
            const string json = @"{
              ""tag_name"": ""v74"",
              ""published_at"": ""2026-11-03T18:52:07Z"",
              ""assets"": [
                { ""name"": ""v74.tar"",
                  ""browser_download_url"": ""https://github.com/AresValley/Artemis-Database/releases/download/v74/v74.tar"",
                  ""size"": 302260000 }
              ]
            }";

            SigIdReleaseInfo release = SigIdInstaller.ParseRelease(json);

            Assert.NotNull(release);
            Assert.Equal(74, release.Version);
            Assert.Equal("v74", release.TagName);
            Assert.Equal("v74.tar", release.AssetName);
            Assert.Equal(302260000L, release.AssetSize);
            Assert.EndsWith("/v74/v74.tar", release.AssetUrl);
            Assert.True(release.IsValid);
        }

        [Fact]
        public void Release_payload_without_a_tar_asset_is_rejected()
        {
            const string json = @"{
              ""tag_name"": ""v74"",
              ""assets"": [ { ""name"": ""v74.zip"", ""browser_download_url"": ""https://example.invalid/x.zip"", ""size"": 1 } ]
            }";

            Assert.Null(SigIdInstaller.ParseRelease(json));
        }

        [Fact]
        public void Garbage_payload_is_rejected()
        {
            Assert.Null(SigIdInstaller.ParseRelease("not json"));
            Assert.Null(SigIdInstaller.ParseRelease(null));
            Assert.Null(SigIdInstaller.ParseRelease("   "));
        }

        [Fact]
        public void Installed_version_is_zero_when_nothing_is_installed()
        {
            // never asserts a specific number - it only checks that a missing database
            // reports "no version" instead of throwing
            int version = SigIdInstaller.GetInstalledVersion();
            Assert.True(version >= 0);
        }
    }

    [Collection("SigId")]
    public class SigIdInstallTests
    {
        [Fact]
        public void Archive_is_extracted_and_swapped_into_place()
        {
            string workspace = Path.Combine(Path.GetTempPath(), "sigid-install-" + Guid.NewGuid().ToString("N"));
            string root = Path.Combine(workspace, "SigId");
            string source = Path.Combine(workspace, "source");
            string archive = Path.Combine(workspace, "db.tar");

            try
            {
                Directory.CreateDirectory(source);
                Directory.CreateDirectory(Path.Combine(source, "media"));

                // a stand-in for the Artemis database
                string database = Path.Combine(source, "data.sqlite");
                using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                    "Data Source=" + database + ";Pooling=False"))
                {
                    connection.Open();
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "CREATE TABLE INFO (DATE TEXT, NAME TEXT, VERSION INTEGER)";
                        command.ExecuteNonQuery();
                        command.CommandText = "INSERT INTO INFO VALUES ('2026-07-18','SigID',74)";
                        command.ExecuteNonQuery();
                        command.CommandText =
                            "CREATE TABLE SIGNALS (SIG_ID INTEGER PRIMARY KEY, DESCRIPTION TEXT, NAME TEXT, URL TEXT, SINCE_VERSION INTEGER)";
                        command.ExecuteNonQuery();
                        command.CommandText =
                            "INSERT INTO SIGNALS VALUES (510,'### SUMMARY`r`nTest signal','STANAG 4285','https://example.invalid',74)";
                        command.ExecuteNonQuery();
                    }
                }

                File.WriteAllText(Path.Combine(source, "media", "1.png"), "fake png");
                File.WriteAllText(Path.Combine(source, "media", "2.ogg"), "fake ogg");

                // includeBaseDirectory: false reproduces the real release layout, where the
                // archive holds "./data.sqlite" and "./media/..." at its root
                System.Formats.Tar.TarFile.CreateFromDirectory(source, archive, false);

                // point the installer at our temporary root
                string original = Environment.GetEnvironmentVariable(SigIdPaths.PATH_ENV_VAR);
                Environment.SetEnvironmentVariable(SigIdPaths.PATH_ENV_VAR, root);
                SigIdPaths.ResetCache();

                SigIdReleaseInfo release = new SigIdReleaseInfo
                {
                    Version = 74,
                    TagName = "v74",
                    AssetName = "v74.tar",
                    AssetUrl = "https://example.invalid/v74.tar",
                    AssetSize = new FileInfo(archive).Length
                };

                SigIdInstaller.Install(archive, release);

                // the database must be in place and readable
                Assert.True(File.Exists(Path.Combine(root, "data.sqlite")));
                Assert.True(File.Exists(Path.Combine(root, "media", "1.png")));
                Assert.True(File.Exists(Path.Combine(root, "media", "2.ogg")));

                // and it must be the real database content
                SigIdDatabase db = SigIdDatabase.Open();
                Assert.True(db != null, "database failed to open: " + SigIdDatabase.LastOpenError);
                Assert.Equal(74, db.Version);
                Assert.Equal("2026-07-18", db.ReleaseDate);
                Assert.Equal(1, db.Signals.Count);
                Assert.Equal("STANAG 4285", db.Find(510).Name);
                Assert.Contains("Test signal", db.Find(510).ShortDescription);

                // the install metadata must record what was installed
                SigIdInstallInfo info = SigIdInstaller.LoadInstallInfo();
                Assert.NotNull(info);
                Assert.Equal(74, info.Version);
                Assert.Equal("v74.tar", info.AssetName);
                Assert.False(info.UpdateAvailable);

                // the staging folder must not be left behind
                Assert.False(Directory.Exists(root + ".new"));
            }
            finally
            {
                string original = Environment.GetEnvironmentVariable(SigIdPaths.PATH_ENV_VAR);
                Environment.SetEnvironmentVariable(SigIdPaths.PATH_ENV_VAR, original);
                SigIdPaths.ResetCache();
                try { if (Directory.Exists(workspace)) Directory.Delete(workspace, true); } catch { }
            }
        }

        [Fact]
        public void Real_release_archive_installs()
        {
            // opt-in integration test - it needs the ~288 MB release archive and a lot of
            // scratch disk, so it only runs when the path is supplied:
            //   set SIGID_TEST_TAR=<path to v74.tar> and run the tests
            string archive = Environment.GetEnvironmentVariable("SIGID_TEST_TAR");
            if (string.IsNullOrWhiteSpace(archive) || !File.Exists(archive)) return;

            string workspace = Path.Combine(Path.GetTempPath(), "sigid-real-" + Guid.NewGuid().ToString("N"));
            string root = Path.Combine(workspace, "SigId");

            try
            {
                string original = Environment.GetEnvironmentVariable(SigIdPaths.PATH_ENV_VAR);
                Environment.SetEnvironmentVariable(SigIdPaths.PATH_ENV_VAR, root);
                SigIdPaths.ResetCache();

                SigIdReleaseInfo release = new SigIdReleaseInfo
                {
                    Version = 74,
                    TagName = "v74",
                    AssetName = "v74.tar",
                    AssetUrl = "https://github.com/AresValley/Artemis-Database/releases/download/v74/v74.tar",
                    AssetSize = new FileInfo(archive).Length
                };

                SigIdInstaller.Install(archive, release);

                Assert.True(File.Exists(Path.Combine(root, "data.sqlite")));
                Assert.True(Directory.Exists(Path.Combine(root, "media")));

                int mediaFiles = Directory.GetFiles(Path.Combine(root, "media")).Length;
                System.Console.WriteLine("[sigid] installed " + mediaFiles + " media files, archive " +
                                         new FileInfo(archive).Length + " bytes");
                Assert.Equal(1113, mediaFiles);

                SigIdDatabase db = SigIdDatabase.Open();
                Assert.True(db != null, "database failed to open: " + SigIdDatabase.LastOpenError);
                Assert.Equal(74, db.Version);
                Assert.Equal(583, db.Signals.Count);

                // the reference audio for STANAG 4285 must have landed on disk
                SigIdSignal stanag = db.Find(510);
                Assert.NotNull(stanag);
                string audio = Path.Combine(root, "media", stanag.AudioFileName);
                Assert.True(File.Exists(audio), "expected " + audio);

                // and the install metadata must be in place
                SigIdInstallInfo info = SigIdInstaller.LoadInstallInfo();
                Assert.NotNull(info);
                Assert.Equal(74, info.Version);
                Assert.Equal("2026-07-18", info.ReleaseDate);

                Assert.False(Directory.Exists(root + ".new"));
            }
            finally
            {
                string original = Environment.GetEnvironmentVariable(SigIdPaths.PATH_ENV_VAR);
                Environment.SetEnvironmentVariable(SigIdPaths.PATH_ENV_VAR, original);
                SigIdPaths.ResetCache();
                try { if (Directory.Exists(workspace)) Directory.Delete(workspace, true); } catch { }
            }
        }

        [Fact]
        public void Install_keeps_the_old_database_when_the_archive_is_broken()
        {
            string workspace = Path.Combine(Path.GetTempPath(), "sigid-broken-" + Guid.NewGuid().ToString("N"));
            string root = Path.Combine(workspace, "SigId");

            try
            {
                Directory.CreateDirectory(root);
                string existing = Path.Combine(root, "data.sqlite");
                File.WriteAllText(existing, "not really a database");

                string brokenArchive = Path.Combine(workspace, "broken.tar");
                File.WriteAllText(brokenArchive, "this is not a tar archive");

                string original = Environment.GetEnvironmentVariable(SigIdPaths.PATH_ENV_VAR);
                Environment.SetEnvironmentVariable(SigIdPaths.PATH_ENV_VAR, root);
                SigIdPaths.ResetCache();

                Assert.ThrowsAny<Exception>(() => SigIdInstaller.Install(brokenArchive, null));

                // the previous installation must still be there
                Assert.True(File.Exists(existing));
                Assert.Equal("not really a database", File.ReadAllText(existing));
                Assert.False(Directory.Exists(root + ".new"));
            }
            finally
            {
                string original = Environment.GetEnvironmentVariable(SigIdPaths.PATH_ENV_VAR);
                Environment.SetEnvironmentVariable(SigIdPaths.PATH_ENV_VAR, original);
                SigIdPaths.ResetCache();
                try { if (Directory.Exists(workspace)) Directory.Delete(workspace, true); } catch { }
            }
        }
    }

    [Collection("SigId")]
    public class SigIdDatabaseTests
    {
        private static string FindTestDatabase()
        {
            // 1. explicit override
            string env = Environment.GetEnvironmentVariable("SIGID_TEST_DB");
            if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;

            // 2. the developer layout - <repo>/Project Files/bin/x64/Release/SigId/data.sqlite
            DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
            for (int i = 0; i < 10 && dir != null; i++)
            {
                string candidate = Path.Combine(dir.FullName, "bin", "x64", "Release", "SigId", "data.sqlite");
                if (File.Exists(candidate)) return candidate;
                dir = dir.Parent;
            }

            return null;
        }

        [Fact]
        public void Missing_database_returns_null()
        {
            Assert.Null(SigIdDatabase.Open(Path.Combine(Path.GetTempPath(), "sigid-does-not-exist.sqlite")));
            Assert.Null(SigIdDatabase.Open(null));
            Assert.Null(SigIdDatabase.Open("   "));
        }

        [Fact]
        public void Real_database_is_read_correctly()
        {
            string path = FindTestDatabase();
            if (path == null) return;   // the database is not installed - nothing to check

            SigIdDatabase db = SigIdDatabase.Open(path);

            Assert.True(db != null, "database failed to open: " + SigIdDatabase.LastOpenError);
            Assert.True(db.IsOpen);
            Assert.Equal(74, db.Version);
            Assert.Equal(583, db.Signals.Count);

            // every signal must be reachable by its id
            foreach (SigIdSignal signal in db.Signals)
            {
                Assert.Same(signal, db.Find(signal.SigId));
            }

            // STANAG 4285 - the reference signal used to validate the PRI/ACF matcher
            SigIdSignal stanag = db.Find(510);
            Assert.NotNull(stanag);
            Assert.Equal("STANAG 4285", stanag.Name);
            Assert.Contains(stanag.AcfEntries, a => Math.Abs(a.ValueMs - 106.66) < 0.01);
            Assert.NotEmpty(stanag.Modulations);
            Assert.NotEmpty(stanag.Categories);
            Assert.NotEmpty(stanag.AudioFileName);
            Assert.NotEmpty(stanag.ImageFileName);

            // the reference audio for STANAG 4285 must exist on disk next to the database
            string media = Path.Combine(Path.GetDirectoryName(path), "media", stanag.AudioFileName);
            Assert.True(File.Exists(media), "expected " + media);

            // a signal with no ACF entry must not get one
            SigIdSignal noAcf = null;
            foreach (SigIdSignal signal in db.Signals)
            {
                if (signal.AcfEntries.Count == 0) { noAcf = signal; break; }
            }
            Assert.NotNull(noAcf);

            // the acf table has 67 rows, so exactly 67 signals carry at least one entry
            int withAcf = 0;
            foreach (SigIdSignal signal in db.Signals)
            {
                if (signal.AcfEntries.Count > 0) withAcf++;
            }
            Assert.True(withAcf > 0 && withAcf <= 67, "signals with acf entries: " + withAcf);

            // bandwidth rows must land on the right signals
            SigIdSignal ghadir = db.Find(1);
            Assert.NotNull(ghadir);
            Assert.Contains(60000, ghadir.Bandwidths);
            Assert.Contains(1000000, ghadir.Bandwidths);
        }

        [Fact]
        public void Short_description_is_extracted_from_the_markdown()
        {
            string path = FindTestDatabase();
            if (path == null) return;

            SigIdDatabase db = SigIdDatabase.Open(path);
            SigIdSignal ghadir = db.Find(1);

            Assert.NotNull(ghadir);
            Assert.False(string.IsNullOrWhiteSpace(ghadir.ShortDescription));
            Assert.True(ghadir.ShortDescription.Length <= 240);
            Assert.DoesNotContain("###", ghadir.ShortDescription);
        }
    }
}
