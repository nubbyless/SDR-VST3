/*  SigIdClassifier.cs

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
// SigID - the modulation classifier.                                                            //
//                                                                                              //
// The hand written rules in SigIdFeatureExtractor only got the modulation family right for  //
// 24 % of the database's reference recordings, so the class is now learned instead.              //
//                                                                                              //
// The training set is the W1HKJ "Sights and Sounds" collection (https://www.w1hkj.org/modes/), //
// which holds 63 clean, isolated recordings of well known modes with the parameters in the    //
// file name: BPSK31, QPSK125, MFSK16, DomEX8, Thor22, Olivia8-500, Baudot45-170 ...            //
// That gives exact labels AND a known symbol rate to check the estimator against.               //
//                                                                                              //
// The model is a nearest centroid classifier over a small, normalised feature vector.  It is    //
// built once, shipped as classifier.json and used by the matcher.                                //
//============================================================================================//

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace Thetis
{
    /// <summary>A trained class: the centroid of the feature vectors and how many samples went in.</summary>
    public sealed class SigIdClassifierClass
    {
        public string Name = string.Empty;
        public double[] Centroid = new double[0];
        public int Count;
    }

    public sealed class SigIdClassifier
    {
        /// <summary>File name of the trained model inside the SigId folder.</summary>
        public const string FILE_NAME = "classifier.json";

        /// <summary>Number of features in the vector.</summary>
        public const int FEATURE_COUNT = 18;

        /// <summary>Per weight, per class centroid.</summary>
        public List<SigIdClassifierClass> Classes = new List<SigIdClassifierClass>();

        public string BuiltUtc = string.Empty;

        /// <summary>Full path of the model file.</summary>
        public static string ModelPath
        {
            get { return Path.Combine(SigIdPaths.Root, FILE_NAME); }
        }

        /// <summary>
        /// Builds the normalised feature vector of a signal.  Everything is scaled so
        /// that no single dimension dominates the distance.
        /// </summary>
        public static double[] Vector(SigIdFeatures features)
        {
            double[] vector = new double[FEATURE_COUNT];
            if (features == null) return vector;

            int index = 0;

            // log bandwidth - 20 Hz to 100 kHz
            vector[index++] = features.OccupiedBandwidthHz > 0
                ? Math.Log(features.OccupiedBandwidthHz) / 5.0
                : 0.0;

            // log symbol rate - 10 to 10000 baud
            vector[index++] = features.SymbolRate > 0
                ? Math.Log(features.SymbolRate) / 5.0
                : 0.0;

            // the number of distinct frequency levels, scaled to 0 .. 1
            vector[index++] = Math.Min(1.0, features.IfClusterCount / 8.0);

            // the separation of the two strongest levels, log scaled
            vector[index++] = features.IfClusterSeparationHz > 0
                ? Math.Min(1.0, Math.Log(features.IfClusterSeparationHz + 1.0) / 6.0)
                : 0.0;

            // the number of phase states
            vector[index++] = Math.Min(1.0, features.PhaseStateCount / 8.0);

            // burstiness
            vector[index++] = Math.Min(1.0, features.EnvelopeVariation / 2.0);

            // how much it looks like speech
            vector[index++] = features.VoiceScore;

            // the spread of the instantaneous frequency
            vector[index++] = Math.Min(1.0, features.FrequencyStdHz / 2000.0);

            // the shape of the autocorrelation, 10 log spaced points
            double[] curve = features.AcfCurve != null && features.AcfCurve.Length > 0
                ? features.AcfCurve
                : features.ComplexAcfCurve;
            for (int i = 0; i < 10 && index < FEATURE_COUNT; i++)
            {
                double value = 0.0;
                if (curve != null && curve.Length > 0)
                {
                    int position = (int)(i * (curve.Length - 1) / 9.0);
                    value = curve[position];
                }
                vector[index++] = value;
            }

            // pad the rest
            while (index < FEATURE_COUNT) vector[index++] = 0.0;

            return vector;
        }

        /// <summary>
        /// Predicts the modulation class of a feature vector.  Returns Unknown when the
        /// model has not been trained or the signal is nowhere near any class.
        /// </summary>
        public SigIdModulationClass Predict(double[] vector, out double confidence)
        {
            confidence = 0.0;
            if (vector == null || Classes.Count == 0) return SigIdModulationClass.Unknown;

            string bestName = null;
            double bestDistance = double.MaxValue;
            double secondDistance = double.MaxValue;

            foreach (SigIdClassifierClass candidate in Classes)
            {
                if (candidate.Centroid == null || candidate.Centroid.Length != vector.Length) continue;

                double sum = 0.0;
                for (int i = 0; i < vector.Length; i++)
                {
                    double difference = vector[i] - candidate.Centroid[i];
                    sum += difference * difference;
                }

                double distance = Math.Sqrt(sum);
                if (distance < bestDistance)
                {
                    secondDistance = bestDistance;
                    bestDistance = distance;
                    bestName = candidate.Name;
                }
                else if (distance < secondDistance)
                {
                    secondDistance = distance;
                }
            }

            if (bestName == null) return SigIdModulationClass.Unknown;

            // how much better is the winner than the runner up
            if (secondDistance < double.MaxValue && secondDistance > 1e-9)
            {
                double margin = 1.0 - bestDistance / secondDistance;
                confidence = Math.Max(0.0, Math.Min(1.0, margin * 2.0));
            }
            else
            {
                confidence = 1.0;
            }

            try { return (SigIdModulationClass)Enum.Parse(typeof(SigIdModulationClass), bestName); }
            catch (Exception) { return SigIdModulationClass.Unknown; }
        }

        /// <summary>Trains the model from labelled feature vectors.</summary>
        public void Train(IEnumerable<KeyValuePair<string, double[]>> samples)
        {
            Classes.Clear();

            Dictionary<string, List<double[]>> grouped = new Dictionary<string, List<double[]>>();
            foreach (KeyValuePair<string, double[]> sample in samples)
            {
                List<double[]> list;
                if (!grouped.TryGetValue(sample.Key, out list))
                {
                    list = new List<double[]>();
                    grouped[sample.Key] = list;
                }
                list.Add(sample.Value);
            }

            foreach (KeyValuePair<string, List<double[]>> group in grouped)
            {
                SigIdClassifierClass entry = new SigIdClassifierClass();
                entry.Name = group.Key;
                entry.Count = group.Value.Count;
                entry.Centroid = new double[FEATURE_COUNT];

                foreach (double[] vector in group.Value)
                {
                    for (int i = 0; i < FEATURE_COUNT && i < vector.Length; i++)
                        entry.Centroid[i] += vector[i];
                }
                for (int i = 0; i < FEATURE_COUNT; i++)
                    entry.Centroid[i] /= group.Value.Count;

                Classes.Add(entry);
            }
        }

        /// <summary>Reads the trained model.  Returns null when it is not available.</summary>
        public static SigIdClassifier Load()
        {
            // the shipped model sits next to the executable, the one built from the
            // installed database sits in the SigId folder
            string[] candidates =
            {
                ModelPath,
                Path.Combine(AppContext.BaseDirectory, FILE_NAME)
            };

            foreach (string path in candidates)
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    string json = File.ReadAllText(path);
                    if (string.IsNullOrWhiteSpace(json)) continue;
                    SigIdClassifier model = JsonConvert.DeserializeObject<SigIdClassifier>(json);
                    if (model == null || model.Classes.Count == 0) continue;
                    return model;
                }
                catch (Exception)
                {
                    // try the next one
                }
            }

            return null;
        }

        /// <summary>Writes the trained model next to the database.</summary>
        public void Save()
        {
            try
            {
                if (!SigIdPaths.EnsureRoot()) return;
                BuiltUtc = DateTime.UtcNow.ToString("o");
                File.WriteAllText(ModelPath, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception)
            {
                // the model is a convenience, never fatal
            }
        }

        /// <summary>Writes the trained model to an explicit path.</summary>
        public void Save2(string path)
        {
            try
            {
                BuiltUtc = DateTime.UtcNow.ToString("o");
                File.WriteAllText(path, JsonConvert.SerializeObject(this, Formatting.Indented));
            }
            catch (Exception)
            {
                // the model is a convenience, never fatal
            }
        }
    }
}
