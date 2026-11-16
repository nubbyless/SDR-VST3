/*  SigIdMatcher.cs

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
// SigID - scoring the measured features against the Artemis database.                          //
//                                                                                              //
// The database is a taxonomy, not a fingerprint database: most entries only describe a        //
// family ("Pulse", "FSK", "AM"), a bandwidth and sometimes a pulse repetition interval.        //
// So the matcher produces a *ranking* with a confidence, not a single answer.                   //
//                                                                                              //
// Scoring weights (they add up to 100):                                                       //
//   PRI / ACF      35   the strongest discriminator - a documented repetition interval that   //
//                       the audio actually shows is very hard to fake                          //
//   modulation     25   the measured family has to be compatible with the database entry     //
//   bandwidth      20   the occupied width has to be plausible for the entry                  //
//   mode           10   USB / LSB / CW sense from the carrier offset                           //
//   frequency      10   the tuned frequency has to be plausible for the entry                 //
//                                                                                              //
// Nothing is ever rejected outright - a wrong classification costs points instead, because    //
// the classifier is good but not perfect.                                                      //
//============================================================================================//

using System;
using System.Collections.Generic;

namespace Thetis
{
    /// <summary>One ranked candidate.</summary>
    public sealed class SigIdCandidate
    {
        public SigIdSignal Signal;

        /// <summary>Confidence from 0 to 100.</summary>
        public int Percent;

        /// <summary>Short explanation of what matched.</summary>
        public string Reason = string.Empty;

        public string Name
        {
            get { return Signal != null ? Signal.Name : string.Empty; }
        }
    }

    public static class SigIdMatcher
    {
        private const double WEIGHT_PRI = 35.0;
        private const double WEIGHT_MODULATION = 25.0;
        private const double WEIGHT_BANDWIDTH = 20.0;
        private const double WEIGHT_MODE = 10.0;
        private const double WEIGHT_FREQUENCY = 10.0;

        /// <summary>How close a measured period has to be to the documented one.</summary>
        private static readonly double PRI_TOLERANCE = 0.08;

        /// <summary>
        /// Ranks every database entry against the measured features.
        /// </summary>
        public static List<SigIdCandidate> Rank(SigIdFeatures features, SigIdDatabase database, double tunedHz)
        {
            return Rank(features, database, null, tunedHz);
        }

        /// <summary>
        /// Ranks every database entry against the measured features.
        ///
        /// The feature index holds the measured profile of every reference recording.
        /// When it is available it is the stronger evidence: it gives every signal a
        /// repetition interval, a bandwidth and a symbol rate, even for the entries where
        /// the wiki documents none of them.
        /// </summary>
        public static List<SigIdCandidate> Rank(SigIdFeatures features, SigIdDatabase database,
                                                SigIdFeatureIndex index, double tunedHz)
        {
            return Rank(features, database, index, tunedHz, SigIdScope.AllSignals);
        }

        /// <summary>
        /// Ranks the database entries that are in the scope against the measured features.
        /// </summary>
        public static List<SigIdCandidate> Rank(SigIdFeatures features, SigIdDatabase database,
                                                SigIdFeatureIndex index, double tunedHz,
                                                SigIdScope scope)
        {
            return Rank(features, database, index, tunedHz, scope, -1);
        }

        /// <summary>
        /// Ranks every database entry against the measured features.
        ///
        /// The feature index holds the measured profile of every reference recording.
        /// When it is available it is the stronger evidence: it gives every signal a
        /// repetition interval, a bandwidth and a symbol rate, even for the entries where
        /// the wiki documents none of them.
        ///
        /// <paramref name="excludeSigId"/> leaves one signal out of the ranking, which is
        /// what the leave-one-out validation needs - otherwise a reference recording
        /// would always match its own profile.
        /// </summary>
        public static List<SigIdCandidate> Rank(SigIdFeatures features, SigIdDatabase database,
                                                SigIdFeatureIndex index, double tunedHz,
                                                SigIdScope scope, int excludeSigId)
        {
            List<SigIdCandidate> results = new List<SigIdCandidate>();
            if (features == null || database == null || !database.HasSignals) return results;

            foreach (SigIdSignal signal in database.Signals)
            {
                if (signal.SigId == excludeSigId) continue;
                if (!SigIdScopes.Includes(scope, signal)) continue;
                // The leave-one-out validation removes the *profile* of the signal under
                // test, so it cannot match its own reference recording - but the signal
                // itself stays in the list, because its position is the measurement.
                SigIdReferenceProfile profile = null;
                if (index != null && signal.SigId != excludeSigId)
                    profile = index.Find(signal.SigId);

                double score;
                List<string> reasons = new List<string>();

                if (profile != null)
                {
                    score = ScoreAgainstProfile(features, profile, signal, tunedHz, reasons);
                }
                else
                {
                    // no reference recording - fall back to the documented fields
                    score = ScoreAgainstDocumented(features, signal, tunedHz, reasons);
                }

                SigIdCandidate candidate = new SigIdCandidate();
                candidate.Signal = signal;
                candidate.Percent = (int)Math.Round(Math.Max(0.0, Math.Min(100.0, score)));
                candidate.Reason = reasons.Count > 0 ? string.Join(" + ", reasons.ToArray()) : "weak";
                results.Add(candidate);
            }

            results.Sort(delegate (SigIdCandidate a, SigIdCandidate b)
            {
                int comparison = b.Percent.CompareTo(a.Percent);
                return comparison != 0 ? comparison : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });

            return results;
        }

        // ====================================================================
        // Scoring against a measured reference profile
        // ====================================================================

        /// <summary>
        /// Compares the live signal with the measured profile of a reference recording.
        /// Every term is a bounded, tight comparison - a loose match on one feature must
        /// not be able to outvote a good match on all the others.
        /// </summary>
        private static double ScoreAgainstProfile(SigIdFeatures live, SigIdReferenceProfile profile,
                                                  SigIdSignal signal, double tunedHz, List<string> reasons)
        {
            double score = 0.0;
            double maximum = 0.0;

            // ---- repetition interval (30) ---------------------------------------
            maximum += 30.0;
            double periodScore = PeriodSimilarity(live, profile);
            score += periodScore * 30.0;
            if (periodScore > 0.85) reasons.Add("PRI");
            // ---- occupied bandwidth (20) ---------------------------------------
            maximum += 20.0;
            score += BandwidthSimilarity(live, profile) * 20.0;

            // ---- symbol rate (12) ----------------------------------------------
            maximum += 12.0;
            score += SymbolRateSimilarity(live, profile) * 12.0;

            // ---- number of frequency levels (10) --------------------------------
            maximum += 10.0;
            score += CountSimilarity(live.IfClusterCount, profile.IfClusterCount, 4) * 10.0;

            // ---- number of phase states (10) ------------------------------------
            maximum += 10.0;
            score += CountSimilarity(live.PhaseStateCount, profile.PhaseStateCount, 4) * 10.0;

            // ---- burstiness (8) -------------------------------------------------
            maximum += 8.0;
            score += EnvelopeSimilarity(live.EnvelopeVariation, profile.EnvelopeVariation) * 8.0;

            // ---- modulation class (10) ------------------------------------------
            maximum += 10.0;
            double modulationScore = ClassSimilarity(live.ModulationClass, profile.ModulationClass);
            score += modulationScore * 10.0;
            if (modulationScore > 0.9) reasons.Add("modulation");

            // ---- mode and frequency from the documented fields (10) ------------
            maximum += 10.0;
            score += (ScoreMode(live, signal) * 0.5 + ScoreFrequency(signal, tunedHz) * 0.5) * 10.0;

            if (maximum <= 0.0) return 0.0;
            return 100.0 * score / maximum;
        }

        /// <summary>
        /// How well the repetition structures agree.
        ///
        /// The autocorrelation *curve* is compared, not the list of peaks.  The peaks of
        /// a periodic signal form a dense harmonic comb, so comparing peak lists matches
        /// almost everything.  The curve is the actual fingerprint: a pulse train, an
        /// FSK burst train and a continuous signal produce clearly different shapes.
        /// </summary>
        private static double PeriodSimilarity(SigIdFeatures live, SigIdReferenceProfile profile)
        {
            double envelope = CurveSimilarity(live.AcfCurve, profile.AcfCurve);
            double complex = CurveSimilarity(live.ComplexAcfCurve, profile.ComplexAcfCurve);

            double best = Math.Max(envelope, complex);
            if (envelope <= 0.0 && complex <= 0.0) return 0.35;   // nothing comparable

            return best;
        }

        /// <summary>
        /// Similarity of two autocorrelation curves.  The curves are compared after
        /// removing their means, so a constant offset does not matter, and the result is
        /// the normalised correlation mapped onto 0 … 1.
        /// </summary>
        private static double CurveSimilarity(double[] a, double[] b)
        {
            if (a == null || b == null || a.Length < 8 || b.Length < 8) return 0.0;
            int count = Math.Min(a.Length, b.Length);

            double meanA = 0.0;
            double meanB = 0.0;
            for (int i = 0; i < count; i++)
            {
                meanA += a[i];
                meanB += b[i];
            }
            meanA /= count;
            meanB /= count;

            double covariance = 0.0;
            double varianceA = 0.0;
            double varianceB = 0.0;
            for (int i = 0; i < count; i++)
            {
                double da = a[i] - meanA;
                double db = b[i] - meanB;
                covariance += da * db;
                varianceA += da * da;
                varianceB += db * db;
            }

            if (varianceA <= 1e-12 || varianceB <= 1e-12) return 0.0;

            double correlation = covariance / Math.Sqrt(varianceA * varianceB);
            if (correlation <= 0.0) return 0.0;

            // a correlation of 0.5 already means "related structure"
            return Math.Min(1.0, (correlation - 0.2) / 0.6);
        }

        private static double BandwidthSimilarity(SigIdFeatures live, SigIdReferenceProfile profile)
        {
            if (profile.BandwidthHz <= 0.0 || live.OccupiedBandwidthHz <= 0.0) return 0.35;

            double distance = Math.Abs(Math.Log(live.OccupiedBandwidthHz / profile.BandwidthHz));

            // when the RX filter cut the signal the live width is only an upper bound
            if (live.FilterLimited) return live.OccupiedBandwidthHz <= profile.BandwidthHz ? 0.7 : 0.25;

            const double tolerance = 1.0;   // a factor of e
            if (distance > tolerance) return 0.0;
            return 1.0 - distance / tolerance;
        }

        private static double SymbolRateSimilarity(SigIdFeatures live, SigIdReferenceProfile profile)
        {
            if (profile.SymbolRate <= 0.0 || live.SymbolRate <= 0.0) return 0.35;

            double distance = Math.Abs(Math.Log(live.SymbolRate / profile.SymbolRate));
            const double tolerance = 0.6;
            if (distance > tolerance) return 0.0;
            return 1.0 - distance / tolerance;
        }

        private static double CountSimilarity(int live, int reference, int range)
        {
            if (live <= 0 || reference <= 0) return 0.35;
            int difference = Math.Abs(live - reference);
            if (difference >= range) return 0.0;
            return 1.0 - (double)difference / range;
        }

        private static double EnvelopeSimilarity(double live, double reference)
        {
            if (live <= 0.0 || reference <= 0.0) return 0.35;
            double difference = Math.Abs(live - reference);
            const double tolerance = 1.2;
            if (difference > tolerance) return 0.0;
            return 1.0 - difference / tolerance;
        }

        /// <summary>How well two modulation classes agree.</summary>
        private static double ClassSimilarity(SigIdModulationClass live, string referenceName)
        {
            if (live == SigIdModulationClass.Unknown || string.IsNullOrWhiteSpace(referenceName))
                return 0.35;

            SigIdModulationClass reference;
            try { reference = (SigIdModulationClass)Enum.Parse(typeof(SigIdModulationClass), referenceName); }
            catch (Exception) { return 0.35; }

            if (reference == live) return 1.0;

            // the same family, e.g. both FSK
            if (IsSameFamily(live, reference)) return 0.6;
            return 0.0;
        }

        private static bool IsSameFamily(SigIdModulationClass a, SigIdModulationClass b)
        {
            if (a == b) return true;
            if (a == SigIdModulationClass.Psk && b == SigIdModulationClass.Fsk) return true;
            if (a == SigIdModulationClass.Fsk && b == SigIdModulationClass.Psk) return true;
            if (a == SigIdModulationClass.Am && b == SigIdModulationClass.Pulse) return true;
            if (a == SigIdModulationClass.Pulse && b == SigIdModulationClass.Am) return true;
            if (a == SigIdModulationClass.Broadband && b == SigIdModulationClass.Psk) return true;
            if (a == SigIdModulationClass.Psk && b == SigIdModulationClass.Broadband) return true;
            return false;
        }

        // ====================================================================
        // Scoring against the documented fields (no reference recording)
        // ====================================================================

        private static double ScoreAgainstDocumented(SigIdFeatures features, SigIdSignal signal,
                                                     double tunedHz, List<string> reasons)
        {
            double score = 0.0;

            double priScore = ScorePriorities(features, signal);
            score += priScore * WEIGHT_PRI;
            if (priScore > 0.9) reasons.Add("PRI");
            else if (priScore < 0.1 && signal.AcfEntries.Count > 0) reasons.Add("no PRI");

            double modulationScore = ScoreModulation(features, signal, null);
            score += modulationScore * WEIGHT_MODULATION;
            if (modulationScore > 0.9) reasons.Add("modulation");

            double bandwidthScore = ScoreBandwidth(features, signal, null);
            score += bandwidthScore * WEIGHT_BANDWIDTH;

            double modeScore = ScoreMode(features, signal);
            score += modeScore * WEIGHT_MODE;

            double frequencyScore = ScoreFrequency(signal, tunedHz);
            score += frequencyScore * WEIGHT_FREQUENCY;

            return score;
        }

        // ====================================================================
        // Pulse repetition interval
        // ====================================================================

        /// <summary>
        /// Scores the measured periods against the periods the database documents.
        /// A documented period that the audio actually shows is a very strong hint.
        /// </summary>
        private static double ScorePriorities(SigIdFeatures features, SigIdSignal signal)
        {
            if (signal.AcfEntries.Count == 0) return 0.5;     // nothing documented - no opinion
            if (features.Periods.Count == 0) return 0.2;        // nothing measured at all

            double best = 0.0;
            foreach (SigIdAcfEntry entry in signal.AcfEntries)
            {
                if (entry.ValueMs <= 0.0) continue;

                foreach (SigIdPeriod period in features.Periods)
                {
                    double error = Math.Abs(period.PeriodMs - entry.ValueMs);
                    double tolerance = Math.Max(3.0, entry.ValueMs * PRI_TOLERANCE);
                    if (error > tolerance) continue;

                    // the closer the match and the stronger the correlation, the better
                    double accuracy = 1.0 - error / tolerance;
                    double value = 0.55 + 0.35 * accuracy + 0.10 * Math.Min(1.0, period.Correlation);
                    if (value > best) best = value;
                }
            }

            return best;
        }

        // ====================================================================
        // Modulation
        // ====================================================================

        /// <summary>
        /// Checks the measured modulation class against the modulation values the
        /// database lists for the signal.
        /// </summary>
        private static double ScoreModulation(SigIdFeatures features, SigIdSignal signal,
                                             SigIdReferenceProfile profile)
        {
            if (signal.Modulations.Count == 0) return 0.5;
            if (features.ModulationClass == SigIdModulationClass.Unknown) return 0.5;

            bool compatible = false;
            bool related = false;

            foreach (string modulation in signal.Modulations)
            {
                int score = ModulationFamilyScore(features.ModulationClass, modulation);
                if (score >= 2) { compatible = true; break; }
                if (score == 1) related = true;
            }

            // the reference recording knows what the signal really is - when its measured
            // class agrees with the database entry, that is a strong confirmation
            if (profile != null && !string.IsNullOrEmpty(profile.ModulationClass))
            {
                SigIdModulationClass reference = SigIdModulationClass.Unknown;
                try { reference = (SigIdModulationClass)Enum.Parse(typeof(SigIdModulationClass), profile.ModulationClass); }
                catch (Exception) { }

                if (reference != SigIdModulationClass.Unknown && reference == features.ModulationClass)
                    return 1.0;
            }

            if (compatible) return 1.0;
            if (related) return 0.55;
            return 0.15;
        }

        /// <summary>
        /// How well a measured modulation class fits a database modulation value.
        /// 2 = exact family, 1 = related, 0 = not compatible.
        /// </summary>
        internal static int ModulationFamilyScore(SigIdModulationClass measured, string modulation)
        {
            if (string.IsNullOrWhiteSpace(modulation)) return 0;
            string value = modulation.Trim().ToUpperInvariant();

            switch (measured)
            {
                case SigIdModulationClass.Fsk:
                    if (In(value, "FSK", "AFSK", "BFSK", "M-ARY FSK", "GFSK", "CPFSK", "CWFSK", "MSK", "GMSK", "C4FM", "FSK-CW", "IFK")) return 2;
                    if (In(value, "MSK", "GMSK")) return 2;
                    return 0;

                case SigIdModulationClass.Psk:
                    if (In(value, "PSK", "BPSK", "QPSK", "OQPSK", "DPSK", "SDPSK", "DQPSK", "D8PSK", "APSK", "QAM", "GMSK", "MSK", "TCM")) return 2;
                    if (In(value, "QAM", "TCM")) return 1;
                    return 0;

                case SigIdModulationClass.Tone:
                    if (In(value, "CW", "PULSE", "FMOP", "FMCW", "CWFSK")) return 2;
                    return 0;

                case SigIdModulationClass.Am:
                    if (In(value, "AM", "DSB", "DSB-WC", "DSB-SC", "DSB-RC", "SSB", "SSB-WC", "SSB-SC", "SSB-RC", "USB", "LSB", "VSB", "FM", "PM", "NFM", "WFM")) return 2;
                    if (In(value, "PULSE", "PAM", "PWM", "PPM", "PDM", "PFM", "PCM", "DPCM", "CVSDM", "ASK", "OOK", "FMOP", "FMCW", "IRIG")) return 1;
                    return 0;

                case SigIdModulationClass.Pulse:
                    if (In(value, "PULSE", "FMOP", "FMCW", "IRIG", "PAM", "PWM", "PPM", "PDM", "PFM")) return 2;
                    if (In(value, "AM", "DSB", "DSB-WC", "DSB-SC", "DSB-RC", "SSB", "SSB-WC", "SSB-SC", "SSB-RC", "USB", "LSB", "VSB", "PCM", "DPCM", "CVSDM", "ASK", "OOK")) return 1;
                    return 0;


                case SigIdModulationClass.Broadband:
                    if (In(value, "OFDM", "COFDM", "CP-OFDM", "DMT", "FBMC", "UFMC", "OTFS", "DSSS", "FHSS", "THSS", "CSS", "LFM", "QAM", "SC-FDMA", "TDMA", "CDMA", "NOMA", "BOC", "TMBOC", "MP-DASK")) return 2;
                    if (In(value, "PSK", "QPSK", "BPSK", "DSSS", "FHSS")) return 1;
                    return 0;
            }

            return 0;
        }

        private static bool In(string value, params string[] options)
        {
            for (int i = 0; i < options.Length; i++)
            {
                if (value == options[i]) return true;
            }
            return false;
        }

        /// <summary>
        /// Which modulation classes are compatible with the modulation values a signal
        /// carries in the database.  Used to check how good the classifier is.
        /// </summary>
        internal static SigIdModulationClass[] CompatibleClasses(SigIdSignal signal)
        {
            List<SigIdModulationClass> classes = new List<SigIdModulationClass>();
            SigIdModulationClass[] all =
            {
                SigIdModulationClass.Tone,
                SigIdModulationClass.Fsk,
                SigIdModulationClass.Psk,
                SigIdModulationClass.Am,
                SigIdModulationClass.Broadband
            };

            foreach (SigIdModulationClass measured in all)
            {
                bool compatible = false;
                bool related = false;
                foreach (string modulation in signal.Modulations)
                {
                    int score = ModulationFamilyScore(measured, modulation);
                    if (score >= 2) { compatible = true; break; }
                    if (score == 1) related = true;
                }
                if (compatible) classes.Add(measured);
                else if (related) classes.Add(measured);   // related still counts as "not ruled out"
            }
            return classes.ToArray();
        }

        // ====================================================================
        // Bandwidth
        // ====================================================================

        /// <summary>
        /// Checks the measured occupied bandwidth against the widths the database lists.
        /// A signal may list several widths, so the closest one is used.
        /// </summary>
        private static double ScoreBandwidth(SigIdFeatures features, SigIdSignal signal,
                                             SigIdReferenceProfile profile)
        {
            if (signal.Bandwidths.Count == 0) return 0.5;
            if (features.OccupiedBandwidthHz <= 0.0) return 0.5;

            double measured = features.OccupiedBandwidthHz;
            double best = 0.0;

            foreach (int width in signal.Bandwidths)
            {
                if (width <= 0) continue;

                double score;
                if (features.FilterLimited)
                {
                    // the audio was cut by the RX filter, so only an upper bound is known
                    score = measured <= width ? 0.85 : 0.4;
                }
                else
                {
                    // compare on a log scale - a factor of two is a rough match
                    double ratio = measured / (double)width;
                    if (ratio < 1.0) ratio = 1.0 / ratio;
                    score = 1.0 / (1.0 + 2.0 * Math.Log(ratio));
                }

                if (score > best) best = score;
            }

            // the reference recording was made with the receiver set up for this signal,
            // so its measured width is the better yardstick
            if (profile != null && profile.BandwidthHz > 0.0 && !features.FilterLimited)
            {
                double ratio = measured / profile.BandwidthHz;
                if (ratio < 1.0) ratio = 1.0 / ratio;
                double score = 1.0 / (1.0 + 2.0 * Math.Log(ratio));
                if (score > best) best = score;
            }

            return best;
        }

        // ====================================================================
        // Mode
        // ====================================================================

        /// <summary>
        /// Checks the side of the filter the signal sits on against the USB / LSB / CW
        /// values the database lists.
        /// </summary>
        private static double ScoreMode(SigIdFeatures features, SigIdSignal signal)
        {
            if (signal.Modes.Count == 0) return 0.5;

            double offset = features.CarrierOffsetHz;

            foreach (string mode in signal.Modes)
            {
                string value = mode.Trim().ToUpperInvariant();

                if (value == "CW" || value == "AM" || value == "DSB" || value == "FM" || value == "NFM" || value == "PM")
                    return 1.0;

                if (value == "USB" || value == "SSB" || value == "SSB-WC")
                    return offset >= -50.0 ? 1.0 : 0.4;

                if (value == "LSB" || value == "SSB-SC")
                    return offset <= 50.0 ? 1.0 : 0.4;

                if (value == "RAW" || value == "RAW I/Q" || value == "IQ" || value == "OFDM" ||
                    value == "COFDM" || value == "CP-OFDM" || value == "ACM/VCM" || value == "DTMF" ||
                    value == "FHSS-TDM" || value == "SC-FDMA" || value == "VSB" || value == "WFM")
                    return 0.7;
            }

            return 0.5;
        }

        // ====================================================================
        // Frequency
        // ====================================================================

        /// <summary>
        /// Checks the tuned frequency against the bands the database lists.  Two rows are
        /// the edges of one band, a single row is a point frequency.
        /// </summary>
        private static double ScoreFrequency(SigIdSignal signal, double tunedHz)
        {
            if (signal.Frequencies.Count == 0) return 0.5;
            if (tunedHz <= 0.0) return 0.5;

            List<long> rows = new List<long>(signal.Frequencies);
            rows.Sort();

            if (rows.Count == 1)
            {
                double distance = Math.Abs(tunedHz - rows[0]);
                double tolerance = Math.Max(1000.0, rows[0] * 0.01);
                return distance <= tolerance ? 1.0 : 0.5;
            }

            // pairs of rows are band edges
            for (int i = 0; i + 1 < rows.Count; i += 2)
            {
                if (tunedHz >= rows[i] && tunedHz <= rows[i +1]) return 1.0;
            }

            // an odd row out is a point frequency
            if ((rows.Count % 2) == 1)
            {
                long last = rows[rows.Count - 1];
                double tolerance = Math.Max(1000.0, last * 0.01);
                if (Math.Abs(tunedHz - last) <= tolerance) return 1.0;
            }

            // inside the overall span but not in one of the bands - partial credit
            if (tunedHz >= rows[0] && tunedHz <= rows[rows.Count - 1]) return 0.6;

            return 0.35;
        }
    }
}
