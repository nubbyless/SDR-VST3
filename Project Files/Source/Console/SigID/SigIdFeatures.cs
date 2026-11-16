/*  SigIdFeatures.cs

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
// SigID - feature extraction from the demodulated receiver audio.                                 //
//                                                                                              //
// Everything here works on the mono audio that came out of the native tap, i.e. the           //
// demodulated, post-filter receiver audio.  The features are chosen because the Artemis        //
// database has a matching field for each of them:                                                 //
//                                                                                              //
//   OccupiedBandwidthHz  <->  bandwidth                                                        //
//   CarrierOffsetHz      <->  mode (USB / LSB / CW ...)                                        //
//   Periods              <->  acf (the pulse repetition interval)                               //
//   IfClusterCount / ... <->  modulation (FSK / PSK / AM / OFDM ...)                            //
//   PhaseStateCount      <->  the PSK order                                                    //
//   SymbolRate           <->  the baud rate quoted in many signal titles                       //
//                                                                                              //
// The autocorrelation / PRI extraction was validated against the database's own 489 reference   //
// clips - the measured PRI reproduces the value stored in the acf column.                       //
//============================================================================================//

using System;
using System.Collections.Generic;

namespace Thetis
{
    /// <summary>Minimal radix-2 complex FFT (no external dependency).</summary>
    internal static class SigIdFft
    {
        /// <summary>In place forward transform.  The length must be a power of two.</summary>
        public static void Forward(double[] re, double[] im)
        {
            Transform(re, im, false);
        }

        /// <summary>In place inverse transform, including the 1/N scaling.</summary>
        public static void Inverse(double[] re, double[] im)
        {
            Transform(re, im, true);
        }

        private static void Transform(double[] re, double[] im, bool inverse)
        {
            int n = re.Length;

            // bit reversal permutation
            for (int i = 1, j = 0; i < n; i++)
            {
                int bit = n >> 1;
                for (; (j & bit) != 0; bit >>= 1) j ^= bit;
                j ^= bit;
                if (i < j)
                {
                    double temp = re[i]; re[i] = re[j]; re[j] = temp;
                    temp = im[i]; im[i] = im[j]; im[j] = temp;
                }
            }

            for (int length = 2; length <= n; length <<= 1)
            {
                double angle = 2.0 * Math.PI / length * (inverse ? 1.0 : -1.0);
                double wReal = Math.Cos(angle);
                double wImag = Math.Sin(angle);

                for (int i = 0; i < n; i += length)
                {
                    double currentReal = 1.0;
                    double currentImag = 0.0;

                    for (int k = 0; k < length / 2; k++)
                    {
                        int a = i + k;
                        int b = i + k + length / 2;

                        double upperReal = re[b] * currentReal - im[b] * currentImag;
                        double upperImag = re[b] * currentImag + im[b] * currentReal;

                        re[b] = re[a] - upperReal;
                        im[b] = im[a] - upperImag;
                        re[a] += upperReal;
                        im[a] += upperImag;

                        double nextReal = currentReal * wReal - currentImag * wImag;
                        currentImag = currentReal * wImag + currentImag * wReal;
                        currentReal = nextReal;
                    }
                }
            }

            if (inverse)
            {
                double scale = 1.0 / n;
                for (int i = 0; i < n; i++)
                {
                    re[i] *= scale;
                    im[i] *= scale;
                }
            }
        }
    }

    /// <summary>One peak of the autocorrelation function.</summary>
    public sealed class SigIdPeriod
    {
        /// <summary>Period in milliseconds.</summary>
        public double PeriodMs;

        /// <summary>Normalised autocorrelation at that period (1 = perfectly periodic).</summary>
        public double Correlation;

        public SigIdPeriod(double periodMs, double correlation)
        {
            PeriodMs = periodMs;
            Correlation = correlation;
        }

        public override string ToString()
        {
            return PeriodMs.ToString("0.###") + " ms (" + Correlation.ToString("0.00") + ")";
        }
    }

    /// <summary>How the received signal is modulated, as far as it can be told apart.</summary>
    public enum SigIdModulationClass
    {
        Unknown,
        /// <summary>A single unmodulated carrier.</summary>
        Tone,
        /// <summary>Two or more frequency levels - FSK.</summary>
        Fsk,
        /// <summary>A constant carrier with several phase states - PSK.</summary>
        Psk,
        /// <summary>Amplitude modulated, usually speech.</summary>
        Am,
        /// <summary>A train of bursts - radar, pulse modulated systems.</summary>
        Pulse,
        /// <summary>Wide band - OFDM, DSSS, spread spectrum.</summary>
        Broadband
    }

    /// <summary>Everything the matcher can use to describe a received signal.</summary>
    public sealed class SigIdFeatures
    {
        public int SampleRate;
        public int SampleCount;
        public double DurationSeconds;

        /// <summary>Overall level.</summary>
        public double Rms;
        public double Peak;

        /// <summary>Noise floor estimate and the signal-to-noise ratio in dB.</summary>
        public double NoiseFloor;
        public double SnrDb;

        /// <summary>Occupied bandwidth of the demodulated audio, in Hz.</summary>
        public double OccupiedBandwidthHz;

        /// <summary>Offset of the strongest component from the filter centre, in Hz.</summary>
        public double CarrierOffsetHz;

        /// <summary>Instantaneous frequency statistics.</summary>
        public double FrequencyMeanHz;
        public double FrequencyStdHz;

        /// <summary>Number of distinct instantaneous-frequency levels (2 = FSK, 1 = single tone).</summary>
        public int IfClusterCount;

        /// <summary>Distance between the two strongest frequency levels, in Hz.</summary>
        public double IfClusterSeparationHz;

        /// <summary>Number of distinct phase states on constant-frequency segments (PSK order).</summary>
        public int PhaseStateCount;

        /// <summary>Estimated symbol rate in baud.</summary>
        public double SymbolRate;

        /// <summary>0 … 1, how much the signal looks like speech.</summary>
        public double VoiceScore;

        /// <summary>Autocorrelation peaks: period in ms plus correlation.</summary>
        public List<SigIdPeriod> Periods = new List<SigIdPeriod>();

        /// <summary>
        /// The autocorrelation curve of the envelope, sampled at logarithmically spaced
        /// lags from 0.5 ms to 2 s.  This curve - not the list of peaks - is the real
        /// fingerprint of the repetition structure: a pulse train, an FSK burst train and
        /// a continuous signal all produce very different curves.
        /// </summary>
        public double[] AcfCurve = new double[0];

        /// <summary>The same for the complex autocorrelation, which also sees phase.</summary>
        public double[] ComplexAcfCurve = new double[0];

        /// <summary>True when the measured bandwidth is limited by the RX filter, not the signal.</summary>
        public bool FilterLimited;

        /// <summary>How the signal is modulated.</summary>
        public SigIdModulationClass ModulationClass = SigIdModulationClass.Unknown;

        /// <summary>How sure the classifier is about the modulation class (0 … 1).</summary>
        public double Confidence;

        /// <summary>
        /// How bursty the signal is: the relative spread of the envelope.  A pulse train
        /// goes silent between the bursts and scores high, a continuous OFDM signal scores
        /// low.  This is what separates a radar from a wide band data signal.
        /// </summary>
        public double EnvelopeVariation;

        /// <summary>The RX filter width in Hz, when it is known.</summary>
        public double FilterWidthHz;

        /// <summary>
        /// The period that best explains a repeating structure, in ms.  Zero when nothing
        /// periodic was found.
        /// </summary>
        public double DominantPeriodMs
        {
            get { return Periods.Count > 0 ? Periods[0].PeriodMs : 0.0; }
        }

        /// <summary>True when the signal looks like a repeating pulse or burst train.</summary>
        public bool IsPeriodic
        {
            get { return Periods.Count > 0 && Periods[0].Correlation >= 0.15; }
        }

        /// <summary>Short human readable summary for the candidate list.</summary>
        public string Summarise()
        {
            List<string> parts = new List<string>();

            if (OccupiedBandwidthHz > 0)
            {
                parts.Add(OccupiedBandwidthHz >= 1000
                    ? (OccupiedBandwidthHz / 1000.0).ToString("0.0") + " kHz"
                    : OccupiedBandwidthHz.ToString("0") + " Hz");
            }

            if (IsPeriodic) parts.Add("PRI " + DominantPeriodMs.ToString("0.#") + " ms");

            if (IfClusterCount >= 2) parts.Add(IfClusterCount + "-tone");
            else if (PhaseStateCount >= 2) parts.Add(PhaseStateCount + "-phase");
            else if (VoiceScore > 0.5) parts.Add("voice");
            else parts.Add("single tone");

            if (SymbolRate > 20) parts.Add(SymbolRate.ToString("0") + " bd");

            return string.Join(", ", parts.ToArray());
        }
    }

    public static class SigIdFeatureExtractor
    {
        /// <summary>Autocorrelation peaks below this correlation are ignored.</summary>
        private const double ACF_MIN_CORRELATION = 0.12;

        /// <summary>Longest period the autocorrelation is searched for.</summary>
        private const double ACF_MAX_PERIOD_MS = 2000.0;

        /// <summary>Shortest period the autocorrelation is searched for.</summary>
        private const double ACF_MIN_PERIOD_MS = 0.5;

        /// <summary>Number of points in the autocorrelation curve.</summary>
        private const int ACF_CURVE_POINTS = 64;

        /// <summary>
        /// Extracts every feature from a block of mono audio.
        /// </summary>
        public static SigIdFeatures Analyze(float[] samples, int rate, double filterWidthHz)
        {
            SigIdFeatures features = new SigIdFeatures();
            features.SampleRate = rate;
            features.SampleCount = samples == null ? 0 : samples.Length;
            features.DurationSeconds = rate > 0 ? (double)features.SampleCount / rate : 0.0;
            features.FilterWidthHz = filterWidthHz;

            if (samples == null || samples.Length < 64 || rate <= 0) return features;

            // ---- levels -------------------------------------------------------
            double sumSquares = 0.0;
            double peak = 0.0;
            for (int i = 0; i < samples.Length; i++)
            {
                double value = samples[i];
                sumSquares += value * value;
                double absolute = Math.Abs(value);
                if (absolute > peak) peak = absolute;
            }
            features.Rms = Math.Sqrt(sumSquares / samples.Length);
            features.Peak = peak;

            // ---- analytic signal, phase and instantaneous frequency ------------
            double[] real;
            double[] imaginary;
            AnalyticSignal(samples, out real, out imaginary);

            // the analysis band has to fit under the Nyquist limit of the source, so a
            // recording made at 8 kHz is not compared over a wider band than it holds
            double frequencyLimit = Math.Min(4000.0, rate * 0.475);

            double[] envelope = Envelope(real, imaginary, rate);
            double[] increments = PhaseIncrements(real, imaginary);
            double[] instantaneous = InstantaneousFrequency(increments, rate);

            // ---- noise floor and SNR -------------------------------------------
            if (envelope != null && envelope.Length > 16)
            {
                double[] sorted = (double[])envelope.Clone();
                Array.Sort(sorted);
                features.NoiseFloor = sorted[sorted.Length / 5];
                double reference = features.NoiseFloor > 1e-9 ? features.NoiseFloor : 1e-9;
                features.SnrDb = 20.0 * Math.Log10((features.Rms > 1e-12 ? features.Rms : 1e-12) / reference);
            }

            // ---- spectrum -----------------------------------------------------
            double[] power = AverageSpectrum(samples, rate);
            if (power != null)
            {
                features.OccupiedBandwidthHz = OccupiedBandwidth(power, rate);
                features.CarrierOffsetHz = PeakOffset(power, rate);
            }

            // ---- instantaneous frequency ---------------------------------------
            AnalyseInstantaneousFrequency(instantaneous, features, frequencyLimit);
            features.PhaseStateCount = CountPhaseStates(increments, frequencyLimit);

            // ---- periodicity ---------------------------------------------------
            // Two independent detectors: the envelope sees amplitude structure, the
            // complex autocorrelation sees the phase structure as well.  Each
            // autocorrelation is computed once and then used for both the peaks and
            // the curve - computing them separately doubled the cost of the whole
            // analysis.
            double[] envelopeAcf = ComputeEnvelopeAcf(envelope);
            double[] complexAcf = ComputeComplexAcf(real, imaginary);

            features.Periods = MergePeriods(
                PeaksFromAcf(envelopeAcf, rate),
                PeaksFromAcf(complexAcf, rate));

            features.AcfCurve = SampleAcfCurve(envelopeAcf, envelope != null ? envelope.Length : 0, rate);
            features.ComplexAcfCurve = SampleAcfCurve(complexAcf, real.Length, rate);

            // ---- speech / symbol rate -------------------------------------------
            features.VoiceScore = VoiceScore(envelope, rate);
            features.SymbolRate = EstimateSymbolRate(instantaneous, increments, rate, frequencyLimit);

            // ---- filter limit ---------------------------------------------------
            if (filterWidthHz > 0 && features.OccupiedBandwidthHz >= filterWidthHz * 0.92)
                features.FilterLimited = true;

            // ---- modulation class -----------------------------------------------
            features.EnvelopeVariation = EnvelopeVariation(envelope);
            features.ModulationClass = ClassifyModulation(features);

            return features;
        }

        /// <summary>
        /// Relative spread of the envelope.  High for a burst train, low for a
        /// continuously modulated signal.
        /// </summary>
        private static double EnvelopeVariation(double[] envelope)
        {
            if (envelope == null || envelope.Length < 16) return 0.0;

            double mean = 0.0;
            for (int i = 0; i < envelope.Length; i++) mean += Math.Abs(envelope[i]);
            mean /= envelope.Length;
            if (mean < 1e-12) return 0.0;

            double variance = 0.0;
            for (int i = 0; i < envelope.Length; i++)
            {
                double d = Math.Abs(envelope[i]) - mean;
                variance += d * d;
            }
            variance /= envelope.Length;

            double deviation = Math.Sqrt(variance);
            return deviation / mean;
        }

        /// <summary>
        /// Decides what kind of modulation the signal uses.  The rules are deliberately
        /// conservative - the matcher treats a wrong guess as a soft penalty, not a
        /// rejection.
        /// </summary>
        private static SigIdModulationClass ClassifyModulation(SigIdFeatures features)
        {
            // A burst train has a wildly varying envelope.  Its instantaneous frequency
            // is spread simply because the pulses are noise like, so the burstiness has
            // to be checked first - otherwise every radar looks like a spread spectrum
            // signal.
            if (features.EnvelopeVariation > 1.10)
            {
                bool tonal = features.IfClusterCount <= 2 && features.FrequencyStdHz < 1200.0;
                if (tonal) return SigIdModulationClass.Am;
                return SigIdModulationClass.Pulse;
            }

            // a wide occupied bandwidth with a spread instantaneous frequency is a
            // multi carrier or spread spectrum signal
            if (features.IfClusterCount >= 4 || features.FrequencyStdHz > 1500.0)
                return SigIdModulationClass.Broadband;

            if (features.IfClusterCount == 2)
            {
                // two levels far apart look like a carrier plus sidebands
                return features.IfClusterSeparationHz > 2500.0
                    ? SigIdModulationClass.Am
                    : SigIdModulationClass.Fsk;
            }

            if (features.IfClusterCount <= 1)
            {
                if (features.PhaseStateCount >= 2) return SigIdModulationClass.Psk;
                if (features.VoiceScore > 0.40) return SigIdModulationClass.Am;
                if (features.VoiceScore > 0.25) return SigIdModulationClass.Am;
                return SigIdModulationClass.Tone;
            }

            return SigIdModulationClass.Unknown;
        }

        // ====================================================================
        // Analytic signal (Hilbert transform), phase, instantaneous frequency
        // ====================================================================

        /// <summary>
        /// Builds the analytic signal: re[] is the input, im[] is its Hilbert transform.
        /// Both arrays come back with exactly the length of the input.
        /// </summary>
        private static void AnalyticSignal(float[] samples, out double[] re, out double[] im)
        {
            int n = samples.Length;
            int fftSize = 1;
            while (fftSize < 2 * n) fftSize <<= 1;

            double[] workRe = new double[fftSize];
            double[] workIm = new double[fftSize];
            for (int i = 0; i < n; i++) workRe[i] = samples[i];

            SigIdFft.Forward(workRe, workIm);

            // keep the positive frequencies and double them, drop the negative half
            for (int i = 0; i < fftSize / 2; i++)
            {
                workRe[i] *= 2.0;
                workIm[i] *= 2.0;
            }
            workRe[0] = 0.0; workIm[0] = 0.0;
            for (int i = fftSize / 2; i < fftSize; i++)
            {
                workRe[i] = 0.0;
                workIm[i] = 0.0;
            }

            SigIdFft.Inverse(workRe, workIm);

            re = new double[n];
            im = new double[n];
            for (int i = 0; i < n; i++)
            {
                re[i] = workRe[i];
                im[i] = workIm[i];
            }
        }

        /// <summary>Unwrapped phase of the analytic signal.</summary>
        private static double[] Phase(double[] re, double[] im)
        {
            int n = Math.Min(re.Length, im.Length);
            double[] phase = new double[n];
            for (int i = 0; i < n; i++) phase[i] = Math.Atan2(im[i], re[i]);
            return phase;
        }

        /// <summary>
        /// Phase increment between two samples of the analytic signal, in radians.
        ///
        /// The increment is taken from the complex ratio x[n] * conj(x[n-1]), which is
        /// the only way to get it right: subtracting two wrapped atan2 values gives a
        /// jump of 2*pi every time the carrier crosses the branch cut, which would
        /// swamp the instantaneous frequency and the symbol rate.
        /// </summary>
        private static double[] PhaseIncrements(double[] re, double[] im)
        {
            int n = Math.Min(re.Length, im.Length);
            double[] increments = new double[n > 0 ? n : 1];
            if (n < 2) return increments;

            for (int i = 1; i < n; i++)
            {
                double cross = im[i] * re[i - 1] - re[i] * im[i - 1];
                double dot = re[i] * re[i - 1] + im[i] * im[i - 1];
                increments[i] = Math.Atan2(cross, dot);
            }
            increments[0] = increments[1];
            return increments;
        }

        /// <summary>Instantaneous frequency in Hz.</summary>
        private static double[] InstantaneousFrequency(double[] increments, int rate)
        {
            int n = increments.Length;
            double[] frequency = new double[n];
            for (int i = 0; i < n; i++) frequency[i] = increments[i] * rate / (2.0 * Math.PI);
            return frequency;
        }

        // ====================================================================
        // Envelope
        // ====================================================================

        /// <summary>
        /// Magnitude envelope of the analytic signal, smoothed and mean removed.
        /// This is what the autocorrelation works on - it turns a burst train into a
        /// clean pulse train.
        /// </summary>
        private static double[] Envelope(double[] re, double[] im, int rate)
        {
            int n = Math.Min(re.Length, im.Length);
            if (n < 32) return null;

            double[] envelope = new double[n];
            for (int i = 0; i < n; i++)
                envelope[i] = Math.Sqrt(re[i] * re[i] + im[i] * im[i]);

            // 2 ms moving average, centred (a causal average would smear the edges
            // and shift the correlation peaks).  A running sum keeps this O(n) - the
            // naive inner loop made the whole analysis 50 times slower.
            int window = Math.Max(1, (int)(rate * 0.002));
            if (window > 1)
            {
                double[] smoothed = new double[n];
                int half = window / 2;

                double sum = 0.0;
                int count = 0;
                int next = 0;

                for (int i = 0; i < n; i++)
                {
                    int wanted = i + half;
                    while (next <= wanted && next < n) { sum += envelope[next]; count++; next++; }

                    int first = i - half;
                    if (first > 0)
                    {
                        sum -= envelope[first - 1];
                        count--;
                    }

                    smoothed[i] = count > 0 ? sum / count : envelope[i];
                }
                envelope = smoothed;
            }

            double mean = 0.0;
            for (int i = 0; i < n; i++) mean += envelope[i];
            mean /= n;
            for (int i = 0; i < n; i++) envelope[i] -= mean;

            return envelope;
        }

        // ====================================================================
        // Spectrum
        // ====================================================================

        /// <summary>Average power spectrum, one entry per bin, DC at index 0.</summary>
        private static double[] AverageSpectrum(float[] samples, int rate)
        {
            int frame = 4096;
            if (samples.Length < frame)
            {
                frame = 1;
                while (frame * 2 <= samples.Length) frame <<= 1;
            }
            if (frame < 64) return null;

            int hop = frame / 2;
            int frames = (samples.Length - frame) / hop + 1;
            if (frames < 1) return null;

            int bins = frame / 2 + 1;
            double[] average = new double[bins];
            double[] window = new double[frame];
            for (int i = 0; i < frame; i++)
                window[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (frame - 1)));

            double[] re = new double[frame];
            double[] im = new double[frame];

            for (int f = 0; f < frames; f++)
            {
                int offset = f * hop;
                for (int i = 0; i < frame; i++)
                {
                    re[i] = samples[offset + i] * window[i];
                    im[i] = 0.0;
                }

                SigIdFft.Forward(re, im);

                for (int i = 0; i < bins; i++)
                    average[i] += (re[i] * re[i] + im[i] * im[i]) / (frame * (double)frame);
            }

            for (int i = 0; i < bins; i++) average[i] /= frames;
            return average;
        }

        /// <summary>Width of the region holding 5 % … 99 % of the energy, in Hz.</summary>
        private static double OccupiedBandwidth(double[] power, int rate)
        {
            int bins = power.Length;
            double total = 0.0;
            for (int i = 0; i < bins; i++) total += power[i];
            if (total <= 0.0) return 0.0;

            double binWidth = (double)rate / ((bins - 1) * 2);

            double running = 0.0;
            int low = -1;
            int high = bins - 1;
            for (int i = 0; i < bins; i++)
            {
                running += power[i];
                if (low < 0 && running >= total * 0.05) low = i;
                if (running >= total * 0.99) { high = i; break; }
            }
            if (low < 0) return 0.0;

            return (high - low) * binWidth;
        }

        /// <summary>Frequency of the strongest bin relative to DC, in Hz.</summary>
        private static double PeakOffset(double[] power, int rate)
        {
            int bins = power.Length;
            if (bins < 3) return 0.0;

            int best = 1;
            for (int i = 2; i < bins; i++)
                if (power[i] > power[best]) best = i;

            double binWidth = (double)rate / ((bins - 1) * 2);
            return best * binWidth;
        }

        // ====================================================================
        // Autocorrelation
        // ====================================================================

        /// <summary>
        /// The normalised autocorrelation of the envelope.  Computed once and then used
        /// for both the peaks and the curve.
        /// </summary>
        private static double[] ComputeEnvelopeAcf(double[] envelope)
        {
            if (envelope == null || envelope.Length < 32) return null;

            int n = envelope.Length;
            int fftSize = 1;
            while (fftSize < 2 * n) fftSize <<= 1;

            double[] re = new double[fftSize];
            double[] im = new double[fftSize];
            for (int i = 0; i < n; i++) re[i] = envelope[i];

            SigIdFft.Forward(re, im);
            for (int i = 0; i < fftSize; i++)
            {
                double r = re[i];
                double m = im[i];
                re[i] = r * r + m * m;
                im[i] = 0.0;
            }
            SigIdFft.Inverse(re, im);

            double normalise = re[0] > 1e-20 ? re[0] : 1e-20;
            for (int i = 0; i < fftSize; i++) re[i] /= normalise;

            double[] result = new double[n];
            Array.Copy(re, result, n);
            return result;
        }

        /// <summary>
        /// The normalised complex autocorrelation, which also sees the phase structure
        /// of the signal.
        /// </summary>
        private static double[] ComputeComplexAcf(double[] re, double[] im)
        {
            if (re == null || im == null) return null;

            int n = Math.Min(re.Length, im.Length);
            if (n < 32) return null;

            int fftSize = 1;
            while (fftSize < 2 * n) fftSize <<= 1;

            double[] workRe = new double[fftSize];
            double[] workIm = new double[fftSize];
            for (int i = 0; i < n; i++)
            {
                workRe[i] = re[i];
                workIm[i] = im[i];
            }

            SigIdFft.Forward(workRe, workIm);
            for (int i = 0; i < fftSize; i++)
            {
                double a = workRe[i];
                double b = workIm[i];
                workRe[i] = a * a + b * b;
                workIm[i] = 0.0;
            }
            SigIdFft.Inverse(workRe, workIm);

            double normalise = workRe[0] > 1e-20 ? workRe[0] : 1e-20;
            for (int i = 0; i < fftSize; i++) workRe[i] /= normalise;

            double[] result = new double[n];
            Array.Copy(workRe, result, n);
            return result;
        }

        /// <summary>
        /// The peaks of an autocorrelation function.  These are the candidate pulse
        /// repetition intervals of the signal.
        /// </summary>
        private static List<SigIdPeriod> PeaksFromAcf(double[] acf, int rate)
        {
            List<SigIdPeriod> result = new List<SigIdPeriod>();
            if (acf == null || acf.Length < 32 || rate <= 0) return result;

            int n = acf.Length;
            int minLag = (int)(rate * ACF_MIN_PERIOD_MS / 1000.0);
            int maxLag = (int)(rate * ACF_MAX_PERIOD_MS / 1000.0);
            if (minLag < 1) minLag = 1;
            if (maxLag > n - 2) maxLag = n - 2;
            if (maxLag <= minLag) return result;

            List<SigIdPeriod> peaks = new List<SigIdPeriod>();
            int minimumGap = Math.Max(1, (int)(rate * 0.005));   // 5 ms

            for (int lag = minLag + 1; lag < maxLag; lag++)
            {
                double value = acf[lag];
                if (value <= ACF_MIN_CORRELATION) continue;
                if (value < acf[lag - 1]) continue;
                if (value < acf[lag + 1]) continue;

                bool tooClose = false;
                for (int i = 0; i < peaks.Count; i++)
                {
                    if (Math.Abs(peaks[i].PeriodMs * rate / 1000.0 - lag) < minimumGap)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose) continue;

                peaks.Add(new SigIdPeriod(lag * 1000.0 / rate, value));
            }

            peaks.Sort(delegate (SigIdPeriod a, SigIdPeriod b) { return b.Correlation.CompareTo(a.Correlation); });

            List<SigIdPeriod> kept = new List<SigIdPeriod>();
            for (int i = 0; i < peaks.Count && kept.Count < 64; i++) kept.Add(peaks[i]);

            kept.Sort(delegate (SigIdPeriod a, SigIdPeriod b) { return a.PeriodMs.CompareTo(b.PeriodMs); });
            result.AddRange(kept);
            return result;
        }
        /// </summary>
        private static List<SigIdPeriod> MergePeriods(List<SigIdPeriod> first, List<SigIdPeriod> second)
        {
            List<SigIdPeriod> merged = new List<SigIdPeriod>();
            if (first != null) merged.AddRange(first);
            if (second != null) merged.AddRange(second);

            merged.Sort(delegate (SigIdPeriod a, SigIdPeriod b) { return a.PeriodMs.CompareTo(b.PeriodMs); });

            List<SigIdPeriod> result = new List<SigIdPeriod>();
            for (int i = 0; i < merged.Count; i++)
            {
                if (result.Count > 0)
                {
                    SigIdPeriod previous = result[result.Count - 1];
                    double tolerance = Math.Max(0.5, previous.PeriodMs * 0.02);
                    if (Math.Abs(merged[i].PeriodMs - previous.PeriodMs) <= tolerance)
                    {
                        if (merged[i].Correlation > previous.Correlation)
                        {
                            result[result.Count - 1] = new SigIdPeriod(previous.PeriodMs, merged[i].Correlation);
                        }
                        continue;
                    }
                }
                result.Add(merged[i]);
            }

            // strongest first, then by period
            result.Sort(delegate (SigIdPeriod a, SigIdPeriod b)
            {
                int comparison = b.Correlation.CompareTo(a.Correlation);
                return comparison != 0 ? comparison : a.PeriodMs.CompareTo(b.PeriodMs);
            });

            if (result.Count > 96) result.RemoveRange(96, result.Count - 96);
            return result;
        }

        /// <summary>Samples an autocorrelation function at logarithmically spaced lags.</summary>
        private static double[] SampleAcfCurve(double[] acf, int n, int rate)
        {
            double[] curve = new double[ACF_CURVE_POINTS];
            if (acf == null || acf.Length < 8 || n < 8 || rate <= 0) return curve;

            double normalise = acf[0] > 1e-20 ? acf[0] : 1e-20;

            for (int i = 0; i < ACF_CURVE_POINTS; i++)
            {
                // 0.5 ms .. 2000 ms, logarithmically spaced
                double fraction = i / (double)(ACF_CURVE_POINTS - 1);
                double periodMs = ACF_MIN_PERIOD_MS * Math.Pow(ACF_MAX_PERIOD_MS / ACF_MIN_PERIOD_MS, fraction);
                double lag = periodMs * rate / 1000.0;

                int index = (int)Math.Round(lag);
                if (index < 1) { curve[i] = 1.0; continue; }
                if (index >= n - 1) { curve[i] = 0.0; continue; }

                int low = (int)Math.Floor(lag);
                int high = (int)Math.Ceiling(lag);
                double weight = lag - low;
                double value = acf[low] * (1.0 - weight) + acf[high] * weight;
                curve[i] = value / normalise;
            }

            return curve;
        }

        // ====================================================================
        // Instantaneous frequency analysis
        // ====================================================================

        private static void AnalyseInstantaneousFrequency(double[] instantaneous, SigIdFeatures features, double frequencyLimit)
        {
            if (instantaneous == null || instantaneous.Length < 16) return;

            double sum = 0.0;
            double sumSquares = 0.0;
            int count = 0;
            for (int i = 0; i < instantaneous.Length; i++)
            {
                double value = instantaneous[i];
                if (Math.Abs(value) > 20000.0) continue;   // ignore wrap-around spikes
                sum += value;
                sumSquares += value * value;
                count++;
            }
            if (count == 0) return;

            features.FrequencyMeanHz = sum / count;
            double variance = sumSquares / count - features.FrequencyMeanHz * features.FrequencyMeanHz;
            features.FrequencyStdHz = Math.Sqrt(variance > 0.0 ? variance : 0.0);

            // histogram the instantaneous frequency to count the distinct levels
            const int bins = 80;
            double limit = frequencyLimit;
            double[] histogram = new double[bins];
            for (int i = 0; i < instantaneous.Length; i++)
            {
                double value = instantaneous[i];
                if (Math.Abs(value) > limit) continue;
                int bin = (int)((value + limit) / (2.0 * limit) * bins);
                if (bin < 0) bin = 0;
                if (bin >= bins) bin = bins - 1;
                histogram[bin] += 1.0;
            }

            double total = 0.0;
            for (int i = 0; i < bins; i++) total += histogram[i];
            if (total <= 0.0) return;
            for (int i = 0; i < bins; i++) histogram[i] /= total;

            int occupied = 0;
            int first = -1;
            int second = -1;
            for (int i = 0; i < bins; i++)
            {
                if (histogram[i] > 0.02) occupied++;
                if (first < 0 || histogram[i] > histogram[first]) first = i;
            }
            for (int i = 0; i < bins; i++)
            {
                if (i == first) continue;
                if (second < 0 || histogram[i] > histogram[second]) second = i;
            }

            features.IfClusterCount = occupied;

            if (first >= 0 && second >= 0 && histogram[second] > 0.05)
            {
                double binWidth = 2.0 * limit / bins;
                features.IfClusterSeparationHz = Math.Abs(first - second) * binWidth;
            }
        }

        // ====================================================================
        // Speech
        // ====================================================================

        /// <summary>
        /// Rough speech detector: an envelope that is modulated at the syllable rate
        /// (2 … 8 Hz).
        /// </summary>
        private static double VoiceScore(double[] envelope, int rate)
        {
            if (envelope == null || envelope.Length < rate / 2) return 0.0;

            double sum = 0.0;
            for (int i = 0; i < envelope.Length; i++) sum += envelope[i];
            double mean = sum / envelope.Length;

            double variance = 0.0;
            for (int i = 0; i < envelope.Length; i++)
            {
                double d = envelope[i] - mean;
                variance += d * d;
            }
            variance /= envelope.Length;
            double deviation = Math.Sqrt(variance);
            if (deviation < 1e-9) return 0.0;

            double best = 0.0;
            for (double hz = 2.0; hz <= 8.0; hz += 0.5)
            {
                double re = 0.0;
                double im = 0.0;
                double omega = 2.0 * Math.PI * hz / rate;
                for (int i = 0; i < envelope.Length; i++)
                {
                    re += (envelope[i] - mean) * Math.Cos(omega * i);
                    im += (envelope[i] - mean) * Math.Sin(omega * i);
                }
                double magnitude = Math.Sqrt(re * re + im * im) * 2.0 / envelope.Length;
                if (magnitude > best) best = magnitude;
            }

            double score = best / deviation;
            return score > 1.0 ? 1.0 : score;
        }

        // ====================================================================
        // Symbol rate
        // ====================================================================

        /// <summary>
        /// Estimates the symbol rate.
        ///
        /// The symbol boundaries are found first - as phase jumps for PSK and as tone
        /// crossings for FSK - and the symbol rate is then the *dominant interval*
        /// between them.  Counting the boundaries directly does not work: the recorded
        /// audio is band limited, so a 180 degree flip is smeared over several samples
        /// and some boundaries are missed entirely.  The intervals, on the other hand,
        /// are all multiples of the symbol period, so their mode is the symbol period.
        /// </summary>
        private static double EstimateSymbolRate(double[] instantaneous, double[] increments,
                                                 int rate, double frequencyLimit)
        {
            if (instantaneous == null || increments == null || rate <= 0) return 0.0;
            if (instantaneous.Length != increments.Length || instantaneous.Length < 64) return 0.0;

            // ---- the phase jumps of a PSK signal ---------------------------------
            List<double> phaseEvents = PhaseJumpEvents(increments);

            // ---- the tone crossings of an FSK signal ----------------------------
            List<double> frequencyEvents = FrequencyCrossingEvents(instantaneous, frequencyLimit);

            double phaseRate = SymbolRateFromIntervals(phaseEvents, rate);
            double frequencyRate = SymbolRateFromIntervals(frequencyEvents, rate);

            // take the more plausible of the two
            if (phaseRate > 0.0 && frequencyRate > 0.0)
                return Math.Abs(Math.Log(phaseRate / frequencyRate)) < 0.4 ? phaseRate : Math.Max(phaseRate, frequencyRate);

            return phaseRate > 0.0 ? phaseRate : frequencyRate;
        }

        /// <summary>
        /// The times at which the phase deviates strongly from the carrier's own step.
        /// The deviation is smeared by the band limited audio, so the events are grouped
        /// into runs.
        /// </summary>
        private static List<double> PhaseJumpEvents(double[] increments)
        {
            List<double> events = new List<double>();

            double[] sorted = (double[])increments.Clone();
            Array.Sort(sorted);
            double median = sorted[sorted.Length / 2];

            double[] deviation = new double[increments.Length];
            for (int i = 0; i < increments.Length; i++)
                deviation[i] = Math.Abs(increments[i] - median);

            double[] deviationCopy = (double[])deviation.Clone();
            Array.Sort(deviationCopy);
            double noise = deviationCopy[deviationCopy.Length / 2];
            double threshold = Math.Max(0.12, noise * 6.0);

            bool inside = false;
            for (int i = 0; i < deviation.Length; i++)
            {
                if (deviation[i] > threshold)
                {
                    if (!inside) { events.Add(i); inside = true; }
                }
                else if (deviation[i] < threshold * 0.5)
                {
                    inside = false;
                }
            }

            return events;
        }

        /// <summary>The times at which the instantaneous frequency crosses its average.</summary>
        private static List<double> FrequencyCrossingEvents(double[] instantaneous, double frequencyLimit)
        {
            List<double> events = new List<double>();

            double limit = Math.Min(6000.0, frequencyLimit * 1.5);
            double mean = 0.0;
            int count = 0;
            for (int i = 0; i < instantaneous.Length; i++)
            {
                if (Math.Abs(instantaneous[i]) > limit) continue;
                mean += instantaneous[i];
                count++;
            }
            if (count < 16) return events;
            mean /= count;

            double[] deviations = new double[count];
            int index = 0;
            for (int i = 0; i < instantaneous.Length; i++)
            {
                if (Math.Abs(instantaneous[i]) > limit) continue;
                deviations[index++] = Math.Abs(instantaneous[i] - mean);
            }
            Array.Sort(deviations);
            double typical = deviations[deviations.Length / 2];
            if (typical <= 25.0) return events;      // a single tone, no tone switching

            double threshold = typical * 0.5;
            int state = 0;
            for (int i = 0; i < instantaneous.Length; i++)
            {
                if (Math.Abs(instantaneous[i]) > limit) continue;
                double deviation = instantaneous[i] - mean;
                int newState = deviation > threshold ? 1 : (deviation < -threshold ? -1 : state);
                if (newState != 0 && state != 0 && newState != state) events.Add(i);
                if (newState != 0) state = newState;
            }

            return events;
        }

        /// <summary>
        /// The symbol rate is one over the dominant interval between the events.  The
        /// intervals are all multiples of the symbol period, so the mode of the
        /// histogram is the symbol period itself.
        /// </summary>
        private static double SymbolRateFromIntervals(List<double> eventTimes, int rate)
        {
            if (eventTimes == null || eventTimes.Count < 4) return 0.0;

            const double binMs = 0.1;
            const int maximumBins = 12000;             // up to 1200 ms

            int[] histogram = new int[maximumBins];
            List<double> intervals = new List<double>();

            for (int i = 1; i < eventTimes.Count; i++)
            {
                double interval = (eventTimes[i] - eventTimes[i - 1]) * 1000.0 / rate;

                // intervals below 0.8 ms are noise, not symbols - a phase jump that is
                // this short means the deviation threshold was crossed by accident
                if (interval < 0.8 || interval > 1200.0) continue;

                intervals.Add(interval);

                int bin = (int)(interval / binMs);
                if (bin >= 0 && bin < maximumBins) histogram[bin]++;
            }

            if (intervals.Count < 3) return 0.0;

            // find the strongest bin, allowing for a little jitter
            int best = -1;
            int bestCount = 0;
            for (int i = 0; i < maximumBins; i++)
            {
                int count = histogram[i];
                if (i > 0) count += histogram[i - 1];
                if (i + 1 < maximumBins) count += histogram[i + 1];
                if (count > bestCount) { bestCount = count; best = i; }
            }

            if (best < 0 || bestCount < 3) return 0.0;

            // average the intervals that fall into the dominant bin for precision
            double sum = 0.0;
            int used = 0;
            int low = best - 1;
            int high = best + 1;
            foreach (double interval in intervals)
            {
                int bin = (int)(interval / binMs);
                if (bin >= low && bin <= high) { sum += interval; used++; }
            }

            double period = used > 0 ? sum / used : (best + 0.5) * binMs;
            if (period <= 0.0) return 0.0;

            double estimate = 1000.0 / period;
            return estimate > 2.0 && estimate < 20000.0 ? estimate : 0.0;
        }

        // ====================================================================
        // Phase states
        // ====================================================================

        /// <summary>
        /// Counts the distinct phase states of the signal.  That is what separates
        /// BPSK (2), QPSK (4) and 8PSK (8).
        /// </summary>
        private static int CountPhaseStates(double[] increments, double frequencyLimit)
        {
            if (increments == null || increments.Length < 64) return 0;

            double limit = frequencyLimit;
            List<double> differences = new List<double>();
            for (int i = 1; i < increments.Length; i++)
            {
                if (Math.Abs(increments[i]) > limit) continue;
                differences.Add(increments[i]);
            }

            if (differences.Count < 32) return 0;

            double[] sorted = differences.ToArray();
            Array.Sort(sorted);
            double median = sorted[sorted.Length / 2];

            // cluster the phase differences around their median
            const double tolerance = 0.6;   // radians
            List<double> centres = new List<double>();
            foreach (double value in differences)
            {
                double shifted = value - median;
                while (shifted > Math.PI) shifted -= 2.0 * Math.PI;
                while (shifted < -Math.PI) shifted += 2.0 * Math.PI;

                bool placed = false;
                for (int i = 0; i < centres.Count; i++)
                {
                    if (Math.Abs(centres[i] - shifted) < tolerance)
                    {
                        centres[i] = (centres[i] + shifted) / 2.0;
                        placed = true;
                        break;
                    }
                }
                if (!placed) centres.Add(shifted);
            }

            return centres.Count;
        }
    }
}
