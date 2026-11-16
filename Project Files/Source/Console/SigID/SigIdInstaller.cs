/*  SigIdInstaller.cs

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
//
//============================================================================================//
// SigID - download and install of the Artemis signal-identification database.                  //
//                                                                                              //
// The database is distributed by the Artemis project as a GitHub release asset ("vNN.tar",    //
// ~288 MB) containing data.sqlite plus the media folder.                                        //
//                                                                                              //
// Policy (SIGID_HANDOFF.md decisions 6 and 11):                                                //
//   - the database is never shipped with the application,                                       //
//   - the app only CHECKS for a newer release and notifies the user,                            //
//   - the ~288 MB download only ever starts when the user presses the button.                   //
//============================================================================================//

using System;
using System.Diagnostics;
using System.Formats.Tar;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Thetis
{
    /// <summary>Description of a published Artemis database release.</summary>
    public sealed class SigIdReleaseInfo
    {
        /// <summary>Database revision, e.g. 74 for the "v74" tag.</summary>
        public int Version;

        /// <summary>Raw git tag, e.g. "v74".</summary>
        public string TagName = string.Empty;

        /// <summary>Publication timestamp in ISO-8601 form.</summary>
        public string PublishedUtc = string.Empty;

        /// <summary>Name of the release asset, e.g. "v74.tar".</summary>
        public string AssetName = string.Empty;

        /// <summary>Direct download URL of the release asset.</summary>
        public string AssetUrl = string.Empty;

        /// <summary>Size of the release asset in bytes.</summary>
        public long AssetSize;

        public bool IsValid
        {
            get { return Version > 0 && !string.IsNullOrWhiteSpace(AssetUrl); }
        }
    }

    /// <summary>Progress information reported while the database is downloading.</summary>
    public struct SigIdDownloadProgress
    {
        public long BytesDownloaded;
        public long TotalBytes;
        public int Percentage;
        public double ElapsedSeconds;

        public double MegabytesPerSecond
        {
            get { return ElapsedSeconds > 0.0 ? (BytesDownloaded / 1048576.0) / ElapsedSeconds : 0.0; }
        }
    }

    /// <summary>Our own persisted install metadata (lives next to the database).</summary>
    public sealed class SigIdInstallInfo
    {
        public int Version;
        public string ReleaseDate = string.Empty;
        public string AssetName = string.Empty;
        public string AssetUrl = string.Empty;
        public long AssetSize;
        public string InstalledUtc = string.Empty;

        public string LastCheckedUtc = string.Empty;
        public int LatestVersion;
        public string LatestTag = string.Empty;
        public string LatestPublishedUtc = string.Empty;

        /// <summary>True when the last check found a newer release than the installed one.</summary>
        [JsonIgnore]
        public bool UpdateAvailable
        {
            get { return LatestVersion > 0 && LatestVersion > Version; }
        }

        /// <summary>True when the last check is older than the given age.</summary>
        public bool CheckIsStale(TimeSpan age)
        {
            DateTime checkedUtc;
            if (!DateTime.TryParse(LastCheckedUtc, out checkedUtc)) return true;
            return (DateTime.UtcNow - checkedUtc.ToUniversalTime()) > age;
        }
    }

    public static class SigIdInstaller
    {
        /// <summary>GitHub API endpoint for the newest Artemis database release.</summary>
        public const string RELEASES_API_URL =
            "https://api.github.com/repos/AresValley/Artemis-Database/releases/latest";

        /// <summary>Page shown to the user, linking to the community database releases.</summary>
        public const string RELEASES_PAGE_URL =
            "https://github.com/AresValley/Artemis-Database/releases";

        /// <summary>Repository home page.</summary>
        public const string REPOSITORY_URL =
            "https://github.com/AresValley/Artemis-Database";

        /// <summary>How long a release check stays valid before it is repeated.</summary>
        public static readonly TimeSpan CHECK_INTERVAL = TimeSpan.FromHours(24);

        /// <summary>How long a release check stays valid when the form is opened.</summary>
        public static readonly TimeSpan CHECK_INTERVAL_FORM = TimeSpan.FromHours(1);

        private const int DOWNLOAD_BUFFER = 128 * 1024;
        private const string USER_AGENT = "SDR-VST3-HL2-SigID";

        // keep ISO-8601 timestamps as the strings they are - Newtonsoft would otherwise
        // turn them into DateTime and re-serialise them in the local culture format
        private static readonly JsonSerializerSettings INFO_JSON_SETTINGS = new JsonSerializerSettings
        {
            DateParseHandling = DateParseHandling.None
        };

        /// <summary>
        /// Queries the GitHub API for the newest database release.  Returns null when the
        /// request fails - the caller keeps whatever it knew before.
        /// </summary>
        public static async Task<SigIdReleaseInfo> FetchLatestReleaseAsync(CancellationToken cancellationToken)
        {
            try
            {
                using (HttpClient client = CreateClient())
                using (HttpResponseMessage response = await client
                    .GetAsync(RELEASES_API_URL, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode) return null;

                    string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                    return ParseRelease(body);
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Parses the GitHub "latest release" JSON payload.</summary>
        public static SigIdReleaseInfo ParseRelease(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return null;

            JObject root;
            try
            {
                // DateParseHandling.None keeps the published_at timestamp as the raw
                // ISO-8601 string instead of turning it into a local-culture DateTime
                using (StringReader stringReader = new StringReader(json))
                using (JsonTextReader jsonReader = new JsonTextReader(stringReader))
                {
                    jsonReader.DateParseHandling = DateParseHandling.None;
                    root = JObject.Load(jsonReader);
                }
            }
            catch (JsonException)
            {
                return null;
            }

            SigIdReleaseInfo info = new SigIdReleaseInfo();
            info.TagName = root.Value<string>("tag_name") ?? string.Empty;
            info.PublishedUtc = root.Value<string>("published_at") ?? string.Empty;
            info.Version = ParseVersion(info.TagName);

            JArray assets = root["assets"] as JArray;
            if (assets != null)
            {
                foreach (JToken token in assets)
                {
                    string name = token.Value<string>("name");
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    if (!name.EndsWith(".tar", StringComparison.OrdinalIgnoreCase)) continue;

                    info.AssetName = name;
                    info.AssetUrl = token.Value<string>("browser_download_url") ?? string.Empty;
                    info.AssetSize = token.Value<long?>("size") ?? 0L;
                    break;
                }
            }

            return info.IsValid ? info : null;
        }

        /// <summary>Turns a tag such as "v74" into the revision number 74.</summary>
        public static int ParseVersion(string tagName)
        {
            if (string.IsNullOrWhiteSpace(tagName)) return 0;
            string digits = tagName.TrimStart('v', 'V');
            int value;
            return int.TryParse(digits, out value) ? value : 0;
        }

        /// <summary>
        /// Downloads a release asset to a temporary file.  Reports progress through
        /// <paramref name="onProgress"/> and honours <paramref name="cancellationToken"/>.
        /// Returns the path of the downloaded file.
        /// </summary>
        public static async Task<string> DownloadReleaseAsync(SigIdReleaseInfo release,
                                                              Action<SigIdDownloadProgress> onProgress,
                                                              CancellationToken cancellationToken)
        {
            if (release == null || !release.IsValid)
                throw new InvalidOperationException("No database release is available to download.");

            if (!SigIdPaths.EnsureRoot())
                throw new IOException("The SigId folder could not be created at " + SigIdPaths.Root + ".");

            string tempFile = Path.Combine(Path.GetTempPath(),
                "ArtemisDB-" + release.TagName + "-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tar");

            try
            {
                using (HttpClient client = CreateClient())
                using (HttpResponseMessage response = await client
                    .GetAsync(release.AssetUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false))
                {
                    if (!response.IsSuccessStatusCode)
                        throw new IOException("The database server answered " + (int)response.StatusCode + ".");

                    long total = response.Content.Headers.ContentLength
                                 ?? (release.AssetSize > 0 ? release.AssetSize : 0L);

                    Stopwatch clock = Stopwatch.StartNew();
                    long downloaded = 0;

                    using (Stream source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
                    using (FileStream target = new FileStream(tempFile, FileMode.Create, FileAccess.Write,
                                                             FileShare.None, DOWNLOAD_BUFFER, true))
                    {
                        byte[] buffer = new byte[DOWNLOAD_BUFFER];
                        int read;

                        while ((read = await source.ReadAsync(buffer, 0, buffer.Length, cancellationToken)
                                   .ConfigureAwait(false)) > 0)
                        {
                            await target.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
                            downloaded += read;

                            if (onProgress != null)
                            {
                                SigIdDownloadProgress progress = new SigIdDownloadProgress();
                                progress.BytesDownloaded = downloaded;
                                progress.TotalBytes = total;
                                progress.ElapsedSeconds = clock.Elapsed.TotalSeconds;
                                progress.Percentage = total > 0
                                    ? (int)Math.Min(100, downloaded * 100L / total)
                                    : 0;
                                onProgress(progress);
                            }
                        }

                        await target.FlushAsync(cancellationToken).ConfigureAwait(false);
                    }

                    if (total > 0 && downloaded != total)
                        throw new IOException("The database download was incomplete (" +
                                              downloaded + " of " + total + " bytes).");

                    return tempFile;
                }
            }
            catch
            {
                TryDelete(tempFile);
                throw;
            }
        }

        /// <summary>
        /// Extracts a downloaded archive and swaps it into place.  The previous installation is
        /// only removed once the new one has been extracted successfully.
        /// </summary>
        public static void Install(string archivePath, SigIdReleaseInfo release)
        {
            if (!File.Exists(archivePath))
                throw new FileNotFoundException("The downloaded database archive is missing.", archivePath);

            if (!SigIdPaths.EnsureRoot())
                throw new IOException("The SigId folder could not be created at " + SigIdPaths.Root + ".");

            string root = SigIdPaths.Root;
            string staging = root + ".new";

            try
            {
                TryDeleteDirectory(staging);
                Directory.CreateDirectory(staging);

                TarFile.ExtractToDirectory(archivePath, staging, true);

                string stagedRoot = FindStagedRoot(staging);
                string stagedDatabase = Path.Combine(stagedRoot, SigIdPaths.DATABASE_FILE);
                if (!File.Exists(stagedDatabase))
                    throw new IOException("The downloaded archive does not contain " + SigIdPaths.DATABASE_FILE + ".");

                // remove the old media folder first so a stale asset can never be mistaken
                // for part of the new installation
                TryDeleteDirectory(Path.Combine(root, SigIdPaths.MEDIA_FOLDER));

                string targetMedia = Path.Combine(root, SigIdPaths.MEDIA_FOLDER);
                string stagedMedia = Path.Combine(stagedRoot, SigIdPaths.MEDIA_FOLDER);
                if (Directory.Exists(stagedMedia))
                    Directory.Move(stagedMedia, targetMedia);
                else
                    Directory.CreateDirectory(targetMedia);

                string targetDatabase = Path.Combine(root, SigIdPaths.DATABASE_FILE);
                TryDelete(targetDatabase);
                File.Move(stagedDatabase, targetDatabase);

                SigIdInstallInfo info = LoadInstallInfo() ?? new SigIdInstallInfo();
                if (release != null)
                {
                    info.Version = release.Version;
                    info.AssetName = release.AssetName;
                    info.AssetUrl = release.AssetUrl;
                    info.AssetSize = release.AssetSize;
                    info.LatestVersion = release.Version;
                    info.LatestTag = release.TagName;
                    info.LatestPublishedUtc = release.PublishedUtc;
                }
                // the release date lives inside the database itself, so read it back from the
                // freshly installed file instead of extracting the archive a second time
                try
                {
                    SigIdDatabase installed = SigIdDatabase.Open();
                    if (installed != null && !string.IsNullOrWhiteSpace(installed.ReleaseDate))
                        info.ReleaseDate = installed.ReleaseDate;
                }
                catch (Exception)
                {
                    // the date is informational only
                }

                info.InstalledUtc = DateTime.UtcNow.ToString("o");
                SaveInstallInfo(info);

                // Measure the reference recordings in the background.  The index gives
                // every signal a repetition interval, a bandwidth and a symbol rate,
                // even the ones the wiki documents none of - and the matcher needs
                // that to tell the signals apart.
                StartIndexBuild();
            }
            finally
            {
                TryDeleteDirectory(staging);
            }
        }

        /// <summary>
        /// Builds the feature index in the background.  It decodes and analyses 489
        /// reference recordings, which takes a while, so it must not block the UI.
        /// </summary>
        public static void StartIndexBuild()
        {
            try
            {
                System.Threading.Tasks.Task.Run(() =>
                {
                    try
                    {
                        SigIdDatabase database = SigIdDatabase.Open();
                        if (database == null || !database.HasSignals) return;

                        SigIdFeatureIndex index = SigIdFeatureIndex.Build(database, SigIdPaths.MediaDirectory, null);
                        if (index.Count == 0) return;

                        index.Save();
                    }
                    catch (Exception)
                    {
                        // the index is a cache, never fatal
                    }
                });
            }
            catch (Exception)
            {
                // never let this break the install
            }
        }

        /// <summary>Reads our install metadata.  Returns null when it does not exist.</summary>
        public static SigIdInstallInfo LoadInstallInfo()
        {
            try
            {
                string path = SigIdPaths.InstallInfoFile;
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return null;
                return JsonConvert.DeserializeObject<SigIdInstallInfo>(json, INFO_JSON_SETTINGS);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Returns the recorded install metadata, reconciled with what is actually on disk.
        /// The database itself is the authority for the installed version - install.json can
        /// be missing (the database was copied in by hand) or stale.
        /// </summary>
        public static SigIdInstallInfo GetCurrentInstallInfo()
        {
            SigIdInstallInfo info = LoadInstallInfo() ?? new SigIdInstallInfo();

            SigIdDatabase database = SigIdDatabase.Open();
            if (database != null)
            {
                if (info.Version != database.Version)
                    info.Version = database.Version;
                if (!string.IsNullOrWhiteSpace(database.ReleaseDate))
                    info.ReleaseDate = database.ReleaseDate;
            }
            else
            {
                info.Version = 0;
            }

            return info;
        }

        /// <summary>Writes our install metadata.</summary>
        public static void SaveInstallInfo(SigIdInstallInfo info)
        {
            if (info == null) return;
            try
            {
                if (!SigIdPaths.EnsureRoot()) return;
                File.WriteAllText(SigIdPaths.InstallInfoFile,
                    JsonConvert.SerializeObject(info, Formatting.Indented, INFO_JSON_SETTINGS));
            }
            catch (Exception)
            {
                // metadata is a convenience, never fatal
            }
        }

        /// <summary>Stores the outcome of a release check so the next start can skip it.</summary>
        public static void RecordCheck(SigIdReleaseInfo release)
        {
            if (release == null) return;
            SigIdInstallInfo info = GetCurrentInstallInfo();
            info.LatestVersion = release.Version;
            info.LatestTag = release.TagName;
            info.LatestPublishedUtc = release.PublishedUtc;
            info.LastCheckedUtc = DateTime.UtcNow.ToString("o");
            SaveInstallInfo(info);
        }

        /// <summary>Reads the installed database revision straight out of the SQLite file.</summary>
        public static int GetInstalledVersion()
        {
            SigIdDatabase db = SigIdDatabase.Open();
            if (db == null) return 0;
            return db.Version;
        }

        /// <summary>Deletes a downloaded archive.</summary>
        public static void DeleteArchive(string path)
        {
            TryDelete(path);
        }

        private static HttpClient CreateClient()
        {
            HttpClient client = new HttpClient();
            client.Timeout = TimeSpan.FromMinutes(30);
            client.DefaultRequestHeaders.Add("User-Agent", USER_AGENT);
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
            return client;
        }

        /// <summary>
        /// The archive may or may not contain a single top level folder.  Locate the folder
        /// that actually holds data.sqlite.
        /// </summary>
        private static string FindStagedRoot(string staging)
        {
            if (File.Exists(Path.Combine(staging, SigIdPaths.DATABASE_FILE))) return staging;

            foreach (string directory in Directory.GetDirectories(staging))
            {
                if (File.Exists(Path.Combine(directory, SigIdPaths.DATABASE_FILE))) return directory;
            }

            return staging;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { }
        }

        private static void TryDeleteDirectory(string path)
        {
            try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { }
        }
    }
}
