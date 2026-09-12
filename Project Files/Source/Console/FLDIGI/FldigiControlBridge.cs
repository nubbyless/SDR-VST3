/*  FldigiControlBridge.cs
 *
 *  Async duplex named-pipe client for the fldigi CTL channel.
 *
 *  Copyright (C) 2026  SDR-VST3 Thetis integration (fldigi sidecar)
 *
 *  This program is free software; you can redistribute it and/or
 *  modify it under the terms of the GNU General Public License
 *  as published by the Free Software Foundation; either version 2
 *  of the License, or (at your option) any later version.
 */

using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Thetis.FLDIGI
{
    /// <summary>
    /// Newline-delimited JSON CTL channel to the fldigi sidecar.
    /// Outbound (Thetis -> fldigi): ping / set_freq / set_mode / set_ptt.
    /// Inbound (fldigi -> Thetis): status (250 ms) / ok / pong / err.
    ///
    /// Same async-read + locked-write pattern as FldigiAudioBridge so a
    /// blocking read is never outstanding while the pipe is written to.
    /// </summary>
    internal static class FldigiControlBridge
    {
        private static NamedPipeClientStream _stream;
        private static volatile bool _running;
        private static int _gen;
        private static CancellationTokenSource _cts;
        private static readonly object _writeLock = new object();

        private static Console.MoxChanged _moxHandler;
        private static Console.VFOAFrequencyChanged _vfoaHandler;
        private static bool _handlersWired;

        private static volatile bool _radioForcedDigital;
        private static long _lastPushFreqHz;
        private static long _prevPushFreqHz;
        private static long _mirrorSuppressUntilTicks;
        private static volatile bool _lastStatusPttOn;
        private static long _lastAppliedRadioFreqHz;
        private static volatile string _lastPushedRigMode = "";
        private static DSPMode _lastModeledRadioMode = DSPMode.FIRST;
        private static volatile string _lastStatusModeName = "";

        private const int WRITE_LEN = 512;

        static FldigiControlBridge()
        {
            _moxHandler = (rx, oldMox, newMox) =>
            {
                if (_running) PushPtt(newMox);
            };
            _vfoaHandler = (oldBand, newBand, oldMode, newMode, oldFilter, newFilter,
                            oldFreq, newFreq, oldCentreF, newCentreF,
                            oldCTUN, newCTUN, oldZoom, newZoom, offset, rx) =>
            {
                try
                {
                    long hz = (long)Math.Round(newFreq * 1e6);
                    FldigiManager.SetDesiredFreqHz(hz);
                    PushRigMode(MapDSPModeToRig(newMode));
                }
                catch { }
            };
        }

        public static void Start(int pid, long freqHz, string mode)
        {
            Stop();
            if (pid <= 0) return;
            _gen++;
            _running = true;
            _radioForcedDigital = false;
            _lastStatusPttOn = false;
            _lastAppliedRadioFreqHz = 0;
            _lastPushedRigMode = "";
            _lastStatusModeName = null;
            _lastModeledRadioMode = DSPMode.FIRST;
            _cts = new CancellationTokenSource();

            var stream = new NamedPipeClientStream(
                ".",
                FldigiManager.CtlPipeName(pid),
                PipeDirection.InOut,
                PipeOptions.Asynchronous);
            _stream = stream;

            // Give the server time to reach its ConnectNamedPipe loop.
            bool connected = false;
            for (int i = 0; i < 50 && _running; i++)
            {
                try
                {
                    stream.Connect(100);
                    connected = true;
                    break;
                }
                catch (TimeoutException) { }
                catch (IOException) { break; }
                catch { break; }
                Thread.Sleep(100);
            }
            if (!connected)
            {
                Trace.WriteLine("FLDIGI-CTL: pipe connect failed");
                try { stream.Dispose(); } catch { }
                _stream = null;
                return;
            }
            Trace.WriteLine(string.Format("FLDIGI-CTL: connected (pid {0})", pid));

            WireConsole();
            FldigiManager.LastReportedMode = mode;
            PushPtt(false);
            PushFreq(freqHz);
            PushMode(mode);
            EnsureDigitalMode();

            // Push the radio's current sideband/mode to fldigi (rig-mode record).
            try
            {
                var c0 = Console.getConsole();
                if (c0 != null && !c0.IsDisposed)
                {
                    Action p0 = () =>
                    {
                        try { PushRigMode(MapDSPModeToRig(c0.RX1DSPMode)); }
                        catch { }
                    };
                    if (c0.InvokeRequired)
                        c0.BeginInvoke(p0);
                    else
                        p0();
                }
            }
            catch { }

            // Baseline established: _lastPushFreqHz now equals the current radio
            // freq (set by PushFreq above).  Halt status reconciliation briefly
            // so fldigi's own boot-up VFO cannot yank the radio at session start.
            _mirrorSuppressUntilTicks =
                (DateTime.UtcNow + TimeSpan.FromMilliseconds(1500)).Ticks;

            var _ = ReaderLoop(_cts.Token, _gen);
        }

        public static void Stop()
        {
            _running = false;
            _gen++;
            var cts = _cts;
            _cts = null;
            if (cts != null)
            {
                try { cts.Cancel(); } catch { }
                try { cts.Dispose(); } catch { }
            }
            UnwireConsole();
            var s = _stream;
            _stream = null;
            if (s != null)
            {
                try { s.Close(); } catch { }
                try { s.Dispose(); } catch { }
            }
            _radioForcedDigital = false;
            _lastStatusPttOn = false;
            _lastAppliedRadioFreqHz = 0;
            _lastPushedRigMode = "";
            _lastStatusModeName = null;
            _lastModeledRadioMode = DSPMode.FIRST;
        }

        public static void PushPtt(bool on)
        {
            Send(string.Format("{{\"cmd\":\"set_ptt\",\"on\":{0}}}", on ? "true" : "false"));
        }

        public static void PushFreq(long hz)
        {
            if (hz == _lastPushFreqHz) return;
            _prevPushFreqHz = _lastPushFreqHz;
            _lastPushFreqHz = hz;
            Send(string.Format("{{\"cmd\":\"set_freq\",\"rf\":{0}}}", hz));
        }

        public static void PushMode(string mode)
        {
            if (string.IsNullOrEmpty(mode)) return;
            Give("{\"cmd\":\"set_mode\",\"mode\":" + JsonQuote(mode) + "}");
        }

        public static void PushRigMode(string rig)
        {
            if (string.IsNullOrEmpty(rig)) return;
            if (rig == _lastPushedRigMode) return;
            _lastPushedRigMode = rig;
            Give("{\"cmd\":\"set_rig_mode\",\"mode\":" + JsonQuote(rig) + "}");
        }

        /// <summary>Map a Thetis DSPMode to the sidecar's rig-mode record.</summary>
        private static string MapDSPModeToRig(DSPMode m)
        {
            switch (m)
            {
                case DSPMode.LSB: return "LSB";
                case DSPMode.USB: return "USB";
                case DSPMode.DSB: return "DSB";
                case DSPMode.CWL: return "CWL";
                case DSPMode.CWU: return "CWU";
                case DSPMode.FM: return "FM";
                case DSPMode.AM:
                case DSPMode.SAM: return "AM";
                case DSPMode.SPEC: return "SPEC";
                case DSPMode.DIGL: return "DATA-L";
                case DSPMode.DIGU:
                case DSPMode.DRM:
                default: return "DATA-U";
            }
        }

        /// <summary>Map an fldigi modem name to a Thetis DSPMode for the radio
        /// to follow (DSPMode.FIRST = no mapping).</summary>
        private static DSPMode MapModemToDSPMode(string s)
        {
            if (string.IsNullOrEmpty(s)) return DSPMode.FIRST;
            string u = s.Trim().ToUpperInvariant();
            switch (u)
            {
                case "USB": return DSPMode.USB;
                case "LSB": return DSPMode.LSB;
                case "DSB": return DSPMode.DSB;
                case "AM": return DSPMode.AM;
                case "SAM": return DSPMode.SAM;
                case "FM":
                case "NFM": return DSPMode.FM;
                case "DIGITAL-U":
                case "DATA-U": return DSPMode.DIGU;
                case "DIGITAL-L":
                case "DATA-L": return DSPMode.DIGL;
            }
            if (u.StartsWith("CW")) return DSPMode.CWU;
            string[] digis =
            {
                "PSK", "RTTY", "MFSK", "OLIVIA", "THOR", "HELL", "MT63",
                "CONTESTIA", "FSQ", "PACKET", "SITOR", "DOMINO", "QPSK",
                "8PSK", "NAVTEX", "WEFAX", "DIGITAL", "DATA"
            };
            foreach (var d in digis)
                if (u.Contains(d)) return DSPMode.DIGU;
            return DSPMode.FIRST;
        }

        private static string JsonQuote(string s)
        {
            return "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private static void Send(string json)
        {
            if (!_running) return;
            var s = _stream;
            if (s == null || !s.IsConnected) return;
            byte[] data = System.Text.Encoding.UTF8.GetBytes(json + "\n");
            try
            {
                lock (_writeLock)
                {
                    if (_stream == null || !_stream.IsConnected) return;
                    _stream.Write(data, 0, data.Length);
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("FLDIGI-CTL: write error: " + ex.Message);
            }
        }

        /// <summary>Unlocked variant (no IsConnected by design) used before the
        /// first read; keeps the connection handshake straightforward.</summary>
        private static void Give(string json)
        {
            if (!_running) return;
            try
            {
                lock (_writeLock)
                {
                    if (_stream == null) return;
                    byte[] data = System.Text.Encoding.UTF8.GetBytes(json + "\n");
                    _stream.Write(data, 0, data.Length);
                }
            }
            catch { }
        }

        private static async Task ReaderLoop(CancellationToken ct, int gen)
        {
            byte[] buf = new byte[4096];
            string pending = "";
            int total = 0;
            var ctsInner = _cts;
            try
            {
                while (_running && !ct.IsCancellationRequested && _gen == gen)
                {
                    var s = _stream;
                    if (!_running || _gen != gen) return;
                    if (s == null)
                    {
                        await Task.Delay(25, ct).ConfigureAwait(false);
                        continue;
                    }
                    int n = 0;
                    try
                    {
                        n = await s.ReadAsync(buf, 0, buf.Length, ct).ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException) { return; }
                    catch (OperationCanceledException) { return; }
                    catch (IOException) { return; }
                    catch { return; }
                    if (n <= 0) return;   // server closed
                    pending += System.Text.Encoding.UTF8.GetString(buf, 0, n);
                    total += n;
                    int nl;
                    while ((nl = pending.IndexOf('\n')) >= 0)
                    {
                        string line = pending.Substring(0, nl).Trim();
                        pending = pending.Substring(nl + 1);
                        if (line.Length > 0)
                            HandleIncoming(line);
                    }
                    // Guard against an unbounded accumulation if fldigi floods.
                    if (total > 256 * 1024)
                    {
                        pending = "";
                        total = 0;
                    }
                }
            }
            catch (TaskCanceledException) { }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
        }

        private static void HandleIncoming(string line)
        {
            try
            {
                var o = JObject.Parse(line);
                string cmd = (string)o["cmd"];
                string type = (string)o["type"];

                // Operator-initiated QSY in fldigi commands the radio.
                if (cmd == "qsy")
                {
                    long qrf = 0;
                    var rfTok = o["rf"];
                    if (rfTok != null)
                    {
                        switch (rfTok.Type)
                        {
                            case JTokenType.Integer:
                                qrf = (long)rfTok;
                                break;
                            case JTokenType.String:
                                long.TryParse((string)rfTok, out qrf);
                                break;
                            default:
                                break;
                        }
                    }
                    if (qrf > 0 && qrf != _lastAppliedRadioFreqHz)
                    {
                        _lastAppliedRadioFreqHz = qrf;
                        ApplyRadioFreq(qrf);
                    }
                    return;
                }

                // fldigi modem-change event: map to a Thetis DSPMode and
                // apply to the radio (mirrors the standard fldigi+rig mode
                // binding).  Only applied when the radio is currently on a
                // mode we last set via this same path (avoids yanking a
                // deliberate manual mode choice).
                if (cmd == "mode")
                {
                    ApplyModemChange((string)o["mode"]);
                    return;
                }

                if (type == "status")
                {
                    string mode = (string)o["mode"];
                    string rxfreq = (string)o["rxfreq"];
                    string ptt = (string)o["ptt"];
                    string txt = (string)o["txt"];
                    if (mode != null)
                    {
                        FldigiManager.LastReportedMode = mode;
                        // Operator-side modem change inside fldigi: reconcile
                        // the radio DSPMode to the reported modem, skipping the
                        // baseline frame right after (re)connect so a stored
                        // modem can't yank the radio at session start.
                        ApplyModemChange(mode);
                    }
                    if (txt != null) FldigiManager.LastRxText = txt;
                    if (rxfreq != null)
                    {
                        long v;
                        if (long.TryParse(rxfreq, out v))
                        {
                            FldigiManager.LastReportedFreqHz = v;
                            // Operator-side change inside fldigi (scroll box,
                            // memory click, freq entry, QSY button...):
                            // reconcile the radio to whatever fldigi reports,
                            // UNLESS the value is one we just commanded
                            // ourselves (own set_freq pushes, in-flight stale
                            // statuses after a dial move, already-applied QSY).
                            bool commanded =
                                v == _lastPushFreqHz ||
                                v == _prevPushFreqHz ||
                                v == _lastAppliedRadioFreqHz;
                            if (!commanded &&
                                v > 0 &&
                                DateTime.UtcNow.Ticks > _mirrorSuppressUntilTicks)
                            {
                                _lastAppliedRadioFreqHz = v;
                                ApplyRadioFreq(v);
                            }
                        }
                    }
                    if (ptt != null)
                    {
                        bool on = (ptt == "TX" || ptt == "TUNE");
                        if (on != _lastStatusPttOn)
                        {
                            _lastStatusPttOn = on;
                            ApplyRadioMox(on);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Trace.WriteLine("FLDIGI-CTL: parse error: " + ex.Message);
            }
        }

        private static void WireConsole()
        {
            if (_handlersWired) return;
            var c = Console.getConsole();
            if (c == null) return;
            try
            {
                c.MoxChangeHandlers += _moxHandler;
                c.VFOAFrequencyChangeHandlers += _vfoaHandler;
                _handlersWired = true;
            }
            catch { }
        }

        private static void UnwireConsole()
        {
            if (!_handlersWired) return;
            var c = Console.getConsole();
            if (c != null)
            {
                try
                {
                    c.MoxChangeHandlers -= _moxHandler;
                    c.VFOAFrequencyChangeHandlers -= _vfoaHandler;
                }
                catch { }
            }
            _handlersWired = false;
        }

        /// <summary>
        /// fldigi owns the demodulation; the radio just needs to be on a
        /// digital passband.  Force DIGU once per session if the operator has
        /// an analogue mode selected (mirrors the natural setup a user would
        /// choose anyway and stops "no audio heard" confusion).
        /// </summary>
        private static void EnsureDigitalMode()
        {
            if (_radioForcedDigital) return;
            _radioForcedDigital = true;
            var c = Console.getConsole();
            if (c == null || c.IsDisposed) return;
            try
            {
                if (c.InvokeRequired)
                    c.BeginInvoke((Action)(() => ApplyDigitalMode(c)));
                else
                    ApplyDigitalMode(c);
            }
            catch { }
        }

        /// <summary>fldigi operator QSY (waterfall click) commands the radio,
        /// exactly like a rig under CAT control.  The radio's own VFOA change
        /// handler then echoes the new frequency back to fldigi as set_freq,
        /// where the values converge (both sides dedupe identical values).</summary>
        private static void ApplyRadioFreq(long hz)
        {
            try
            {
                var c = Console.getConsole();
                if (c == null || c.IsDisposed) return;
                if (c.InvokeRequired)
                    c.BeginInvoke((Action)(() => SetRadioFreq(c, hz)));
                else
                    SetRadioFreq(c, hz);
            }
            catch { }
        }

        private static void SetRadioFreq(Console c, long hz)
        {
            if (c == null || c.IsDisposed) return;
            try
            {
                c.VFOAFreq = hz / 1e6;
            }
            catch { }
        }

        /// <summary>React to an fldigi modem state.  The first observation after a
        /// (re)connect only establishes the baseline; subsequent changes map to
        /// a Thetis DSPMode and apply to the radio.</summary>
        private static void ApplyModemChange(string modem)
        {
            if (string.IsNullOrEmpty(modem)) return;
            if (_lastStatusModeName == null)
            {
                _lastStatusModeName = modem;
                return;
            }
            if (string.Equals(modem, _lastStatusModeName, StringComparison.Ordinal))
                return;
            _lastStatusModeName = modem;
            try
            {
                DSPMode mapped = MapModemToDSPMode(modem);
                if (mapped != DSPMode.FIRST && mapped != _lastModeledRadioMode)
                {
                    _lastModeledRadioMode = mapped;
                    ApplyRadioMode(mapped);
                }
            }
            catch { }
        }

        private static void ApplyRadioMode(DSPMode m)
        {
            try
            {
                var c = Console.getConsole();
                if (c == null || c.IsDisposed) return;
                if (c.InvokeRequired)
                    c.BeginInvoke((Action)(() => SetRadioMode(c, m)));
                else
                    SetRadioMode(c, m);
            }
            catch { }
        }

        private static void SetRadioMode(Console c, DSPMode m)
        {
            if (c == null || c.IsDisposed) return;
            try
            {
                if (c.RX1DSPMode != m)
                    c.RX1DSPMode = m;
            }
            catch { }
        }

        private static void ApplyDigitalMode(Console c)
        {
            if (c == null || c.IsDisposed) return;
            try
            {
                var m = c.RX1DSPMode;
                if (m != DSPMode.DIGL && m != DSPMode.DIGU && m != DSPMode.SPEC)
                    c.RX1DSPMode = DSPMode.DIGU;
            }
            catch { }
        }

        /// <summary>fldigi side TX button (PTT) states the radio.  The radio
        /// stays the master of the actual over (audio.cs), so all we do here is
        /// key MOX and let the existing MOX machinery cascade.</summary>
        private static void ApplyRadioMox(bool on)
        {
            try
            {
                var c = Console.getConsole();
                if (c == null || c.IsDisposed) return;
                if (c.InvokeRequired)
                    c.BeginInvoke((Action)(() => SetRadioMox(c, on)));
                else
                    SetRadioMox(c, on);
            }
            catch { }
        }

        private static void SetRadioMox(Console c, bool on)
        {
            if (c == null || c.IsDisposed) return;
            try
            {
                if (c.MOX != on)
                    c.MOX = on;
            }
            catch { }
        }
    }
}