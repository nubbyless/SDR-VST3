/*  SigIdFeatureIndex.cs

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
// SigID - the measured fingerprint of every reference recording.                                  //
//                                                                                              //
// The Artemis database only documents a pulse repetition interval for 45 of its 583 signals, but  //
// it ships a reference recording for 543 of them.  Those recordings can be measured just like    //
// a live signal is: run the feature extractor over them once and keep the result.                //
//                                                                                              //
// That turns the database from "a wiki with sparse fields" into "583 signals with a measured     //
// profile" - pulse repetition interval, occupied bandwidth, symbol rate, modulation class and the //
// dominant spectral shape.  The matcher can then compare a live signal against all of them, not  //
// just against the handful that happen to be documented.                                          //
//                                                                                              //
// The index is built once, right after the database is installed, and stored as features.json    //
// next to the database.                                                                          //
//============================================================================================//

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Thetis
{
    /// <summary>The measured profile of one reference recording.</summary>
    public sealed class SigIdReferenceProfile
    {
        /// <summary>Database signal id this profile belongs to.</summary>
        public int SigId;

        /// <summary>Name of the media file the profile was measured from.</summary>
        public string Source = string.Empty;

        /// <summary>Seconds of audio that were analysed.</summary>
        public double Seconds;

        /// <summary>The dominant pulse repetition intervals, in milliseconds.</summary>
        public double[] PeriodsMs = new double[0];

        /// <summary>Occupied bandwidth in Hz.</summary>
        public double BandwidthHz;

        /// <summary>Estimated symbol rate in baud.</summary>
        public double SymbolRate;

        /// <summary>The modulation class the extractor assigned.</summary>
        public string ModulationClass = "Unknown";

        /// <summary>Number of distinct instantaneous frequency levels.</summary>
        public int IfClusterCount;

        /// <summary>Distance between the two strongest frequency levels, in Hz.</summary>
        public double IfClusterSeparationHz;

        /// <summary>Number of distinct phase states.</summary>
        public int PhaseStateCount;

        /// <summary>How bursty the signal is.</summary>
        public double EnvelopeVariation;

        /// <summary>Normalised spectral shape - the average spectrum in 24 bands.</summary>
        public double[] Spectrum = new double[0];

        /// <summary>Autocorrelation curve of the envelope, log spaced lags.</summary>
        public double[] AcfCurve = new double[0];

        /// <summary>Autocorrelation curve of the complex signal, log spaced lags.</summary>
        public double[] ComplexAcfCurve = new double[0];

        /// <summary>Builds the profile from a feature set.</summary>
        public static SigIdReferenceProfile From(int sigId, string source, SigIdFeatures features)
        {
            SigIdReferenceProfile profile = new SigIdReferenceProfile();
            profile.SigId = sigId;
            profile.Source = source;
            profile.Seconds = features.DurationSeconds;
            profile.BandwidthHz = features.OccupiedBandwidthHz;
            profile.SymbolRate = features.SymbolRate;
            profile.ModulationClass = features.ModulationClass.ToString();
            profile.IfClusterCount = features.IfClusterCount;
            profile.IfClusterSeparationHz = features.IfClusterSeparationHz;
            profile.PhaseStateCount = features.PhaseStateCount;
            profile.EnvelopeVariation = features.EnvelopeVariation;

            List<double> periods = new List<double>();
            foreach (SigIdPeriod period in features.Periods)
            {
                if (period.Correlation < 0.25) continue;
                periods.Add(period.PeriodMs);
                if (periods.Count >= 12) break;
            }
            profile.PeriodsMs = periods.ToArray();

            if (features.AcfCurve != null) profile.AcfCurve = features.AcfCurve;
            if (features.ComplexAcfCurve != null) profile.ComplexAcfCurve = features.ComplexAcfCurve;

            return profile;
        }

        /// <summary>
        /// Compares the measured periods with a set of candidate periods.
        /// Returns the quality of the best match, 0 … 1.
        /// </summary>
        public double MatchPeriods(IEnumerable<SigIdPeriod> measured)
        {
            if (PeriodsMs.Length == 0 || measured == null) return 0.0;

            double best = 0.0;
            foreach (SigIdPeriod period in measured)
            {
                foreach (double reference in PeriodsMs)
                {
                    double error = Math.Abs(period.PeriodMs - reference);
                    double tolerance = Math.Max(3.0, reference * 0.08);
                    if (error > tolerance) continue;

                    double accuracy = 1.0 - error / tolerance;
                    double value = 0.6 + 0.4 * accuracy;
                    if (value > best) best = value;
                }
            }
            return best;
        }
    }

    public sealed class SigIdFeatureIndex
    {
        /// <summary>File name of the index inside the SigId folder.</summary>
        public const string FILE_NAME = "features.json";

        /// <summary>How much of a reference clip is analysed.</summary>
        public const double ANALYSIS_SECONDS = 10.0;

        /// <summary>Profile per database signal id.</summary>
        public Dictionary<int, SigIdReferenceProfile> Profiles = new Dictionary<int, SigIdReferenceProfile>();

        /// <summary>When the index was built.</summary>
        public string BuiltUtc = string.Empty;

        /// <summary>Number of profiles in the index.</summary>
        public int Count
        {
            get { return Profiles.Count; }
        }

        /// <summary>Full path of the index file.</summary>
        public static string IndexPath
        {
            get { return Path.Combine(SigIdPaths.Root, FILE_NAME); }
        }

        /// <summary>Reads the index.  Returns null when it does not exist or cannot be read.</summary>
        public static SigIdFeatureIndex Load()
        {
            try
            {
                string path = IndexPath;
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return null;
                return JsonConvert.DeserializeObject<SigIdFeatureIndex>(json);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Writes the index next to the database.</summary>
        public void Save()
        {
            try
            {
                if (!SigIdPaths.EnsureRoot()) return;
                BuiltUtc = DateTime.UtcNow.ToString("o");
                File.WriteAllText(IndexPath, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception)
            {
                // the index is a cache, never fatal
            }
        }

        /// <summary>The profile of a signal, or null.</summary>
        public SigIdReferenceProfile Find(int sigId)
        {
            SigIdReferenceProfile profile;
            return Profiles.TryGetValue(sigId, out profile) ? profile : null;
        }

        // ====================================================================
        // Building the index
        // ====================================================================

        /// <summary>
        /// Measures every reference recording in the installed database.  This is the
        /// expensive step - it decodes and analyses 543 clips - so it runs once, after the
        /// database has been installed, and the result is cached in features.json.
        /// </summary>
        public static SigIdFeatureIndex Build(SigIdDatabase database, string mediaDirectory,
                                               Action<string> onProgress)
        {
            SigIdFeatureIndex index = new SigIdFeatureIndex();
            if (database == null) return index;

            if (!SigIdAudioDecoder.Available)
            {
                if (onProgress != null) onProgress("libsndfile is not available - " + SigIdAudioDecoder.LastError);
                return index;
            }

            const int analysisRate = 24000;

            int done = 0;
            foreach (SigIdSignal signal in database.Signals)
            {
                done++;
                if (onProgress != null && (done % 25) == 0)
                    onProgress("measuring reference recordings: " + done + "/" + database.Signals.Count);

                if (string.IsNullOrEmpty(signal.AudioFileName)) continue;

                string path = Path.Combine(mediaDirectory, signal.AudioFileName);
                float[] samples;
                int rate;
                if (!SigIdAudioDecoder.TryDecode(path, out samples, out rate)) continue;

                // only the first ANALYSIS_SECONDS are needed - the repetition structure
                // is visible much sooner and this keeps the whole pass to a minute
                int wanted = (int)(ANALYSIS_SECONDS * rate);
                if (samples.Length > wanted)
                {
                    float[] trimmed = new float[wanted];
                    Array.Copy(samples, trimmed, wanted);
                    samples = trimmed;
                }

                if (rate != analysisRate)
                    samples = SigIdAudioDecoder.Resample(samples, rate, analysisRate);

                SigIdFeatures features = SigIdFeatureExtractor.Analyze(samples, analysisRate, 0.0);
                SigIdReferenceProfile profile = SigIdReferenceProfile.From(signal.SigId, signal.AudioFileName, features);
                index.Profiles[signal.SigId] = profile;
            }

            if (onProgress != null) onProgress("measured " + index.Profiles.Count + " reference recordings");
            return index;
        }
    }
}
