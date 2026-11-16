/*  SigIdFeatureTests.cs

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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Thetis.Tests
{
    /// <summary>
    /// Validates the feature extractor against the Artemis database's own reference
    /// recordings.  Each clip carries a documented pulse repetition interval in the
    /// database's acf column, so the extractor has to reproduce that value from the
    /// audio alone.  This is the ground truth for the whole matcher.
    ///
    /// The test is opt-in because it needs the clips converted to WAV first:
    ///   set SIGID_TEST_WAVS=&lt;folder with index.json&gt; and run the tests
    /// </summary>
    [Collection("SigId")]
    public class SigIdFeatureTests
    {
        private sealed class ClipEntry
        {
            public int sig_id;
            public string wav;
            public double[] acf = new double[0];
            public List<double> acfValues = new List<double>();
            public double seconds;
        }

        private static List<ClipEntry> LoadIndex()
        {
            string folder = Environment.GetEnvironmentVariable("SIGID_TEST_WAVS");
            if (string.IsNullOrWhiteSpace(folder)) return null;

            string index = Path.Combine(folder, "index.json");
            if (!File.Exists(index)) return null;

            try
            {
                List<ClipEntry> result = new List<ClipEntry>();
                JArray array = JArray.Parse(File.ReadAllText(index));
                foreach (JToken token in array)
                {
                    ClipEntry entry = new ClipEntry();
                    entry.sig_id = token.Value<int>("sig_id");
                    entry.wav = token.Value<string>("wav");
                    entry.seconds = token.Value<double>("seconds");
                    JArray acf = token["acf"] as JArray;
                    if (acf != null)
                        foreach (JToken value in acf) entry.acfValues.Add(value.Value<double>());
                    entry.acf = entry.acfValues.ToArray();
                    result.Add(entry);
                }
                return result;
            }
            catch (Exception)
            {
                return null;
            }
        }

        [Fact]
        public void Reference_clips_reproduce_their_documented_pri()
        {
            List<ClipEntry> clips = LoadIndex();
            if (clips == null) return;   // the clips are not available - nothing to check

            int tested = 0;
            int matched = 0;
            List<string> failures = new List<string>();

            foreach (ClipEntry clip in clips)
            {
                if (clip.acf.Length == 0) continue;
                if (!File.Exists(clip.wav)) continue;

                // a zero period means "variable" in the database, there is nothing to match
                List<double> expected = new List<double>();
                foreach (double value in clip.acf) if (value > 0.0) expected.Add(value);
                if (expected.Count == 0) continue;

                float[] samples;
                int rate;
                if (!TryReadMonoWav(clip.wav, out samples, out rate)) continue;

                tested++;

                SigIdFeatures features = SigIdFeatureExtractor.Analyze(samples, rate, 0.0);

                bool hit = false;
                foreach (double expectedValue in expected)
                {
                    foreach (SigIdPeriod period in features.Periods)
                    {
                        double error = Math.Abs(period.PeriodMs - expectedValue);
                        if (error <= Math.Max(3.0, expectedValue * 0.08))
                        {
                            hit = true;
                            break;
                        }
                    }
                    if (hit) break;
                }

                if (hit) matched++;
                else
                {
                    string measured = features.Periods.Count > 0 ? features.Periods[0].ToString() : "none";
                    failures.Add("sig " + clip.sig_id + ": expected " +
                                 string.Join("/", Array.ConvertAll(expected.ToArray(), a => a.ToString("0.##"))) +
                                 " ms, measured " + measured +
                                 " [" + string.Join(" ", Array.ConvertAll(features.Periods.ToArray(),
                                     p => p.PeriodMs.ToString("0.##") + "@" + p.Correlation.ToString("0.00"))) + "]");
                }
            }

            Assert.True(tested > 20, "not enough reference clips were tested: " + tested);

            double ratio = (double)matched / tested;
            System.Console.WriteLine("[sigid] PRI reproduction: " + matched + "/" + tested +
                              " (" + (ratio * 100.0).ToString("0.0") + " %)");
            foreach (string failure in failures) System.Console.WriteLine("   miss " + failure);

            // the extractor has to reproduce the documented PRI for the large majority
            Assert.True(ratio > 0.70,
                "PRI reproduction was only " + (ratio * 100.0).ToString("0.0") + " % (" +
                matched + "/" + tested + ")");
        }

        [Fact]
        public void Features_of_a_pure_tone_are_sane()
        {
            const int rate = 24000;
            const double toneHz = 1000.0;

            float[] samples = new float[rate];
            for (int i = 0; i < samples.Length; i++)
                samples[i] = (float)(0.5 * Math.Sin(2.0 * Math.PI * toneHz * i / rate));

            SigIdFeatures features = SigIdFeatureExtractor.Analyze(samples, rate, 0.0);

            Assert.True(Math.Abs(features.CarrierOffsetHz - toneHz) < 30.0,
                "carrier offset was " + features.CarrierOffsetHz.ToString("0.0") + " Hz");

            // a continuous tone has no envelope structure, so the occupied bandwidth
            // must be narrow
            Assert.True(features.OccupiedBandwidthHz < 200.0,
                "occupied bandwidth was " + features.OccupiedBandwidthHz.ToString("0.0") + " Hz");

            Assert.True(features.Rms > 0.3 && features.Rms < 0.4,
                "rms was " + features.Rms.ToString("0.000"));
        }

        [Fact]
        public void Features_of_a_pulse_train_report_its_period()
        {
            const int rate = 24000;
            const double periodMs = 100.0;      // 10 Hz repetition
            int periodSamples = (int)(rate * periodMs / 1000.0);

            float[] samples = new float[rate];
            for (int i = 0; i < samples.Length; i++)
            {
                int phase = i % periodSamples;
                // a short burst of tone at the start of every period
                double value = phase < (rate * 0.002) ? 0.5 * Math.Sin(2.0 * Math.PI * 1000.0 * phase / rate) : 0.0;
                samples[i] = (float)value;
            }

            SigIdFeatures features = SigIdFeatureExtractor.Analyze(samples, rate, 0.0);

            Assert.True(features.IsPeriodic,
                "no periodicity found in a 100 ms pulse train");
            Assert.True(Math.Abs(features.DominantPeriodMs - periodMs) < 8.0,
                "measured period was " + features.DominantPeriodMs.ToString("0.0") + " ms");
        }

        [Fact]
        public void The_matcher_ranks_the_right_signal_first()
        {
            List<ClipEntry> clips = LoadIndex();
            if (clips == null) return;

            string databasePath = Environment.GetEnvironmentVariable("SIGID_TEST_DB");
            if (string.IsNullOrWhiteSpace(databasePath)) databasePath = SigIdPaths.DatabaseFile;
            SigIdDatabase database = SigIdDatabase.Open(databasePath);
            if (database == null) return;   // no database installed - nothing to check

            int tested = 0;
            int top1 = 0;
            int top5 = 0;
            int top20 = 0;
            List<string> misses = new List<string>();
            int priDumps = 0;

            foreach (ClipEntry clip in clips)
            {
                if (!File.Exists(clip.wav)) continue;

                float[] samples;
                int rate;
                if (!TryReadMonoWav(clip.wav, out samples, out rate)) continue;

                SigIdFeatures features = SigIdFeatureExtractor.Analyze(samples, rate, 0.0);
                List<SigIdCandidate> ranked = SigIdMatcher.Rank(features, database, 0.0);
                if (ranked.Count == 0) continue;

                tested++;

                int position = -1;
                for (int i = 0; i < ranked.Count; i++)
                {
                    if (ranked[i].Signal.SigId == clip.sig_id) { position = i + 1; break; }
                }

                if (position == 1) top1++;
                if (position > 0 && position <= 5) top5++;
                if (position > 0 && position <= 20) top20++;

                if (position != 1)
                {
                    SigIdCandidate best = ranked[0];
                    misses.Add("sig " + clip.sig_id + " expected at " +
                               (position > 0 ? position.ToString() : ">20") +
                               ", best was " + best.Signal.Name + " [" + best.Percent + "%]");
                }

                if (clip.acf.Length > 0 && priDumps < 6)
                {
                    priDumps++;
                    System.Console.WriteLine("   clip " + clip.sig_id + " expected " +
                        string.Join("/", Array.ConvertAll(clip.acf, a => a.ToString("0.##"))) +
                        " ms, measured periods " + string.Join(" ", Array.ConvertAll(
                            features.Periods.ToArray(), p => p.PeriodMs.ToString("0.##") + "@" + p.Correlation.ToString("0.00"))));
                    for (int i = 0; i < Math.Min(5, ranked.Count); i++)
                    {
                        SigIdCandidate c = ranked[i];
                        System.Console.WriteLine("      " + (i + 1) + ". " + c.Signal.Name +
                            " [" + c.Percent + "% " + c.Reason + "] bw=" +
                            string.Join("/", c.Signal.Bandwidths.ConvertAll(b => b.ToString()).ToArray()) +
                            " mod=" + string.Join("/", c.Signal.Modulations.ToArray()) +
                            " acf=" + string.Join("/", c.Signal.AcfEntries.ConvertAll(a => a.ValueMs.ToString("0.##")).ToArray()));
                    }
                }
            }

            Assert.True(tested > 100, "not enough clips were tested: " + tested);

            System.Console.WriteLine("[sigid] matcher ranking over " + tested + " reference clips: " +
                                    "top1 " + top1 + ", top5 " + top5 + ", top20 " + top20);
            foreach (string miss in misses) System.Console.WriteLine("   " + miss);

            // how good is the modulation classifier on its own?
            Dictionary<string, int> classHistogram = new Dictionary<string, int>();
            int classRight = 0;
            int classTested = 0;
            int familyRight = 0;
            int bestFamilyRight = 0;

            foreach (ClipEntry clip in clips)
            {
                if (!File.Exists(clip.wav)) continue;
                SigIdSignal own = database.Find(clip.sig_id);
                if (own == null) continue;

                float[] samples;
                int rate;
                if (!TryReadMonoWav(clip.wav, out samples, out rate)) continue;

                SigIdFeatures features = SigIdFeatureExtractor.Analyze(samples, rate, 0.0);
                SigIdModulationClass[] compatible = SigIdMatcher.CompatibleClasses(own);

                classTested++;
                string key = features.ModulationClass.ToString();
                if (!classHistogram.ContainsKey(key)) classHistogram[key] = 0;
                classHistogram[key]++;

                bool ok = false;
                foreach (SigIdModulationClass candidate in compatible)
                {
                    if (candidate == features.ModulationClass) { ok = true; break; }
                }
                if (ok) classRight++;

                bool anyCompatible = false;
                foreach (SigIdModulationClass candidate in compatible) anyCompatible = true;
                if (anyCompatible) familyRight++;
            }

            System.Console.WriteLine("[sigid] modulation class of the received signal vs the database entry:");
            foreach (KeyValuePair<string, int> pair in classHistogram)
                System.Console.WriteLine("   " + pair.Key + ": " + pair.Value);
            System.Console.WriteLine("[sigid] class compatible with the correct entry: " +
                                    classRight + "/" + classTested +
                                    " (" + (classTested > 0 ? (100.0 * classRight / classTested).ToString("0.0") : "0") + " %)");

            // ---- how much does knowing the tuned frequency help? -------------
            // In real use the operator knows the frequency, which the reference clips
            // cannot express.  Simulate it by ranking with the frequency band the
            // signal itself is documented in.
            int knownTop1 = 0;
            int knownTop5 = 0;
            int knownTop20 = 0;
            long candidateTotal = 0;
            int knownTested = 0;

            foreach (ClipEntry clip in clips)
            {
                if (!File.Exists(clip.wav)) continue;
                SigIdSignal own = database.Find(clip.sig_id);
                if (own == null || own.Frequencies.Count == 0) continue;

                float[] samples;
                int rate;
                if (!TryReadMonoWav(clip.wav, out samples, out rate)) continue;

                SigIdFeatures features = SigIdFeatureExtractor.Analyze(samples, rate, 0.0);

                // a representative frequency for this signal
                List<long> rows = new List<long>(own.Frequencies);
                rows.Sort();
                double tuned = rows.Count == 1 ? rows[0] : (rows[0] + rows[1]) / 2.0;

                List<SigIdCandidate> ranked = SigIdMatcher.Rank(features, database, tuned);
                if (ranked.Count == 0) continue;

                knownTested++;

                int position = -1;
                for (int i = 0; i < ranked.Count; i++)
                {
                    if (ranked[i].Signal.SigId == clip.sig_id) { position = i + 1; break; }
                }
                if (position == 1) knownTop1++;
                if (position > 0 && position <= 5) knownTop5++;
                if (position > 0 && position <= 20) knownTop20++;

                // how many entries survive the frequency filter alone
                int compatible = 0;
                foreach (SigIdCandidate candidate in ranked)
                {
                    if (candidate.Signal.Frequencies.Count == 0) { compatible++; continue; }
                    List<long> candidateRows = new List<long>(candidate.Signal.Frequencies);
                    candidateRows.Sort();
                    bool inside = false;
                    for (int i = 0; i + 1 < candidateRows.Count; i += 2)
                    {
                        if (tuned >= candidateRows[i] && tuned <= candidateRows[i + 1]) { inside = true; break; }
                    }
                    if (!inside && candidateRows.Count == 1 &&
                        Math.Abs(tuned - candidateRows[0]) <= Math.Max(1000.0, candidateRows[0] * 0.01)) inside = true;
                    if (inside) compatible++;
                }
                candidateTotal += compatible;
            }

            System.Console.WriteLine("[sigid] WITH a known tuned frequency: top1 " + knownTop1 +
                                    ", top5 " + knownTop5 + ", top20 " + knownTop20 +
                                    " (over " + knownTested + " clips)");
            if (knownTested > 0)
                System.Console.WriteLine("[sigid] entries compatible with the frequency alone: " +
                                        (candidateTotal / knownTested) + " of 583 on average");

            double ratio = (double)top1 / tested;
            Assert.True(ratio > 0.15,
                "top-1 accuracy was only " + (ratio * 100.0).ToString("0.0") + " % (" +
                top1 + "/" + tested + ")");
        }

        /// <summary>Minimal 16 bit PCM mono WAV reader.</summary>
        private static bool TryReadMonoWav(string path, out float[] samples, out int rate)
        {
            samples = null;
            rate = 0;

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                if (bytes.Length < 44) return false;

                int offset = 12;
                int channels = 0;
                int bitsPerSample = 0;
                int dataOffset = -1;
                int dataLength = 0;

                while (offset + 8 <= bytes.Length)
                {
                    string id = System.Text.Encoding.ASCII.GetString(bytes, offset, 4);
                    int size = BitConverter.ToInt32(bytes, offset + 4);

                    if (id == "fmt ")
                    {
                        channels = BitConverter.ToInt16(bytes, offset + 10);
                        rate = BitConverter.ToInt32(bytes, offset + 12);
                        bitsPerSample = BitConverter.ToInt16(bytes, offset + 22);
                    }
                    else if (id == "data")
                    {
                        dataOffset = offset + 8;
                        dataLength = size;
                        break;
                    }

                    offset += 8 + size + (size & 1);
                }

                if (dataOffset < 0 || channels != 1 || bitsPerSample != 16 || rate <= 0) return false;

                int count = Math.Min(dataLength / 2, bytes.Length - dataOffset);
                samples = new float[count / 2];
                for (int i = 0; i < samples.Length; i++)
                    samples[i] = BitConverter.ToInt16(bytes, dataOffset + i * 2) / 32768.0f;

                return samples.Length > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
