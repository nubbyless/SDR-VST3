/*  FldigiManager.cs
 *
 *  Manages the fldigi sidecar process for HF digital modes.
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
using System.Threading;
using System.Windows.Forms;

namespace Thetis.FLDIGI
{
    /// <summary>
    /// Owns the fldigi.exe sidecar: spawn / kill / auto-restart, plus the
    /// audio and control named-pipe bridges that talk to it.  Enable/disable
    /// is driven from the main console "FLDIGI" menu entry (between CWX and
    /// Diversity); this class reacts to SetEnabled and does NOT auto-start on
    /// app launch by design -- the operator starts/stops the sidecar
    /// explicitly each session.
    /// </summary>
    public static class FldigiManager
    {
        public const string PIPE_BASE = "SDRVST3.FLDIGI";
        public const int AUDIO_RATE = 8000;

        private static readonly object _sync = new object();
        private static Process _process;
        private static int _pid;
        private static bool _enabled;
        private static bool _starting;

        // Authority for what the sidecar should be doing right now.
        private static long _desiredFreqHz;
        private static volatile string _desiredMode = "RTTY";
        private static volatile bool _pttEngaged;

        private static System.Threading.Timer _restartTimer;

        /// <summary>True while the fldigi session is desired on (managed + bridge live).</summary>
        public static bool Enabled { get { lock (_sync) return _enabled; } }

        /// <summary>PID of the running fldigi.exe, 0 if none.</summary>
        public static int Pid { get { lock (_sync) return _pid; } }

        public static bool Running
        {
            get
            {
                lock (_sync)
                {
                    try { return _process != null && _process.HasExited == false; }
                    catch { return false; }
                }
            }
        }

        public static long DesiredFreqHz { get { return _desiredFreqHz; } }
        public static string DesiredMode { get { return _desiredMode; } }
        public static bool PttEngaged { get { return _pttEngaged; } }

        static FldigiManager()
        {
            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SDR-VST3");
                Directory.CreateDirectory(dir);
                var l = new TextWriterTraceListener(Path.Combine(dir, "fldigi-integr.log"), "fldigiint");
                Trace.Listeners.Add(l);
                Trace.AutoFlush = true;
            }
            catch { }
        }

        /// <summary>Last known mode reported by the sidecar (for UI/round-trip).</summary>
        public static volatile string LastReportedMode = "";
        public static volatile string LastRxText = "";
        public static long LastReportedFreqHz = 0;

        /// <summary>Raised after the sidecar's desired state changes (started,
        /// exited, crash-recovered) so the console can refresh the menu.</summary>
        public static event EventHandler StateChanged;

        private static void NotifyStateChanged()
        {
            try { StateChanged?.Invoke(null, EventArgs.Empty); }
            catch { }
        }

        /// <summary>Executable path preference.  If unset the manager probes
        /// <c>Application.StartupPath\Fldigi\fldigi.exe</c>.</summary>
        public static string ExecutablePath = "";

        /// <summary>Config dir passed via --config-dir.</summary>
        public static string ConfigDir = "";

        public static string AudioPipeName(int pid)
        {
            return string.Format("{0}.{1}.AUDIO", PIPE_BASE, pid);
        }

        public static string CtlPipeName(int pid)
        {
            return string.Format("{0}.{1}.CTL", PIPE_BASE, pid);
        }

        /// <summary>Paths probed, in order, for fldigi.exe.</summary>
        private static string ResolveExecutable()
        {
            if (!string.IsNullOrWhiteSpace(ExecutablePath) && File.Exists(ExecutablePath))
                return ExecutablePath;

            string[] candidates =
            {
                Path.Combine(Application.StartupPath, "Fldigi", "fldigi.exe"),
                Path.Combine(Path.GetDirectoryName(Application.ExecutablePath) ?? "", "Fldigi", "fldigi.exe"),
                // Dev-tree fallback: the freshly built sidecar next to this
                // source checkout.  Lets "enable" launch the current build
                // before a real installer layout exists.
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "fldigi", "src", "fldigi.exe"),
            };
            foreach (var c in candidates)
            {
                string full;
                try { full = Path.GetFullPath(c); } catch { continue; }
                if (!string.IsNullOrWhiteSpace(full) && File.Exists(full))
                    return full;
            }
            return "";
        }

        private static string ResolveConfigDir()
        {
            if (!string.IsNullOrWhiteSpace(ConfigDir))
                return ConfigDir;
            string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(root, "SDR-VST3", "fldigi");
        }

        /// <summary>
        /// Enable or disable the whole fldigi session.  Called by the Setup
        /// checkbox (which also owns persistence).  When enabling, the native
        /// RX1/TX taps are armed and the sidecar is launched; when disabling
        /// everything is torn down and the taps disarmed.
        /// </summary>
        public static void SetEnabled(bool on)
        {
            lock (_sync)
            {
                if (on == _enabled)
                {
                    // Still apply if a (re)start was requested while already
                    // enabled but the process is gone.
                    if (!on || Running)
                        return;
                }
                _enabled = on;
            }

            if (on)
                Start();
            else
                Stop();
        }

        public static void Start()
        {
            bool should;
            lock (_sync) should = _enabled && !_starting && !Running;
            if (!should) return;

            lock (_sync) _starting = true;
            try
            {
                string exe = ResolveExecutable();
                if (exe == "")
                {
                    Trace.WriteLine("FLDIGI: no fldigi.exe found (checked Fldigi\\fldigi.exe next to host)");
                    return;
                }

                string configDir = ResolveConfigDir();
                try { Directory.CreateDirectory(configDir); } catch { }

                string args = string.Format("--sdrvst3-pipe={0} --config-dir=\"{1}\"",
                    PIPE_BASE, configDir);

                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
                };
                // Dev convenience: let the MinGW DLLs resolve exactly like the
                // probe harness does.  The install layout ships them next to
                // fldigi.exe so this is only meaningful for dev trees.
                string mingw = @"C:\msys64\mingw64\bin";
                if (Directory.Exists(mingw))
                    psi.Environment["PATH"] = mingw + ";" + (Environment.GetEnvironmentVariable("PATH") ?? "");

                var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                if (!proc.Start())
                {
                    Trace.WriteLine("FLDIGI: process failed to start");
                    return;
                }
                proc.Exited += OnProcessExited;
                _process = proc;
                _pid = proc.Id;

                // Arm taps + start bridges.  RX flows whenever the session is
                // up so the modem always has audio; TX is gated natively by
                // mox (audio.cs SetFldigiMoxState).
                try { cmaster.SetFldigiRxEnable(0, 1); } catch { }
                try { cmaster.SetFldigiTxEnable(1); } catch { }
                try { cmaster.FldigiFlush(); } catch { }

                FldigiAudioBridge.Start(_pid);
                FldigiControlBridge.Start(_pid, _desiredFreqHz, _desiredMode);
                Trace.WriteLine(string.Format("FLDIGI: started pid {0} ({1})", _pid, exe));
            }
            catch (Exception ex)
            {
                Trace.WriteLine("FLDIGI: start exception: " + ex.Message);
            }
            finally
            {
                lock (_sync) _starting = false;
            }
        }

        private static void OnProcessExited(object sender, EventArgs e)
        {
            Process p = sender as Process;
            if (p == null) return;

            // A graceful exit (code 0) means the operator closed the fldigi
            // window: treat it as the session being turned off (checkbox up)
            // and do NOT auto-restart.  A non-zero code is an unexpected crash:
            // keep the session desired on so it auto-recovers, menu stays green.
            int code = 0;
            try { code = p.ExitCode; } catch { }
            bool graceful = code == 0;

            lock (_sync)
            {
                if (_process != p)
                    return;   // stale exit from an old instance
                _process = null;
                _pid = 0;
                if (graceful)
                    _enabled = false;
            }
            try { cmaster.SetFldigiRxEnable(0, 0); } catch { }
            try { cmaster.SetFldigiTxEnable(0); } catch { }
            FldigiAudioBridge.Stop();
            FldigiControlBridge.Stop();
            try { p.Dispose(); } catch { }
            NotifyStateChanged();

            bool restart;
            lock (_sync) restart = !graceful && _enabled;
            if (restart)
                ScheduleRestart();
        }

        private static void ScheduleRestart()
        {
            lock (_sync)
            {
                if (_restartTimer != null)
                    return;
                // Thread timer (not a WinForms timer): OnProcessExited runs on
                // the threadpool with no message pump, where a WinForms Timer's
                // Tick would never fire.
                _restartTimer = new System.Threading.Timer(
                    RestartTick, null, 4000, Timeout.Infinite);
            }
        }

        private static void RestartTick(object state)
        {
            lock (_sync)
            {
                if (_restartTimer != null)
                {
                    _restartTimer.Dispose();
                    _restartTimer = null;
                }
            }
            if (Enabled && !Running)
                Start();
        }

        public static void Stop()
        {
            Process p;
            lock (_sync)
            {
                p = _process;
                _process = null;
                _pid = 0;
            }

            try { FldigiControlBridge.Stop(); } catch { }
            try { FldigiAudioBridge.Stop(); } catch { }

            if (p != null)
            {
                try
                {
                    if (!p.HasExited)
                        p.Kill();
                }
                catch { }
                try { p.WaitForExit(2000); } catch { }
                try { p.Dispose(); } catch { }
            }

            try { cmaster.SetFldigiMoxState(0); } catch { }
            try { cmaster.SetFldigiRxEnable(0, 0); } catch { }
            try { cmaster.SetFldigiTxEnable(0); } catch { }
            try { cmaster.FldigiFlush(); } catch { }
            Trace.WriteLine("FLDIGI: stopped");
        }

        /// <summary>App-exit hook: always tear down cleanly.</summary>
        public static void Shutdown()
        {
            lock (_sync) _enabled = false;
            Stop();
        }

        /// <summary>Desired-freq push path (VFO change / reconnect reconcile).</summary>
        public static void SetDesiredFreqHz(long hz)
        {
            _desiredFreqHz = hz;
            if (Running)
                FldigiControlBridge.PushFreq(hz);
        }

        public static void SetDesiredMode(string mode)
        {
            _desiredMode = string.IsNullOrEmpty(mode) ? "RTTY" : mode;
            if (Running)
                FldigiControlBridge.PushMode(_desiredMode);
        }

        public static void SetPtt(bool on)
        {
            _pttEngaged = on;
            if (Running)
                FldigiControlBridge.PushPtt(on);
        }
    }
}