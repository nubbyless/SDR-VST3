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
        public DateTime UtcTime { get; set; } = DateTime.UtcNow;
        public string Raw { get; set; } = "";
    }

    public static class DxClusterParser
    {
        private const string DxDeToken = "DX de";

        private static readonly Regex FrequencyRegex = new Regex(@"^\d+(?:\.\d+)?$", RegexOptions.Compiled);
        private static readonly Regex TimeRegex = new Regex(@"^(?<hh>\d{2})(?<mm>\d{2})Z?$", RegexOptions.Compiled);

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

            spot = new DxClusterSpot
            {
                Node = node ?? "",
                Spotter = spotter,
                Callsign = call,
                FrequencyHz = (long)Math.Round(freqKhz * 1000.0),
                Comment = comment,
                UtcTime = utc,
                Raw = raw
            };

            return true;
        }
    }
}
