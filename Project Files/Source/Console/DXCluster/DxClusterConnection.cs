/*  DxClusterConnection.cs

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
// DX Cluster - a single telnet node connection.                                               //
// Owns one TcpClient and a dedicated background reader thread. Raises raw line and state      //
// events; the manager (DxClusterManager) is responsible for parsing and feeding spots.        //
//============================================================================================//

using System;
using System.Drawing;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Thetis
{
    public enum DxClusterConnectionState
    {
        Disconnected,
        Connecting,
        Connected,
        Disconnecting,
        Error
    }

    public sealed class DxClusterConnection : IDisposable
    {
        public event Action<DxClusterConnection, string> LineReceived;
        public event Action<DxClusterConnection> StateChanged;

        public string Id { get; }
        public string Host { get; }
        public int Port { get; }
        public string LoginCallsign { get; set; }
        public bool LoginSent { get; private set; }
        public DxClusterConnectionState State { get; private set; }
        public string LastError { get; private set; }

        // bound node settings (color / panadapter visibility), kept in sync by the manager
        public DxClusterNodeSettings Node { get; set; }
        public Color SpotColor { get; set; } = Color.FromArgb(255, 255, 200, 0);
        public bool ShowOnPanadapter { get; set; } = true;

        private readonly object _sync = new object();
        private TcpClient _client;
        private StreamReader _reader;
        private StreamWriter _writer;
        private Thread _thread;
        private volatile bool _running;

        public DxClusterConnection(string host, int port, string loginCallsign = "")
        {
            Host = host ?? "";
            Port = port;
            LoginCallsign = loginCallsign ?? "";
            Id = Guid.NewGuid().ToString("N");
            State = DxClusterConnectionState.Disconnected;
            LastError = "";
        }

        public void Connect()
        {
            lock (_sync)
            {
                if (_running) return;

                _running = true;
                LoginSent = false;
                LastError = "";
                setState(DxClusterConnectionState.Connecting);

                _thread = new Thread(run)
                {
                    IsBackground = true,
                    Name = "DxCluster:" + Host
                };
                _thread.Start();
            }
        }

        public void Disconnect()
        {
            lock (_sync)
            {
                if (!_running) return;

                _running = false;
                setState(DxClusterConnectionState.Disconnecting);
            }

            cleanUp();
        }

        // send the login callsign once, if connected and not already logged in
        public void SendLogin(string callsign)
        {
            callsign = (callsign ?? "").Trim();
            if (callsign.Length == 0) return;
            if (LoginSent) return;
            if (State != DxClusterConnectionState.Connected) return;

            LoginCallsign = callsign;
            Send(callsign);
            LoginSent = true;
        }

        public void Send(string text)
        {
            try
            {
                if (_writer == null) return;
                if (State != DxClusterConnectionState.Connected) return;
                if (string.IsNullOrEmpty(text)) return;

                _writer.WriteLine(text);
            }
            catch (Exception ex)
            {
                Common.LogString("DXCluster send failed (" + Host + ":" + Port + "): " + ex.Message);
            }
        }

        private void run()
        {
            try
            {
                TcpClient client = new TcpClient();
                client.NoDelay = true;
                client.Connect(Host, Port);

                _client = client;
                NetworkStream ns = client.GetStream();

                _reader = new StreamReader(ns, Encoding.Latin1, false, 4096);
                _writer = new StreamWriter(ns, Encoding.Latin1) { AutoFlush = true, NewLine = "\r\n" };

                setState(DxClusterConnectionState.Connected);

                // log in with the callsign if we already have one; otherwise the manager can
                // supply it later via SendLogin (dx clusters usually stream no spots until then)
                SendLogin(LoginCallsign);

                string line;
                while (_running && (line = _reader.ReadLine()) != null)
                {
                    LineReceived?.Invoke(this, line);
                }
            }
            catch (Exception ex)
            {
                if (_running)
                {
                    LastError = ex.Message;
                    Common.LogString("DXCluster (" + Host + ":" + Port + ") error: " + ex.Message);
                    setState(DxClusterConnectionState.Error);
                    return;
                }
            }
            finally
            {
                _running = false;
                cleanUp();
            }

            // only reached on a clean exit (server closed the stream / user disconnected)
            if (State != DxClusterConnectionState.Error)
                setState(DxClusterConnectionState.Disconnected);
        }

        private void cleanUp()
        {
            try { _client?.Close(); } catch { }
            try { _reader?.Dispose(); } catch { }
            try { _writer?.Dispose(); } catch { }

            _client = null;
            _reader = null;
            _writer = null;
        }

        private void setState(DxClusterConnectionState state)
        {
            if (State == state) return;

            State = state;
            StateChanged?.Invoke(this);
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
