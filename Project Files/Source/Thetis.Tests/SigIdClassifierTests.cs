/*  SigIdClassifierTests.cs

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
using System.Globalization;
using System.IO;
using System.Linq;
using Xunit;

namespace Thetis.Tests
{
    /// <summary>
    /// Trains and validates the modulation classifier on the W1HKJ "Sights and Sounds"
    /// collection, which holds 63 clean recordings of well known modes with the mode and
    /// its parameters in the file name.
    ///
    /// It needs the collection unpacked somewhere:
    ///   set SIGID_TEST_MODES=&lt;folder with the wav files&gt;
    /// </summary>
    [Collection("SigId")]
    public class SigIdClassifierTests
    {
        private static string ModesFolder
        {
            get
            {
                string path = Environment.GetEnvironmentVariable("SIGID_TEST_MODES");
                return string.IsNullOrWhiteSpace(path) ? null : path;
            }
        }

        /// <summary>
        /// Works out the expected modulation family from the file name, and the expected
        /// symbol rate when the name carries one.
        /// </summary>
        internal static string ExpectedClass(string fileName)
        {
            string name = Path.GetFileNameWithoutExtension(fileName).ToUpperInvariant();

            if (name.Contains("BPSK") || name.Contains("QPSK") || name.StartsWith("PSK")) return "Psk";
            if (name.StartsWith("CW")) return "Tone";
            if (name.Contains("MT63")) return "Am";
            if (name.Contains("MFSK") || name.Contains("DOMEX") || name.Contains("CTSTIA") ||
                name.Contains("THOR") || name.Contains("THROB") || name.Contains("FSQ") ||
                name.Contains("IFKP") || name.Contains("BAUDOT") || name.Contains("FELD") ||
                name.Contains("FSKHELL") || name.Contains("OLIVIA")) return "Fsk";
            return null;
        }

        /// <summary>
        /// The symbol rate the file name promises, or 0 when it does not say.
        /// Only the modes where the number really is a baud rate are used:
        /// BPSK31 / QPSK125 / Thor22 / Baudot45 are symbol rates, while MFSK16 and
        /// DomEX8 name the number of tones or characters, not the baud rate.
        /// </summary>
        internal static double ExpectedSymbolRate(string fileName)
        {
            string name = Path.GetFileNameWithoutExtension(fileName).ToUpperInvariant();

            // BPSK31 / QPSK125 / PSK-125R -> 31 / 125 baud
            if (name.StartsWith("BPSK") || name.StartsWith("QPSK") || name.StartsWith("PSK"))
            {
                int marker = name.IndexOf("PSK");
                string tail = name.Substring(marker + 3);
                string digits = new string(tail.TakeWhile(char.IsDigit).ToArray());
                double value;
                if (digits.Length > 0 && double.TryParse(digits, out value)) return value;
            }

            // Thor22 -> 22 baud (Thor is a multi tone FSK with a defined symbol rate)
            if (name.StartsWith("THOR") && !name.StartsWith("THROB"))
            {
                string digits = new string(name.Substring(4).TakeWhile(char.IsDigit).ToArray());
                double value;
                if (digits.Length > 0 && double.TryParse(digits, out value)) return value;
            }

            // Baudot45-170 -> 45 baud, 170 Hz shift
            if (name.StartsWith("BAUDOT"))
            {
                string digits = new string(name.Substring(6).TakeWhile(char.IsDigit).ToArray());
                double value;
                if (digits.Length > 0 && double.TryParse(digits, out value)) return value;
            }

            return 0.0;
        }

        private sealed class Sample
        {
            public string File;
            public string ExpectedClass;
            public double ExpectedSymbolRate;
            public double[] Vector;
            public SigIdFeatures Features;
        }

        [Fact]
        public void The_classifier_learns_the_modes_from_the_labelled_recordings()
        {
            string folder = ModesFolder;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;

            List<Sample> samples = new List<Sample>();
            foreach (string file in Directory.GetFiles(folder, "*.wav"))
            {
                string expected = ExpectedClass(file);
                if (expected == null) continue;

                float[] audio;
                int rate;
                if (!SigIdWavReader.TryRead(file, out audio, out rate)) continue;


                SigIdFeatures features = SigIdFeatureExtractor.Analyze(audio, rate, 0.0);

                Sample sample = new Sample();
                sample.File = Path.GetFileName(file);
                sample.ExpectedClass = expected;
                sample.ExpectedSymbolRate = ExpectedSymbolRate(file);
                sample.Features = features;
                sample.Vector = SigIdClassifier.Vector(features);
                samples.Add(sample);
            }

            if (samples.Count < 40) return;
            System.Console.WriteLine("[sigid] labelled mode recordings: " + samples.Count);

            // ---- leave one out ------------------------------------------------
            int correct = 0;
            int symbolCorrect = 0;
            int symbolTested = 0;
            List<string> misses = new List<string>();

            for (int i = 0; i < samples.Count; i++)
            {
                // train without the sample under test
                List<KeyValuePair<string, double[]>> training = new List<KeyValuePair<string, double[]>>();
                for (int k = 0; k < samples.Count; k++)
                {
                    if (k == i) continue;
                    training.Add(new KeyValuePair<string, double[]>(samples[k].ExpectedClass, samples[k].Vector));
                }

                SigIdClassifier model = new SigIdClassifier();
                model.Train(training);

                double confidence;
                SigIdModulationClass predicted = model.Predict(samples[i].Vector, out confidence);
                string predictedName = predicted.ToString();

                if (string.Equals(predictedName, samples[i].ExpectedClass, StringComparison.OrdinalIgnoreCase)) correct++;
                else if (misses.Count < 12)
                    misses.Add(Path.GetFileNameWithoutExtension(samples[i].File) + ": expected " +
                               samples[i].ExpectedClass + ", got " + predictedName);

                // symbol rate check
                if (samples[i].ExpectedSymbolRate > 0)
                {
                    symbolTested++;
                    double measured = samples[i].Features.SymbolRate;
                    if (measured > 0 && Math.Abs(Math.Log(measured / samples[i].ExpectedSymbolRate)) < 0.35)
                        symbolCorrect++;
                    else if (misses.Count < 20)
                        misses.Add("baud " + Path.GetFileNameWithoutExtension(samples[i].File) + ": expected " +
                                   samples[i].ExpectedSymbolRate + " bd, measured " +
                                   measured.ToString("0") + " bd");
                }
            }

            System.Console.WriteLine("[sigid] leave-one-out family accuracy: " + correct + "/" + samples.Count +
                                    " (" + (100.0 * correct / samples.Count).ToString("0.0") + " %)");
            System.Console.WriteLine("[sigid] symbol rate within 35 %: " + symbolCorrect + "/" + symbolTested);
            foreach (string miss in misses) System.Console.WriteLine("   " + miss);

            Assert.True(correct > samples.Count / 2,
                "family accuracy was only " + correct + "/" + samples.Count);
        }
    }

    /// <summary>Minimal 16 bit PCM WAV reader shared by the SigID tests.</summary>
    internal static class SigIdWavReader
    {
        public static bool TryRead(string path, out float[] samples, out int rate)
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

                if (dataOffset < 0 || rate <= 0) return false;
                if (bitsPerSample != 16) return false;

                int count = Math.Min(dataLength / 2, bytes.Length - dataOffset) / Math.Max(1, channels);
                float[] mono = new float[count];
                for (int i = 0; i < count; i++)
                    mono[i] = BitConverter.ToInt16(bytes, dataOffset + i * channels * 2) / 32768.0f;

                samples = mono;
                return samples.Length > 0;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
