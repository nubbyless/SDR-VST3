/*  SigIdDatabase.cs

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
// SigID - reader for the Artemis signal-identification database (data.sqlite).                //
//                                                                                              //
// The shipped database is a Peewee/Artemis SQLite file with 11 tables.  Everything is read   //
// once into immutable in-memory lists - the whole file is ~1 MB and a few thousand rows, so   //
// there is no reason to hit the disk during an analysis cycle.                                 //
//                                                                                              //
// Schema (upper case in the shipped file, SQLite identifiers are case insensitive):          //
//   INFO      (DATE, EDITABLE, NAME, VERSION)                                                 //
//   SIGNALS   (SIG_ID, DESCRIPTION, NAME, URL, SINCE_VERSION)                                  //
//   BANDWIDTH (BAND_ID, DESCRIPTION, SIG_ID, VALUE)          VALUE in Hz                       //
//   ACF       (ACF_ID, DESCRIPTION, SIG_ID, VALUE)            VALUE = PRI in ms                //
//   MODULATION(MDL_ID, DESCRIPTION, SIG_ID, VALUE)                                               //
//   MODE      (MOD_ID, DESCRIPTION, SIG_ID, VALUE)                                              //
//   LOCATION  (LOC_ID, DESCRIPTION, SIG_ID, VALUE)                                              //
//   FREQUENCY (FREQ_ID, DESCRIPTION, SIG_ID, VALUE)          VALUE in Hz                       //
//   CATEGORY  (CAT_ID, CLB_ID, SIG_ID)                       + CATEGORYLABEL (CLB_ID, VALUE)    //
//   DOCUMENTS (DOC_ID, DESCRIPTION, EXTENSION, NAME, PREVIEW, SIG_ID, TYPE)                    //
//                                                                                              //
// Media files are named <DOC_ID>.<EXTENSION> and live in the "media" sub-folder.              //
//============================================================================================//

using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using Microsoft.Data.Sqlite;

namespace Thetis
{
    /// <summary>One autocorrelator entry: a characteristic pulse repetition interval.</summary>
    public sealed class SigIdAcfEntry
    {
        public double ValueMs;
        public string Description = string.Empty;

        public SigIdAcfEntry(double valueMs, string description)
        {
            ValueMs = valueMs;
            Description = description ?? string.Empty;
        }
    }

    /// <summary>One signal entry, with all of its structured parameters.</summary>
    public sealed class SigIdSignal
    {
        public int SigId;
        public string Name = string.Empty;
        public string Description = string.Empty;
        public string Url = string.Empty;
        public int SinceVersion;

        public readonly List<int> Bandwidths = new List<int>();        // Hz
        public readonly List<SigIdAcfEntry> AcfEntries = new List<SigIdAcfEntry>();
        public readonly List<string> Modulations = new List<string>();
        public readonly List<string> Modes = new List<string>();
        public readonly List<string> Categories = new List<string>();
        public readonly List<string> Locations = new List<string>();
        public readonly List<long> Frequencies = new List<long>();     // Hz

        /// <summary>Media file name of the reference audio clip, e.g. "410.ogg". Empty when absent.</summary>
        public string AudioFileName = string.Empty;

        /// <summary>Media file name of the waterfall snapshot, e.g. "411.png". Empty when absent.</summary>
        public string ImageFileName = string.Empty;

        /// <summary>First paragraph of the description, used for the candidate list.</summary>
        public string ShortDescription
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Description)) return string.Empty;
                string text = Description;

                // the Artemis DB stores the summary as the leading "### SUMMARY" markdown block
                const string marker = "### SUMMARY";
                int start = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (start >= 0)
                {
                    text = text.Substring(start + marker.Length);
                    int next = text.IndexOf("###", StringComparison.Ordinal);
                    if (next >= 0) text = text.Substring(0, next);
                }

                text = text.Replace("\r", " ").Replace("\n", " ").Trim();
                while (text.Contains("  ")) text = text.Replace("  ", " ");
                return text.Length > 240 ? text.Substring(0, 240) : text;
            }
        }
    }

    /// <summary>
    /// In-memory view of the installed signal-identification database.
    /// </summary>
    public sealed class SigIdDatabase
    {
        private readonly Dictionary<int, SigIdSignal> _byId = new Dictionary<int, SigIdSignal>();

        public bool IsOpen { get; private set; }

        /// <summary>
        /// Reason the most recent Open() attempt failed.  Empty when the database opened
        /// cleanly.  The UI uses this to explain why identification is unavailable.
        /// </summary>
        public static string LastOpenError { get; private set; } = string.Empty;

        /// <summary>Database revision number, e.g. 74 for the v74 release.</summary>
        public int Version { get; private set; }

        /// <summary>Release date string as stored by the packager, e.g. "2026-07-18".</summary>
        public string ReleaseDate { get; private set; } = string.Empty;

        /// <summary>Database name as stored by the packager, e.g. "SigID".</summary>
        public string Name { get; private set; } = string.Empty;

        public List<SigIdSignal> Signals { get; private set; } = new List<SigIdSignal>();

        /// <summary>True when the database exposes at least one signal.</summary>
        public bool HasSignals { get { return Signals.Count > 0; } }

        /// <summary>
        /// Opens the installed database.  Returns null when the database is not installed or
        /// cannot be read - the caller must treat that as "identification unavailable".
        /// </summary>
        public static SigIdDatabase Open()
        {
            return Open(SigIdPaths.DatabaseFile);
        }

        /// <summary>Opens a specific database file.  Returns null on any failure.</summary>
        public static SigIdDatabase Open(string databaseFile)
        {
            if (string.IsNullOrWhiteSpace(databaseFile)) return null;
            if (!File.Exists(databaseFile)) return null;

            SigIdDatabase db = new SigIdDatabase();
            try
            {
                db.Load(databaseFile);
                db.IsOpen = true;
                LastOpenError = string.Empty;
                return db;
            }
            catch (Exception ex)
            {
                LastOpenError = ex.GetType().Name + ": " + ex.Message;
                return null;
            }
        }

        public SigIdSignal Find(int sigId)
        {
            SigIdSignal signal;
            return _byId.TryGetValue(sigId, out signal) ? signal : null;
        }

        /// <summary>Absolute path of a media asset, or an empty string when it is not present.</summary>
        public string MediaPath(string fileName)
        {
            return SigIdPaths.ResolveMediaFile(fileName);
        }

        private void Load(string databaseFile)
        {
            string connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = databaseFile,
                Mode = SqliteOpenMode.ReadOnly,
                Cache = SqliteCacheMode.Shared,
                Pooling = false
            }.ToString();

            using (SqliteConnection connection = new SqliteConnection(connectionString))
            {
                connection.Open();

                LoadInfo(connection);
                LoadSignals(connection);

                // every other table is optional: the Artemis schema changes between
                // revisions and a missing table must degrade the matcher, not break it
                LoadOptional(connection, LoadBandwidths);
                LoadOptional(connection, LoadAcf);
                LoadOptional(connection, LoadModulation);
                LoadOptional(connection, LoadMode);
                LoadOptional(connection, LoadLocation);
                LoadOptional(connection, LoadFrequency);
                LoadOptional(connection, LoadCategory);
                LoadOptional(connection, LoadDocuments);
            }
        }

        /// <summary>
        /// Runs a table loader and swallows "no such table" errors.  Any other failure is
        /// also tolerated here - a partially readable database is still more useful than none.
        /// </summary>
        private static void LoadOptional(SqliteConnection connection, Action<SqliteConnection> loader)
        {
            try
            {
                loader(connection);
            }
            catch (SqliteException)
            {
                // the table is not part of this database revision
            }
            catch (InvalidOperationException)
            {
                // the column layout changed
            }
        }

        private void LoadInfo(SqliteConnection connection)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT DATE, NAME, VERSION FROM INFO LIMIT 1";
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    if (!reader.Read()) return;
                    if (!reader.IsDBNull(0)) ReleaseDate = reader.GetString(0);
                    if (!reader.IsDBNull(1)) Name = reader.GetString(1);
                    if (!reader.IsDBNull(2)) Version = reader.GetInt32(2);
                }
            }
        }

        private void LoadSignals(SqliteConnection connection)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT SIG_ID, NAME, DESCRIPTION, URL, SINCE_VERSION FROM SIGNALS";
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        SigIdSignal signal = new SigIdSignal();
                        signal.SigId = reader.GetInt32(0);
                        signal.Name = reader.IsDBNull(1) ? string.Empty : reader.GetString(1);
                        signal.Description = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                        signal.Url = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                        signal.SinceVersion = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);
                        Signals.Add(signal);
                        _byId[signal.SigId] = signal;
                    }
                }
            }
        }

        private void LoadBandwidths(SqliteConnection connection)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT SIG_ID, VALUE FROM BANDWIDTH";
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        SigIdSignal signal = Find(reader.GetInt32(0));
                        if (signal == null) continue;
                        if (reader.IsDBNull(1)) continue;
                        int value = reader.GetInt32(1);
                        if (!signal.Bandwidths.Contains(value)) signal.Bandwidths.Add(value);
                    }
                }
            }
        }

        private void LoadAcf(SqliteConnection connection)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT SIG_ID, VALUE, DESCRIPTION FROM ACF";
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        SigIdSignal signal = Find(reader.GetInt32(0));
                        if (signal == null) continue;
                        if (reader.IsDBNull(1)) continue;
                        string description = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                        signal.AcfEntries.Add(new SigIdAcfEntry(reader.GetDouble(1), description));
                    }
                }
            }
        }

        private void LoadModulation(SqliteConnection connection)
        {
            LoadStringColumn(connection, "SELECT SIG_ID, VALUE FROM MODULATION", delegate(SigIdSignal s, string v)
            {
                if (!s.Modulations.Contains(v)) s.Modulations.Add(v);
            });
        }

        private void LoadMode(SqliteConnection connection)
        {
            LoadStringColumn(connection, "SELECT SIG_ID, VALUE FROM MODE", delegate(SigIdSignal s, string v)
            {
                if (!s.Modes.Contains(v)) s.Modes.Add(v);
            });
        }

        private void LoadLocation(SqliteConnection connection)
        {
            LoadStringColumn(connection, "SELECT SIG_ID, VALUE FROM LOCATION", delegate(SigIdSignal s, string v)
            {
                if (!s.Locations.Contains(v)) s.Locations.Add(v);
            });
        }

        private void LoadFrequency(SqliteConnection connection)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT SIG_ID, VALUE FROM FREQUENCY";
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        SigIdSignal signal = Find(reader.GetInt32(0));
                        if (signal == null) continue;
                        if (reader.IsDBNull(1)) continue;
                        long value = reader.GetInt64(1);
                        if (!signal.Frequencies.Contains(value)) signal.Frequencies.Add(value);
                    }
                }
            }
        }

        private void LoadCategory(SqliteConnection connection)
        {
            Dictionary<int, string> labels = new Dictionary<int, string>();

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT CLB_ID, VALUE FROM CATEGORYLABEL";
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        if (reader.IsDBNull(1)) continue;
                        labels[reader.GetInt32(0)] = reader.GetString(1);
                    }
                }
            }

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = "SELECT SIG_ID, CLB_ID FROM CATEGORY";
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        SigIdSignal signal = Find(reader.GetInt32(0));
                        if (signal == null) continue;
                        if (reader.IsDBNull(1)) continue;
                        string label;
                        if (!labels.TryGetValue(reader.GetInt32(1), out label)) continue;
                        if (!signal.Categories.Contains(label)) signal.Categories.Add(label);
                    }
                }
            }
        }

        private void LoadDocuments(SqliteConnection connection)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText =
                    "SELECT SIG_ID, DOC_ID, EXTENSION, TYPE, PREVIEW FROM DOCUMENTS WHERE TYPE IN ('Audio','Image')";
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        SigIdSignal signal = Find(reader.GetInt32(0));
                        if (signal == null) continue;

                        int docId = reader.GetInt32(1);
                        string extension = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
                        string type = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);
                        int preview = reader.IsDBNull(4) ? 0 : reader.GetInt32(4);

                        string fileName = docId.ToString() + "." + extension;

                        if (string.Equals(type, "Audio", StringComparison.OrdinalIgnoreCase))
                        {
                            // prefer the asset flagged as the preview
                            if (string.IsNullOrEmpty(signal.AudioFileName) || preview == 1)
                                signal.AudioFileName = fileName;
                        }
                        else if (string.Equals(type, "Image", StringComparison.OrdinalIgnoreCase))
                        {
                            if (string.IsNullOrEmpty(signal.ImageFileName) || preview == 1)
                                signal.ImageFileName = fileName;
                        }
                    }
                }
            }
        }

        private void LoadStringColumn(SqliteConnection connection, string sql, Action<SigIdSignal, string> add)
        {
            using (SqliteCommand command = connection.CreateCommand())
            {
                command.CommandText = sql;
                using (SqliteDataReader reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        SigIdSignal signal = Find(reader.GetInt32(0));
                        if (signal == null) continue;
                        if (reader.IsDBNull(1)) continue;
                        add(signal, reader.GetString(1));
                    }
                }
            }
        }
    }
}
