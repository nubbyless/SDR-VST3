/*  DxClusterManager.cs

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
// DX Cluster - manages a set of parallel telnet node connections.                             //
//                                                                                             //
// The manager owns two things:                                                                //
//   * the live connection set (one DxClusterConnection per configured node), and              //
//   * a bounded in-memory list of every received spot (newest last), which backs the popup    //
//     window. A spot is only pushed into the shared SpotManager2 (i.e. drawn on the           //
//     panadapter) when the master "show on panadapter" flag AND the owning node's flag allow. //
//                                                                                             //
// Settings (node list, per-node color/visibility/auto-connect, login callsign) are persisted  //
// as a single JSON blob in the existing "Options" database table under the key "dx_cluster",   //
// mirroring how discovered_radios are stored (see setup.cs).                                   //
//============================================================================================//

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace Thetis
{
    public sealed class DxClusterNodeSettings
    {
        public string Host { get; set; } = "";
        public int Port { get; set; } = 23;
        public bool AutoConnect { get; set; } = true;
        public bool ShowOnPanadapter { get; set; } = true;
        public int ColorArgb { get; set; } = unchecked((int)0xFFFFC800); // gold, opaque
    }

    public sealed class DxClusterSettings
    {
        private const string OptionsTable = "Options";
        private const string OptionsKey = "dx_cluster";

        public string LoginCallsign { get; set; } = "";
        public bool ShowOnPanadapter { get; set; } = true;
        public string FilterBand { get; set; } = "All";
        public string FilterText { get; set; } = "";
        public List<DxClusterNodeSettings> Nodes { get; set; } = new List<DxClusterNodeSettings>();

        public string ToJson()
        {
            return JsonSerializer.Serialize(this);
        }

        public static DxClusterSettings FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new DxClusterSettings();

            try
            {
                DxClusterSettings settings = JsonSerializer.Deserialize<DxClusterSettings>(json);
                if (settings == null) return new DxClusterSettings();
                if (settings.Nodes == null) settings.Nodes = new List<DxClusterNodeSettings>();
                settings.Nodes.RemoveAll(n => n == null || string.IsNullOrWhiteSpace(n.Host));
                return settings;
            }
            catch
            {
                return new DxClusterSettings();
            }
        }

        public static DxClusterSettings Load()
        {
            Dictionary<string, string> options = DB.GetVarsDictionary(OptionsTable);
            if (options != null && options.TryGetValue(OptionsKey, out string json))
                return FromJson(json);

            return new DxClusterSettings();
        }

        public void Save()
        {
            Dictionary<string, string> options = DB.GetVarsDictionary(OptionsTable);
            if (options == null) options = new Dictionary<string, string>();

            options[OptionsKey] = ToJson();

            DB.SaveVarsDictionary(OptionsTable, ref options, true);
            DB.WriteDB();
        }
    }

    public static class DxClusterManager
    {
        public const string SourcePrefix = "dxcluster:";
        public const int MaxSpots = 3000;

        public static event Action<DxClusterSpot> SpotReceived;
        public static event Action<DxClusterConnection> ConnectionStateChanged;
        public static event Action<DxClusterConnection, string> RawLineReceived;

        private static readonly object _lock = new object();
        private static readonly Dictionary<string, DxClusterConnection> _connections = new Dictionary<string, DxClusterConnection>();
        private static readonly List<DxClusterSpot> _spots = new List<DxClusterSpot>();
        private static readonly Dictionary<string, DateTime> _recent = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        private static long _nextSpotId = 1;
        private static bool _recentDirty;

        public static Color DefaultSpotColor { get; set; } = Color.FromArgb(255, 255, 200, 0);

        // persisted state; callers should treat the returned object as owned by the manager
        public static DxClusterSettings Settings { get; private set; } = new DxClusterSettings();

        public static void LoadSettings()
        {
            Settings = DxClusterSettings.Load();
            if (Settings.Nodes == null) Settings.Nodes = new List<DxClusterNodeSettings>();
        }

        public static void SaveSettings()
        {
            try { Settings.Save(); }
            catch (Exception ex) { Common.LogString("DXCluster settings save failed: " + ex.Message); }
        }

        public static string NodeKey(string host, int port)
        {
            return ((host ?? "").Trim().ToLowerInvariant()) + ":" + port;
        }

        public static DxClusterNodeSettings FindNode(string host, int port)
        {
            string key = NodeKey(host, port);
            return Settings.Nodes.FirstOrDefault(n => n != null && NodeKey(n.Host, n.Port) == key);
        }

        public static DxClusterNodeSettings GetOrCreateNode(string host, int port)
        {
            DxClusterNodeSettings node = FindNode(host, port);
            if (node != null) return node;

            node = new DxClusterNodeSettings { Host = (host ?? "").Trim(), Port = port };
            Settings.Nodes.Add(node);
            return node;
        }

        public static DxClusterConnection AddConnection(string host, int port, string loginCallsign = "")
        {
            string key = NodeKey(host, port);

            // never create a second live connection for the same host:port
            DxClusterConnection existing = Connections.FirstOrDefault(c => NodeKey(c.Host, c.Port) == key);
            if (existing != null) return existing;

            DxClusterNodeSettings node = GetOrCreateNode(host, port);

            DxClusterConnection connection = new DxClusterConnection(host, port, loginCallsign)
            {
                Node = node,
                SpotColor = Color.FromArgb(node.ColorArgb),
                ShowOnPanadapter = node.ShowOnPanadapter,
            };

            connection.LineReceived += onLineReceived;
            connection.StateChanged += onStateChanged;

            lock (_lock)
            {
                _connections[connection.Id] = connection;
            }

            return connection;
        }

        public static void RemoveConnection(string id)
        {
            DxClusterConnection connection;

            lock (_lock)
            {
                if (!_connections.TryGetValue(id, out connection)) return;
                _connections.Remove(id);
            }

            connection.LineReceived -= onLineReceived;
            connection.StateChanged -= onStateChanged;
            connection.Dispose();
        }

        public static DxClusterConnection[] Connections
        {
            get
            {
                lock (_lock)
                {
                    return _connections.Values.ToArray();
                }
            }
        }

        public static void DisconnectAll()
        {
            foreach (DxClusterConnection connection in Connections)
                connection.Disconnect();
        }

        public static void ConnectAutoNodes()
        {
            foreach (DxClusterNodeSettings node in Settings.Nodes.ToArray())
            {
                if (node == null || !node.AutoConnect || string.IsNullOrWhiteSpace(node.Host)) continue;

                DxClusterConnection existing = Connections.FirstOrDefault(c =>
                    NodeKey(c.Host, c.Port) == NodeKey(node.Host, node.Port));
                if (existing != null) continue;

                AddConnection(node.Host, node.Port, Settings.LoginCallsign).Connect();
            }
        }

        public static bool IsConfigured(string host, int port)
        {
            return FindNode(host, port) != null;
        }

        public static DxClusterConnection EnsureConnected(string host, int port, string loginCallsign = "")
        {
            string key = NodeKey(host, port);
            DxClusterConnection connection = Connections.FirstOrDefault(c => NodeKey(c.Host, c.Port) == key);

            if (connection == null)
            {
                connection = AddConnection(host, port, loginCallsign);
                connection.Connect();
            }
            else if (connection.State == DxClusterConnectionState.Disconnected || connection.State == DxClusterConnectionState.Error)
            {
                connection.Connect();
            }

            return connection;
        }

        public static void DisconnectAndRemoveNode(string host, int port)
        {
            string key = NodeKey(host, port);

            DxClusterConnection connection = Connections.FirstOrDefault(c => NodeKey(c.Host, c.Port) == key);
            if (connection != null) RemoveConnection(connection.Id);

            DxClusterNodeSettings node = FindNode(host, port);
            if (node != null) Settings.Nodes.Remove(node);

            // drop this node's spots so they leave the panadapter and the list immediately
            lock (_lock)
            {
                _spots.RemoveAll(s => s != null && s.NodeKey == key);
            }

            SpotManager2.ClearSpotsBySource(SourcePrefix + (host ?? "").Trim());
        }

        public static DxClusterSpot[] GetSpots()
        {
            lock (_lock)
            {
                return _spots.ToArray();
            }
        }

        public static int SpotCount
        {
            get { lock (_lock) { return _spots.Count; } }
        }

        public static void ClearSpots()
        {
            lock (_lock)
            {
                _spots.Clear();
                _recent.Clear();
                _recentDirty = false;
            }

            SpotManager2.ClearSpotsBySource(SourcePrefix);
        }

        // re-evaluate which stored spots should be drawn on the panadapter
        public static void ResyncPanadapter()
        {
            SpotManager2.ClearSpotsBySource(SourcePrefix);

            if (Settings == null || !Settings.ShowOnPanadapter) return;

            DxClusterSpot[] snapshot;
            lock (_lock) snapshot = _spots.ToArray();

            foreach (DxClusterSpot spot in snapshot)
            {
                DxClusterNodeSettings node = FindNodeByKey(spot.NodeKey);
                if (node == null || !node.ShowOnPanadapter) continue;

                pushSpot(spot, Color.FromArgb(node.ColorArgb));
            }
        }

        public static DxClusterNodeSettings FindNodeByKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            return Settings.Nodes.FirstOrDefault(n => n != null && NodeKey(n.Host, n.Port) == key);
        }

        private static void onStateChanged(DxClusterConnection connection)
        {
            ConnectionStateChanged?.Invoke(connection);
        }

        private static void onLineReceived(DxClusterConnection connection, string line)
        {
            RawLineReceived?.Invoke(connection, line);

            if (!DxClusterParser.TryParse(line, connection.Host, out DxClusterSpot spot)) return;

            spot.NodeKey = NodeKey(connection.Host, connection.Port);
            addSpot(connection, spot);
        }

        private static void addSpot(DxClusterConnection connection, DxClusterSpot spot)
        {
            spot.Id = System.Threading.Interlocked.Increment(ref _nextSpotId);

            lock (_lock)
            {
                // suppress repeats of the same call/frequency seen within the last minute
                string dedupKey = spot.Callsign + "|" + spot.FrequencyHz.ToString(CultureInfo.InvariantCulture);
                if (_recent.TryGetValue(dedupKey, out DateTime seen) && (DateTime.UtcNow - seen).TotalSeconds < 60)
                    return;

                _recent[dedupKey] = DateTime.UtcNow;
                _recentDirty = true;

                _spots.Add(spot);
                if (_spots.Count > MaxSpots)
                    _spots.RemoveRange(0, _spots.Count - MaxSpots);

                if (_recentDirty && _recent.Count > 6000)
                {
                    pruneRecent();
                    _recentDirty = false;
                }
            }

            if (Settings != null && Settings.ShowOnPanadapter && connection.ShowOnPanadapter)
                pushSpot(spot, connection.SpotColor);

            SpotReceived?.Invoke(spot);
        }

        private static void pruneRecent()
        {
            DateTime cutoff = DateTime.UtcNow.AddMinutes(-1);

            List<string> dead = new List<string>();
            foreach (KeyValuePair<string, DateTime> kv in _recent)
                if (kv.Value < cutoff) dead.Add(kv.Key);

            foreach (string key in dead) _recent.Remove(key);
        }

        private static void pushSpot(DxClusterSpot spot, Color color)
        {
            // cluster spots carry no mode; default to SSB and let the frequency decide USB/LSB
            DSPMode mode = SpotManager2.SpotModeNumberToDSPMode(0, spot.FrequencyHz);

            SpotManager2.JsonSpotData json = new SpotManager2.JsonSpotData
            {
                Spotter = spot.Spotter,
                Comment = spot.Comment,
                UtcTime = spot.UtcTime.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'Z'", CultureInfo.InvariantCulture)
            };

            SpotManager2.AddSpot(
                spot.Callsign,
                mode,
                spot.FrequencyHz,
                color,
                spot.Comment,
                json,
                SourcePrefix + spot.Node);
        }
    }
}