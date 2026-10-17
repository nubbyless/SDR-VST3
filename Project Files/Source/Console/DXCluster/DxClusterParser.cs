/*  DxClusterParser.cs

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
// DX Cluster - telnet spot line parser.                                                       //
// Parses the standard DX cluster "DX de <spotter>: <khz> <call> <comment> <hhmm>Z" format    //
// into a normalised DxClusterSpot. Pure/stateless and culture invariant.                      //
//============================================================================================//

using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Thetis
{
    public sealed class DxClusterSpot
    {
        public long Id { get; set; }
        public string Node { get; set; } = "";
        public string NodeKey { get; set; } = "";
        public string Spotter { get; set; } = "";
        public string Callsign { get; set; } = "";
        public long FrequencyHz { get; set; }
        public string Comment { get; set; } = "";
        public string Country { get; set; } = "";
        public string CountryCode { get; set; } = ""; // ISO 3166-1 alpha-2, used for the SWL flag
        public string Language { get; set; } = "";
        public string Mode { get; set; } = "";
        public int ModeNumber { get; set; }
        public DateTime UtcTime { get; set; } = DateTime.UtcNow;
        public string Raw { get; set; } = "";

        public bool IsSwl { get; set; }              // broadcast (EiBi) entry rather than a cluster spot
        public long SwlSecondsToLive { get; set; }   // panadapter expiry for SWL spots
        public string Key { get; set; } = "";         // stable identity for SWL entries
    }

    public static class DxClusterParser
    {
        private const string DxDeToken = "DX de";

        private static readonly Regex FrequencyRegex = new Regex(@"^\d+(?:\.\d+)?$", RegexOptions.Compiled);
        private static readonly Regex TimeRegex = new Regex(@"^(?<hh>\d{2})(?<mm>\d{2})Z?$", RegexOptions.Compiled);

        // mode is derived from the spot frequency against the band plan; segments are in kHz
        // and cover the common HF/VHF sub-bands (CW / data / SSB / FM)
        private struct BandSegment
        {
            public readonly long Low;
            public readonly long High;
            public readonly string Label;
            public readonly int Number;

            public BandSegment(long lowKhz, long highKhz, string label, int number)
            {
                Low = lowKhz * 1000L;
                High = highKhz * 1000L;
                Label = label;
                Number = number;
            }
        }

        private static readonly BandSegment[] BandPlan = new BandSegment[]
        {
            new BandSegment(1800, 1840, "CW", 1),
            new BandSegment(1840, 2000, "SSB", 0),

            new BandSegment(3500, 3570, "CW", 1),
            new BandSegment(3570, 3600, "DIGI", 10),
            new BandSegment(3600, 4000, "SSB", 0),

            new BandSegment(5000, 5500, "SSB", 0),

            new BandSegment(7000, 7040, "CW", 1),
            new BandSegment(7040, 7100, "DIGI", 10),
            new BandSegment(7100, 7300, "SSB", 0),

            new BandSegment(10100, 10130, "CW", 1),
            new BandSegment(10130, 10150, "DIGI", 10),

            new BandSegment(14000, 14070, "CW", 1),
            new BandSegment(14070, 14100, "DIGI", 10),
            new BandSegment(14100, 14350, "SSB", 0),

            new BandSegment(18068, 18095, "CW", 1),
            new BandSegment(18095, 18110, "DIGI", 10),
            new BandSegment(18110, 18168, "SSB", 0),

            new BandSegment(21000, 21070, "CW", 1),
            new BandSegment(21070, 21090, "DIGI", 10),
            new BandSegment(21090, 21450, "SSB", 0),

            new BandSegment(24890, 24915, "CW", 1),
            new BandSegment(24915, 24930, "DIGI", 10),
            new BandSegment(24930, 24990, "SSB", 0),

            new BandSegment(28000, 28070, "CW", 1),
            new BandSegment(28070, 28150, "DIGI", 10),
            new BandSegment(28150, 29000, "SSB", 0),
            new BandSegment(29000, 29700, "FM", 11),

            new BandSegment(50000, 50100, "CW", 1),
            new BandSegment(50100, 50500, "SSB", 0),
            new BandSegment(50500, 54000, "FM", 11),
        };

        private static void inferModeFromFrequency(long hz, out string label, out int number)
        {
            label = "";
            number = 0;

            for (int i = 0; i < BandPlan.Length; i++)
            {
                if (hz < BandPlan[i].Low || hz >= BandPlan[i].High) continue;

                label = BandPlan[i].Label;
                number = BandPlan[i].Number;
                return;
            }
        }

        // country/entity of the spotted callsign, from the bundled cty.dat prefix database
        private static string lookupCountry(string callsign)
        {
            try
            {
                CountryData.PrefixData data = CountryData.GetCallsignData(callsign);
                return data == null ? "" : (data.Country ?? "");
            }
            catch
            {
                return "";
            }
        }

        public static bool TryParse(string line, out DxClusterSpot spot)
        {
            return TryParse(line, "", out spot);
        }

        public static bool TryParse(string line, string node, out DxClusterSpot spot)
        {
            spot = null;

            if (string.IsNullOrWhiteSpace(line)) return false;

            string raw = line.Trim();
            if (raw.Length == 0) return false;

            // tolerate leading node prompts / control characters by locating the spot marker
            int marker = raw.IndexOf(DxDeToken, StringComparison.OrdinalIgnoreCase);
            if (marker < 0) return false;

            string s = raw.Substring(marker);

            int colon = s.IndexOf(':');
            if (colon < 0) return false;

            string spotter = s.Substring(DxDeToken.Length, colon - DxDeToken.Length).Trim();
            string rest = s.Substring(colon + 1).Trim();

            string[] tokens = rest.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2) return false;

            if (!FrequencyRegex.IsMatch(tokens[0])) return false;
            if (!double.TryParse(tokens[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double freqKhz)) return false;
            if (freqKhz <= 0 || freqKhz > 1000000) return false;

            string call = tokens[1].ToUpperInvariant();
            if (call.Length == 0) return false;

            // trailing HHMMZ is optional; only treat as a time when a comment token precedes it
            int commentEnd = tokens.Length;
            DateTime utc = DateTime.UtcNow;

            if (tokens.Length >= 3)
            {
                Match tm = TimeRegex.Match(tokens[tokens.Length - 1]);
                if (tm.Success)
                {
                    int hh = int.Parse(tm.Groups["hh"].Value, CultureInfo.InvariantCulture);
                    int mm = int.Parse(tm.Groups["mm"].Value, CultureInfo.InvariantCulture);

                    if (hh < 24 && mm < 60)
                    {
                        commentEnd = tokens.Length - 1;
                        DateTime now = DateTime.UtcNow;
                        utc = new DateTime(now.Year, now.Month, now.Day, hh, mm, 0, DateTimeKind.Utc);
                    }
                }
            }

            int commentCount = commentEnd - 2;
            string comment = commentCount > 0 ? string.Join(" ", tokens, 2, commentCount) : "";

            long freqHz = (long)Math.Round(freqKhz * 1000.0);

            inferModeFromFrequency(freqHz, out string modeLabel, out int modeNumber);

            spot = new DxClusterSpot
            {
                Node = node ?? "",
                Spotter = spotter,
                Callsign = call,
                FrequencyHz = freqHz,
                Comment = comment,
                Country = lookupCountry(call),
                Mode = modeLabel,
                ModeNumber = modeNumber,
                UtcTime = utc,
                Raw = raw
            };

            return true;
        }
    }
}
