/*  SigIdAudioTap.cs

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
// SigID - managed side of the native RX audio tap.                                            //
//                                                                                              //
// The native tap (ChannelMaster/sigid_mod.c) sits in the WDSP pipe next to the fldigi and    //
// WSJT-X taps, which means the audio it carries has NOT been through the VST3 RX chain.      //
// That is the whole point: a VST equaliser, limiter or delay would change the signal the     //
// analyser measures.                                                                           //
//                                                                                              //
// This class only moves samples.  The feature extraction lives in SigIdAnalyzer.             //
//============================================================================================//

using System;
using System.Collections.Generic;

namespace Thetis
{
    /// <summary>Result of a short capture from the SigID audio tap.</summary>
    public sealed class SigIdCaptureResult
    {
        /// <summary>Number of samples captured.</summary>
        public int SampleCount;

        /// <summary>Sample rate the tap delivered at.</summary>
        public int Rate;

        /// <summary>Peak absolute sample value (0 … 1).</summary>
        public float Peak;

        /// <summary>Root mean square level (0 … 1).</summary>
        public float Rms;

        /// <summary>Number of times the native FIFO overflowed during the capture.</summary>
        public long Overruns;

        /// <summary>How many times the native hot path ran.</summary>
        public long Blocks;

        /// <summary>Total samples the native resampler has produced since startup.</summary>
        public long NativeSamples;

        /// <summary>Hot path calls that produced no samples at all.</summary>
        public long ZeroBlocks;

        /// <summary>Error text, empty when everything worked.</summary>
        public string Error = string.Empty;

        /// <summary>Frequency measured in the self test (Hz).</summary>
        public double MeasuredHz;

        /// <summary>Frequency the self test injected (Hz).</summary>
        public double ExpectedHz;

        /// <summary>Duration of the capture in seconds.</summary>
        public double Seconds
        {
            get { return Rate > 0 ? (double)SampleCount / Rate : 0.0; }
        }

        public string Describe()
        {
            if (!string.IsNullOrEmpty(Error)) return "error: " + Error;

            string text = SampleCount + " samples (" + Seconds.ToString("0.00") + " s @ " + Rate + " Hz), " +
                          "peak " + Peak.ToString("0.0000") + ", rms " + Rms.ToString("0.0000") +
                          (Overruns > 0 ? ", " + Overruns + " overrun(s)" : string.Empty);

            text += " [native blocks " + Blocks + ", samples " + NativeSamples +
                    (ZeroBlocks > 0 ? ", empty " + ZeroBlocks : string.Empty) + "]";
            return text;
        }
    }

    public static class SigIdAudioTap
    {
        /// <summary>Receiver index for RX1.</summary>
        public const int RX1 = 0;

        /// <summary>Receiver index for RX2.</summary>
        public const int RX2 = 1;

        /// <summary>
        /// Last error seen while talking to the native module.  Empty when everything is
        /// fine.  The native calls are wrapped because the module lives in ChannelMaster.dll
        /// and must not be able to take the application down.
        /// </summary>
        public static string LastError { get; private set; } = string.Empty;

        /// <summary>Analysis rate of the tap in Hz (asked from the native module).</summary>
        public static int Rate
        {
            get
            {
                try { return cmaster.SigidRxRate(); }
                catch (Exception ex) { LastError = ex.Message; return 0; }
            }
        }

        /// <summary>Enables or disables the tap for a receiver.</summary>
        public static void Enable(int rx, bool enabled)
        {
            try { cmaster.SigidSetRxEnable(rx, enabled ? 1 : 0); }
            catch (Exception ex) { LastError = ex.Message; }
        }

        /// <summary>True when the tap is running for that receiver.</summary>
        public static bool IsEnabled(int rx)
        {
            try { return cmaster.SigidGetRxEnable(rx) != 0; }
            catch (Exception ex) { LastError = ex.Message; return false; }
        }

        /// <summary>Number of samples currently queued in the native FIFO.</summary>
        public static int FifoLevel(int rx)
        {
            try { return cmaster.SigidRxFifoLevel(rx); }
            catch (Exception ex) { LastError = ex.Message; return 0; }
        }

        /// <summary>Number of times the native FIFO has overflowed.</summary>
        public static long OverrunCount(int rx)
        {
            try { return cmaster.SigidRxOverrunCount(rx); }
            catch (Exception ex) { LastError = ex.Message; return 0; }
        }

        /// <summary>How many times the native hot path has run for that receiver.</summary>
        public static long BlockCount(int rx)
        {
            try { return cmaster.SigidRxBlockCount(rx); }
            catch (Exception ex) { LastError = ex.Message; return 0; }
        }

        /// <summary>Total samples the native resampler has produced.</summary>
        public static long NativeSampleCount(int rx)
        {
            try { return cmaster.SigidRxSampleCount(rx); }
            catch (Exception ex) { LastError = ex.Message; return 0; }
        }

        /// <summary>Hot path calls that produced no samples at all.</summary>
        public static long ZeroBlockCount(int rx)
        {
            try { return cmaster.SigidRxZeroCount(rx); }
            catch (Exception ex) { LastError = ex.Message; return 0; }
        }

        /// <summary>Discards any queued audio.</summary>
        public static void Flush(int rx)
        {
            try { cmaster.SigidFlushRx(rx); }
            catch (Exception ex) { LastError = ex.Message; }
        }

        /// <summary>Drains up to <paramref name="maxCount"/> samples from the tap.</summary>
        public static int Drain(int rx, float[] buffer, int maxCount)
        {
            if (buffer == null || maxCount <= 0) return 0;
            try { return cmaster.SigidDrainRx(rx, buffer, maxCount); }
            catch (Exception ex) { LastError = ex.Message; return 0; }
        }

        /// <summary>
        /// Pushes a synthetic tone through the real native chain (de-interleave ->
        /// resample -> FIFO) and verifies what comes out.  This proves the DSP path
        /// works even when no radio is connected, which is the case on a bench with the
        /// radio switched off.
        /// </summary>
        public static SigIdCaptureResult SelfTest(int rx, int toneHz, int milliseconds)
        {
            SigIdCaptureResult result = new SigIdCaptureResult();

            const int inputRate = 48000;
            const int blockFrames = 64;

            try
            {
                result.Rate = Rate;
                if (result.Rate <= 0)
                {
                    result.Error = string.IsNullOrEmpty(LastError)
                        ? "the native module did not report a sample rate"
                        : LastError;
                    return result;
                }

                cmaster.SigidCreate();
                Flush(rx);
                Enable(rx, true);

                long blocksBefore = BlockCount(rx);
                long samplesBefore = NativeSampleCount(rx);
                long zerosBefore = ZeroBlockCount(rx);

                // one second of a sine, pushed in 64 frame blocks like the live path
                double[] block = new double[blockFrames * 2];
                int totalFrames = inputRate;
                double phase = 0.0;
                double step = 2.0 * Math.PI * toneHz / inputRate;

                for (int produced = 0; produced < totalFrames; produced += blockFrames)
                {
                    for (int i = 0; i < blockFrames; i++)
                    {
                        double sample = 0.5 * Math.Sin(phase);
                        phase += step;
                        block[2 * i] = sample;       // L
                        block[2 * i + 1] = 0.0;      // R (must be ignored)
                    }

                    cmaster.SigidProcessRxBlock(rx, block, blockFrames, inputRate);
                }

                // drain what we pushed
                float[] buffer = new float[8192];
                List<float> drained = new List<float>();
                System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
                while (drained.Count < totalFrames / 2 && clock.ElapsedMilliseconds < milliseconds + 2000)
                {
                    int got = Drain(rx, buffer, buffer.Length);
                    if (got <= 0) { System.Threading.Thread.Sleep(5); continue; }
                    for (int i = 0; i < got; i++) drained.Add(buffer[i]);
                }

                Enable(rx, false);

                result.SampleCount = drained.Count;
                result.Blocks = BlockCount(rx) - blocksBefore;
                result.NativeSamples = NativeSampleCount(rx) - samplesBefore;
                result.ZeroBlocks = ZeroBlockCount(rx) - zerosBefore;

                if (drained.Count == 0)
                {
                    result.Error = "the native chain produced no samples";
                    return result;
                }

                // amplitude (ignore the filter transient at the start)
                int start = Math.Min(drained.Count / 4, 480);
                float peak = 0.0f;
                double sumSquares = 0.0;
                for (int i = start; i < drained.Count; i++)
                {
                    float absolute = Math.Abs(drained[i]);
                    if (absolute > peak) peak = absolute;
                    sumSquares += (double)drained[i] * drained[i];
                }
                int analysed = drained.Count - start;
                result.Peak = peak;
                result.Rms = analysed > 0 ? (float)Math.Sqrt(sumSquares / analysed) : 0.0f;

                // frequency from zero crossings
                int crossings = 0;
                for (int i = start + 1; i < drained.Count; i++)
                {
                    if ((drained[i - 1] < 0.0f && drained[i] >= 0.0f) ||
                        (drained[i - 1] >= 0.0f && drained[i] < 0.0f)) crossings++;
                }
                double measuredHz = analysed > 0 ? crossings * result.Rate / (2.0 * analysed) : 0.0;
                result.MeasuredHz = measuredHz;
                result.ExpectedHz = toneHz;

                return result;
            }
            catch (Exception ex)
            {
                result.Error = ex.Message;
                return result;
            }
        }

        /// <summary>
        /// Captures a short burst and measures it.  Used by the self test in the Digi Id
        /// window: it proves the whole native chain (pipe tap -> resampler -> FIFO -> drain)
        /// is alive without needing a radio to be transmitting.
        /// </summary>
        public static SigIdCaptureResult Capture(int rx, int milliseconds)
        {
            SigIdCaptureResult result = new SigIdCaptureResult();
            result.Rate = Rate;

            if (result.Rate <= 0)
            {
                result.Error = string.IsNullOrEmpty(LastError)
                    ? "the native module did not report a sample rate"
                    : LastError;
                return result;
            }

            Enable(rx, true);
            Flush(rx);

            long blocksBefore = BlockCount(rx);
            long samplesBefore = NativeSampleCount(rx);
            long zerosBefore = ZeroBlockCount(rx);

            int wanted = result.Rate * Math.Max(1, milliseconds) / 1000;
            float[] buffer = new float[8192];

            double sumSquares = 0.0;
            float peak = 0.0f;
            int total = 0;

            System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
            while (total < wanted && clock.ElapsedMilliseconds < milliseconds + 500)
            {
                int got = Drain(rx, buffer, buffer.Length);
                if (got <= 0)
                {
                    System.Threading.Thread.Sleep(10);
                    continue;
                }

                for (int i = 0; i < got; i++)
                {
                    float sample = buffer[i];
                    float absolute = sample < 0 ? -sample : sample;
                    if (absolute > peak) peak = absolute;
                    sumSquares += (double)sample * sample;
                }

                total += got;
            }

            Enable(rx, false);

            result.SampleCount = total;
            result.Peak = peak;
            result.Rms = total > 0 ? (float)Math.Sqrt(sumSquares / total) : 0.0f;
            result.Overruns = OverrunCount(rx);
            result.Blocks = BlockCount(rx) - blocksBefore;
            result.NativeSamples = NativeSampleCount(rx) - samplesBefore;
            result.ZeroBlocks = ZeroBlockCount(rx) - zerosBefore;
            return result;
        }
    }
}
