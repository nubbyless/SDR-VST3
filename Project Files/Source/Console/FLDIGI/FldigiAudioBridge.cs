/*  FldigiAudioBridge.cs
 *
 *  Async duplex named-pipe client for the fldigi AUDIO channel.
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

namespace Thetis.FLDIGI
{
    /// <summary>
    /// Drains the down-sampled RX audio from the native module
    /// (FldigiDrainRx) and pushes it into the fldigi AUDIO pipe.  Asynchronously
    /// reads any TX audio fldigi returns and pushes it into the native TX FIFO
    /// (FldigiPushTx).
    ///
    /// Client-side named-pipe concurrency rule (HANDOFF_FLDIGI_P1.md §5.2):
    /// a pending blocking read + concurrent write on the SAME duplex handle
    /// deadlocks on this Windows/.NET combo.  This class uses overlapped I/O
    /// for the read side (async stream) and a synchronous Write guarded by
    /// a lock -- exactly the V4 pattern that was proven safe.
    /// </summary>
    internal static class FldigiAudioBridge
    {
        private static NamedPipeClientStream _stream;
        private static volatile bool _running;
        private static int _gen;        // incremented on each Start/Stop cycle
        private static Thread _writerThread;
        private static CancellationTokenSource _cts;
        private static readonly object _writeLock = new object();
        private static readonly float[] _rxBuf = new float[4096];
        private static readonly float[] _txBuf = new float[4096];
        private static readonly byte[] _writeBytes = new byte[4096 * 4];

        private const int DRAIN_BATCH = 2048;   // samples per FldigiDrainRx call
        private const int READ_LEN = 16384;     // bytes per async read

        public static void Start(int pid)
        {
            Stop();                              // tear down any prior session
            if (pid <= 0) return;
            _gen++;
            _running = true;
            _cts = new CancellationTokenSource();
            var stream = new NamedPipeClientStream(
                ".",
                FldigiManager.AudioPipeName(pid),
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            _stream = stream;

            // Give the server a short window to create its pipe instance.
            bool connected = false;
            try
            {
                stream.Connect(3000);
                connected = true;
            }
            catch (TimeoutException)
            {
                Trace.WriteLine("FLDIGI-AUDIO: pipe connect timed out");
            }
            catch (IOException ex)
            {
                Trace.WriteLine("FLDIGI-AUDIO: pipe connect error: " + ex.Message);
            }
            if (!connected)
            {
                try { stream.Dispose(); } catch { }
                _stream = null;
                return;
            }
            Trace.WriteLine(string.Format("FLDIGI-AUDIO: connected (pid {0})", pid));

            // Writer thread: drain native RX FIFO -> write into pipe.
            _writerThread = new Thread(WriterLoop) { IsBackground = true, Name = "fldigi-audio-wr" };
            _writerThread.Start();

            // Reader: async read loop.
            var _ = ReaderLoop(_cts.Token, _gen);
        }

        private static void WriterLoop()
        {
            int gen = _gen;
            while (_running)
            {
                if (_gen != gen) return;
                var s = _stream;
                if (s == null || !s.IsConnected)
                {
                    Thread.Sleep(20);
                    continue;
                }
                try
                {
                    int n = cmaster.FldigiDrainRx(0, _rxBuf, DRAIN_BATCH);
                    if (n > 0)
                    {
                        Buffer.BlockCopy(_rxBuf, 0, _writeBytes, 0, n * 4);
                        lock (_writeLock)
                        {
                            if (_stream != null && _stream.IsConnected)
                                _stream.Write(_writeBytes, 0, n * 4);
                        }
                    }
                }
                catch (ObjectDisposedException) { return; }
                catch (IOException ex)
                {
                    Trace.WriteLine("FLDIGI-AUDIO: write exception: " + ex.Message);
                    return;
                }
                catch (Exception ex)
                {
                    Trace.WriteLine("FLDIGI-AUDIO: writer error: " + ex.Message);
                }
                Thread.Sleep(4);
            }
        }

        private static async Task ReaderLoop(CancellationToken ct, int gen)
        {
            byte[] readBuf = new byte[READ_LEN];
            try
            {
                while (_running && !ct.IsCancellationRequested && _gen == gen)
                {
                    var s = _stream;
                    if (s == null || !s.IsConnected)
                    {
                        await Task.Delay(25, ct).ConfigureAwait(false);
                        continue;
                    }
                    int bytesRead = 0;
                    try
                    {
                        bytesRead = await s.ReadAsync(readBuf, 0, READ_LEN, ct).ConfigureAwait(false);
                    }
                    catch (ObjectDisposedException) { return; }
                    catch (OperationCanceledException) { return; }
                    catch (IOException) { return; }
                    catch { return; }
                    if (bytesRead <= 0)
                        return;      // server closed the pipe
                    int floatCount = bytesRead / 4;
                    if (floatCount <= 0) continue;
                    try
                    {
                        // Convert bytes to floats via manual copy to avoid
                        // intermediate array allocation in the hot path.
                        for (int i = 0; i < floatCount; i++)
                        {
                            _txBuf[i] = BitConverter.ToSingle(readBuf, i * 4);
                        }
                        cmaster.FldigiPushTx(_txBuf, floatCount);
                    }
                    catch { }
                }
            }
            catch (TaskCanceledException) { }
            catch (OperationCanceledException) { }
            catch (ObjectDisposedException) { }
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
            var s = _stream;
            _stream = null;
            if (s != null)
            {
                try { s.Close(); } catch { }
                try { s.Dispose(); } catch { }
            }
            var t = _writerThread;
            _writerThread = null;
            if (t != null)
            {
                try { t.Join(200); } catch { }
            }
            Trace.WriteLine("FLDIGI-AUDIO: stopped");
        }
    }
}