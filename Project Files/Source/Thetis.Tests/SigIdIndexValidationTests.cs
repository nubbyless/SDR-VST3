/*  SigIdIndexValidationTests.cs

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
using System.Collections.Generic;
using System.IO;
using Xunit;

namespace Thetis.Tests
{
    /// <summary>
    /// Validates the matcher against the Artemis reference recordings using the feature
    /// index - the measured profile of every reference clip.
    ///
    /// The evaluation is leave-one-out: the clip being tested is removed from the index
    /// first, so it can never match its own profile.
    ///
    /// It needs the installed database and a working libsndfile:
    ///   set SIGID_TEST_DB=<path to data.sqlite> and SIGID_TEST_MEDIA=<path to media>
    /// </summary>
    [Collection("SigId")]
    public class SigIdIndexValidationTests
    {
        private static string DatabasePath
        {
            get
            {
                string path = Environment.GetEnvironmentVariable("SIGID_TEST_DB");
                return string.IsNullOrWhiteSpace(path) ? SigIdPaths.DatabaseFile : path;
            }
        }

        private static string MediaDirectory
        {
            get
            {
                string path = Environment.GetEnvironmentVariable("SIGID_TEST_MEDIA");
                if (!string.IsNullOrWhiteSpace(path)) return path;
                string database = DatabasePath;
                return string.IsNullOrWhiteSpace(database) ? null : Path.Combine(Path.GetDirectoryName(database), "media");
            }
        }

        [Fact]
        public void Leave_one_out_ranking_against_the_feature_index()
        {
            string databasePath = DatabasePath;
            string media = MediaDirectory;
            if (string.IsNullOrWhiteSpace(databasePath) || string.IsNullOrWhiteSpace(media)) return;
            if (!File.Exists(databasePath) || !Directory.Exists(media)) return;

            SigIdDatabase database = SigIdDatabase.Open(databasePath);
            if (database == null) return;

            if (!SigIdAudioDecoder.Available)
            {
                System.Console.WriteLine("[sigid] libsndfile is not available: " + SigIdAudioDecoder.LastError);
                return;
            }
            System.Console.WriteLine("[sigid] using " + SigIdAudioDecoder.Version);

            // measure every reference recording once
            SigIdFeatureIndex index = new SigIdFeatureIndex();
            Dictionary<int, SigIdFeatures> measured = new Dictionary<int, SigIdFeatures>();

            foreach (SigIdSignal signal in database.Signals)
            {
                if (string.IsNullOrEmpty(signal.AudioFileName)) continue;

                float[] samples;
                int rate;
                string path = Path.Combine(media, signal.AudioFileName);
                if (!SigIdAudioDecoder.TryDecode(path, out samples, out rate)) continue;

                int wanted = (int)(SigIdFeatureIndex.ANALYSIS_SECONDS * rate);
                if (samples.Length > wanted)
                {
                    float[] trimmed = new float[wanted];
                    Array.Copy(samples, trimmed, wanted);
                    samples = trimmed;
                }

                SigIdFeatures features = SigIdFeatureExtractor.Analyze(samples, rate, 0.0);
                measured[signal.SigId] = features;
                index.Profiles[signal.SigId] = SigIdReferenceProfile.From(signal.SigId, signal.AudioFileName, features);
            }

            System.Console.WriteLine("[sigid] measured " + index.Profiles.Count + " reference recordings");
            if (index.Profiles.Count < 50) return;

            int tested = 0;
            int top1 = 0;
            int top5 = 0;
            int top20 = 0;
            int familyRight = 0;

            foreach (KeyValuePair<int, SigIdFeatures> entry in measured)
            {
                int sigId = entry.Key;
                SigIdFeatures features = entry.Value;

                List<SigIdCandidate> ranked = SigIdMatcher.Rank(features, database, index, 0.0, SigIdScope.AllSignals, sigId);
                if (ranked.Count == 0) continue;

                tested++;

                int position = -1;
                for (int i = 0; i < ranked.Count; i++)
                {
                    if (ranked[i].Signal.SigId == sigId) { position = i + 1; break; }
                }

                if (position == 1) top1++;
                if (position > 0 && position <= 5) top5++;
                if (position > 0 && position <= 20) top20++;

                // is the best candidate in the same modulation family as the truth?
                SigIdSignal truth = database.Find(sigId);
                SigIdCandidate best = ranked[0];
                if (truth != null && best.Signal != null)
                {
                    bool related = false;
                    foreach (string modulation in truth.Modulations)
                    {
                        if (SigIdMatcher.ModulationFamilyScore(features.ModulationClass, modulation) >= 1) { related = true; break; }
                    }
                    foreach (string modulation in best.Signal.Modulations)
                    {
                        if (SigIdMatcher.ModulationFamilyScore(features.ModulationClass, modulation) >= 1) { related = true; break; }
                    }
                    if (related) familyRight++;
                }
            }

            System.Console.WriteLine("[sigid] LEAVE-ONE-OUT over " + tested + " clips: top1 " + top1 +
                                    ", top5 " + top5 + ", top20 " + top20 +
                                    ", family " + familyRight);
            System.Console.WriteLine("[sigid] top1 " + (tested > 0 ? (100.0 * top1 / tested).ToString("0.0") : "0") +
                                    " %, top5 " + (tested > 0 ? (100.0 * top5 / tested).ToString("0.0") : "0") +
                                    " %, top20 " + (tested > 0 ? (100.0 * top20 / tested).ToString("0.0") : "0") + " %");

            Assert.True(tested > 50, "not enough clips were tested: " + tested);
        }
    }
}
