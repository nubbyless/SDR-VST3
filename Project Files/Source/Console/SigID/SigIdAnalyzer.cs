/*  SigIdAnalyzer.cs

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
// SigID - the realtime analysis thread.                                                         //
//                                                                                              //
// The thread drains the native RX tap, keeps a rolling buffer of the demodulated audio and   //
// re-runs the feature extraction and the matcher once a second.  The result is published as an //
// immutable snapshot, which the panadapter renderer and the Digi Id window read.                //
//                                                                                              //
// The thread does no work at all while identification is switched off or the database is not   //
// installed.                                                                                   //
//============================================================================================//

using System;
using System.Collections.Generic;
using System.Threading;

namespace Thetis
{
    /// <summary>The published result of one analysis cycle.</summary>
    public sealed class SigIdAnalysisSnapshot
    {
        /// <summary>When the analysis ran.</summary>
        public DateTime Timestamp = DateTime.UtcNow;

        /// <summary>True when the snapshot holds a usable result.</summary>
        public bool Valid;

        /// <summary>Why the snapshot is not usable.</summary>
        public string Status = string.Empty;

        /// <summary>The measured features.</summary>
        public SigIdFeatures Features;

        /// <summary>The ranked candidates, best first.</summary>
        public List<SigIdCandidate> Candidates = new List<SigIdCandidate>();

        /// <summary>The frequency the analysis was made for, in Hz.</summary>
        public double FrequencyHz;

        /// <summary>The RX filter width the analysis was made with, in Hz.</summary>
        public double FilterWidthHz;

        /// <summary>How many samples were analysed.</summary>
        public int SampleCount;
    }

    public sealed class SigIdAnalyzer
    {
        /// <summary>
        /// How much audio is kept for the analysis, in seconds.  Fifteen seconds gives the
        /// repetition structure several periods to settle in, which is what makes the
        /// result stop jumping about.
        /// </summary>
        public const double HISTORY_SECONDS = 15.0;

        /// <summary>How often the analysis is refreshed, in milliseconds.</summary>
        public const int REFRESH_MS = 3000;

        /// <summary>
        /// How much better a challenger has to be before the reported signal changes.
        /// Without this the tag flip-flops between two similar candidates every cycle.
        /// </summary>
        public const int HYSTERESIS_MARGIN = 10;

        /// <summary>
        /// Candidates below this confidence are not reported at all - a guess that is
        /// mostly wrong is worse than no answer.
        /// </summary>
        public const int MIN_CONFIDENCE = 30;

        private readonly object _lock = new object();

        private Thread _thread;
        private volatile bool _running;
        private volatile bool _enabled;
        private volatile int _receiver = SigIdAudioTap.RX1;

        private SigIdDatabase _database;
        private SigIdFeatureIndex _index;
        private SigIdClassifier _classifier;

        private float[] _history = new float[0];
        private int _historyCount;
        private int _historyWrite;

        private SigIdAnalysisSnapshot _snapshot = new SigIdAnalysisSnapshot();
        private int _stickySigId;
        private SigIdScope _scope = SigIdScope.AmateurRadio;
        private double _tunedHz;
        private double _filterWidthHz;

        /// <summary>The colour of the identification tag on the panadapter.</summary>
        public static readonly System.Drawing.Color TagColour = System.Drawing.Color.FromArgb(0x40, 0xC0, 0xFF);

        /// <summary>The current result.  Never null.</summary>
        public SigIdAnalysisSnapshot Snapshot
        {
            get { lock (_lock) return _snapshot; }
        }

        /// <summary>True when the analyzer is doing anything at all.</summary>
        public bool IsRunning
        {
            get { return _running; }
        }

        /// <summary>The receiver the analysis is running on (0 = RX1, 1 = RX2).</summary>
        public int AnalyzedReceiver
        {
            get { return _receiver; }
        }

        /// <summary>Which signals the identification looks at.</summary>
        public SigIdScope Scope
        {
            get { return _scope; }
            set { _scope = value; }
        }

        /// <summary>True when identification is switched on.</summary>
        public bool Enabled
        {
            get { return _enabled; }
        }

        /// <summary>
        /// Loads the database, the feature index and the trained classifier.  Returns
        /// false when the database is not installed - identification stays off then.
        /// </summary>
        public bool Load()
        {
            _database = SigIdDatabase.Open();
            if (_database == null || !_database.HasSignals) return false;

            _index = SigIdFeatureIndex.Load();
            _classifier = SigIdClassifier.Load();
            _scope = SigIdScope.AmateurRadio;

            // the index is built from the reference recordings - if it is not there yet
            // (first run after the database was installed) build it in the background
            if (_index == null || _index.Count == 0)
            {
                SigIdInstaller.StartIndexBuild();
            }

            return true;
        }

        /// <summary>Starts the worker thread.</summary>
        public void Start()
        {
            if (_running) return;
            _running = true;

            _thread = new Thread(Worker);
            _thread.IsBackground = true;
            _thread.Name = "sigid-analyzer";
            _thread.Priority = ThreadPriority.BelowNormal;
            _thread.Start();
        }

        /// <summary>Stops the worker thread.</summary>
        public void Stop()
        {
            _running = false;
            Thread thread = _thread;
            if (thread != null && thread.IsAlive)
            {
                try { thread.Join(1500); } catch (Exception) { }
            }
            _thread = null;
        }

        /// <summary>Switches identification on or off.</summary>
        public void SetEnabled(bool enabled)
        {
            _enabled = enabled;

            if (!enabled)
            {
                SigIdAudioTap.Enable(_receiver, false);
                SigIdSpotPublisher.Clear();
                lock (_lock)
                {
                    _historyCount = 0;
                    _historyWrite = 0;
                    _snapshot = new SigIdAnalysisSnapshot();
                    _snapshot.Status = "identification is switched off";
                }
            }
            else
            {
                SigIdAudioTap.Enable(_receiver, true);
            }
        }

        /// <summary>Chooses the receiver to listen to.</summary>
        public void SetReceiver(int receiver)
        {
            if (receiver == _receiver) return;

            bool wasEnabled = _enabled;
            SigIdAudioTap.Enable(_receiver, false);
            _receiver = receiver;

            lock (_lock)
            {
                _historyCount = 0;
                _historyWrite = 0;
            }

            if (wasEnabled) SigIdAudioTap.Enable(_receiver, true);
        }

        /// <summary>Tells the analyzer what the receiver is tuned to and how wide the filter is.</summary>
        public void SetTuning(double tunedHz, double filterWidthHz)
        {
            _tunedHz = tunedHz;
            _filterWidthHz = filterWidthHz;
        }

        /// <summary>Runs one analysis cycle immediately (the "Identify now" button).</summary>
        public void AnalyseNow()
        {
            Drain();
            Analyse();
        }

        private void Worker()
        {
            while (_running)
            {
                try
                {
                    if (_enabled && _database != null)
                    {
                        Drain();
                        Analyse();
                    }
                }
                catch (Exception)
                {
                    // never let the worker die
                }

                Thread.Sleep(REFRESH_MS);
            }

            SigIdAudioTap.Enable(_receiver, false);
            SigIdSpotPublisher.Clear();
        }

        /// <summary>Moves the samples from the native FIFO into the rolling buffer.</summary>
        private void Drain()
        {
            int rate = SigIdAudioTap.Rate;
            if (rate <= 0) return;

            int capacity = (int)(HISTORY_SECONDS * rate);
            if (capacity <= 0) return;

            if (_history.Length != capacity)
            {
                _history = new float[capacity];
                _historyCount = 0;
                _historyWrite = 0;
            }

            float[] buffer = new float[8192];
            while (true)
            {
                int got = SigIdAudioTap.Drain(_receiver, buffer, buffer.Length);
                if (got <= 0) break;

                for (int i = 0; i < got; i++)
                {
                    _history[_historyWrite] = buffer[i];
                    _historyWrite = (_historyWrite + 1) % capacity;
                    if (_historyCount < capacity) _historyCount++;
                }
            }
        }

        /// <summary>Runs the feature extraction and the matcher over the rolling buffer.</summary>
        private void Analyse()
        {
            if (_database == null || _historyCount < 1024) return;

            int rate = SigIdAudioTap.Rate;
            if (rate <= 0) return;

            float[] window = new float[_historyCount];
            int start = _historyCount < _history.Length ? 0 : _historyWrite;
            for (int i = 0; i < _historyCount; i++)
                window[i] = _history[(start + i) % _history.Length];

            SigIdFeatures features = SigIdFeatureExtractor.Analyze(window, rate, _filterWidthHz);

            // the trained classifier has the last word on the modulation family
            if (_classifier != null)
            {
                double confidence;
                SigIdModulationClass predicted = _classifier.Predict(SigIdClassifier.Vector(features), out confidence);
                if (predicted != SigIdModulationClass.Unknown && confidence > 0.15)
                {
                    features.ModulationClass = predicted;
                    features.Confidence = confidence;
                }
            }

            List<SigIdCandidate> candidates = SigIdMatcher.Rank(features, _database, _index, _tunedHz, _scope);

            // ---- stability -----------------------------------------------------
            // Only the candidates that are worth reporting survive.
            List<SigIdCandidate> reported = new List<SigIdCandidate>();
            foreach (SigIdCandidate candidate in candidates)
            {
                if (candidate.Percent < MIN_CONFIDENCE) break;
                reported.Add(candidate);
            }

            // Hysteresis: the signal that is being reported only changes when a
            // challenger is clearly better, so the tag does not flicker.
            if (reported.Count > 0 && _stickySigId > 0)
            {
                SigIdCandidate current = null;
                foreach (SigIdCandidate candidate in reported)
                {
                    if (candidate.Signal.SigId == _stickySigId) { current = candidate; break; }
                }

                if (current != null && reported[0].Signal.SigId != _stickySigId)
                {
                    if (reported[0].Percent < current.Percent + HYSTERESIS_MARGIN)
                    {
                        // move the current one back to the top and keep it there
                        reported.Remove(current);
                        reported.Insert(0, current);
                    }
                }
            }

            if (reported.Count > 0) _stickySigId = reported[0].Signal.SigId;

            // publish the identification as a panadapter spot, so it is drawn by the same
            // renderer that draws the DX cluster tags
            if (reported.Count > 0)
            {
                SigIdCandidate best = reported[0];
                string details = best.Percent + "%" +
                                 (features.Summarise().Length > 0 ? "  -  " + features.Summarise() : string.Empty);
                SigIdSpotPublisher.Publish(best.Name, (long)_tunedHz, TagColour, details);
            }
            else
            {
                SigIdSpotPublisher.Clear();
            }

            SigIdAnalysisSnapshot snapshot = new SigIdAnalysisSnapshot();
            snapshot.Valid = candidates.Count > 0;
            snapshot.Features = features;
            snapshot.Candidates = reported;
            snapshot.FrequencyHz = _tunedHz;
            snapshot.FilterWidthHz = _filterWidthHz;
            snapshot.SampleCount = _historyCount;
            snapshot.Status = reported.Count > 0
                    ? reported[0].Name
                    : "no match above the confidence limit";

            lock (_lock)
            {
                _snapshot = snapshot;
            }
        }
    }
}
