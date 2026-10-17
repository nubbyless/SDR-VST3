/*  DxClusterSwl.cs

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
// DX Cluster - SWL (broadcast) support.                                                       //
//                                                                                             //
// The CSV schedule is the EiBi shortwave broadcast list (eibispace.de), a semicolon-separated  //
// file with these columns:                                                                    //
//   0 kHz  1 Time(UTC) "HHMM-HHMM"  2 Days  3 ITU  4 Station  5 Language  6 Target             //
//   7 Remarks  8 Persistence  9 StartDate  10 StopDate                                        //
//                                                                                             //
// Only entries whose time window contains the current UTC minute are surfaced ("on air now"). //
// Country name + ISO flag code come from the embedded ITU table below; language names are read  //
// from the EiBi README.TXT legend at runtime (falling back to the raw code).                   //
//============================================================================================//

using System;
using System.Collections.Generic;
using System.Globalization;

namespace Thetis
{
    public sealed class DxClusterSwlEntry
    {
        public long FrequencyHz;
        public int StartMin;                 // minutes from 00:00 UTC
        public int StopMin;                  // minutes from 00:00 UTC (1440 = 24:00)
        public string TimeText = "";         // "HHMM-HHMM"
        public string Days = "";             // raw day spec
        public bool[] DayMask;               // [0..6] Mon..Sun, null = every day
        public string Itu = "";
        public string Station = "";
        public string LanguageCode = "";
        public string Target = "";
        public string Remarks = "";
        public int Persistence;
        public string StartDate = "";
        public string StopDate = "";
    }

    public static class DxClusterSwl
    {
        public const string DefaultUrlTemplate = "http://www.eibispace.de/dx/sked-{0}.csv";
        public const string ReadmeUrl = "http://www.eibispace.de/dx/README.TXT";

        //----------------------------------------------------------------------------------------
        // ITU country code -> ISO 3166-1 alpha-2 (flag atlas) + display name.
        //----------------------------------------------------------------------------------------
        private const string CountryTable =
            "AFS=ZA|South Africa;ALG=DZ|Algeria;ALS=US|Alaska;ARG=AR|Argentina;AUS=AU|Australia;AZR=PT|Azores;" +
            "B=BR|Brazil;BEL=BE|Belgium;BER=BM|Bermuda;BHR=BH|Bahrain;BLR=BY|Belarus;BOL=BO|Bolivia;" +
            "BUL=BG|Bulgaria;CAN=CA|Canada;CHL=CL|Chile;CHN=CN|China;CLA=|Clandestine;CLM=CO|Colombia;" +
            "CLN=LK|Sri Lanka;CNR=ES|Canary Islands;COG=CG|Congo;CTI=CI|Ivory Coast;CUB=CU|Cuba;CVA=VA|Vatican;" +
            "CYM=KY|Cayman Islands;CZE=CZ|Czech Republic;D=DE|Germany;DNK=DK|Denmark;E=ES|Spain;EGY=EG|Egypt;" +
            "EQA=EC|Ecuador;ETH=ET|Ethiopia;F=FR|France;FIN=FI|Finland;FJI=FJ|Fiji;G=GB|United Kingdom;" +
            "GRC=GR|Greece;GUM=GU|Guam;HKG=HK|Hong Kong;HNG=HU|Hungary;HOL=NL|Netherlands;HWA=US|Hawaii;" +
            "I=IT|Italy;IND=IN|India;INS=ID|Indonesia;IRL=IE|Ireland;IRN=IR|Iran;ISL=IS|Iceland;" +
            "ISR=IL|Israel;J=JP|Japan;KEN=KE|Kenya;KGZ=KG|Kyrgyzstan;KOR=KR|South Korea;KRE=KP|North Korea;" +
            "KWT=KW|Kuwait;LBR=LR|Liberia;MDG=MG|Madagascar;MEX=MX|Mexico;MLA=MY|Malaysia;MLI=ML|Mali;" +
            "MNG=MN|Mongolia;MRT=MQ|Martinique;MYA=MM|Myanmar;NCG=NI|Nicaragua;NOR=NO|Norway;NZL=NZ|New Zealand;" +
            "OCE=PF|French Polynesia;PAK=PK|Pakistan;PHL=PH|Philippines;PNR=PA|Panama;POL=PL|Poland;PRU=PE|Peru;" +
            "PTR=PR|Puerto Rico;ROU=RO|Romania;RUS=RU|Russia;S=SE|Sweden;SEN=SN|Senegal;SEY=SC|Seychelles;" +
            "SLM=SB|Solomon Islands;SNG=SG|Singapore;SOM=SO|Somalia;SUI=CH|Switzerland;SVK=SK|Slovakia;SWZ=SZ|Eswatini;" +
            "TCD=TD|Chad;THA=TH|Thailand;TRD=TT|Trinidad and Tobago;TUN=TN|Tunisia;TUR=TR|Turkey;TWN=TW|Taiwan;" +
            "UKR=UA|Ukraine;UN=|United Nations;USA=US|United States;UZB=UZ|Uzbekistan;VTN=VN|Vietnam;VUT=VU|Vanuatu;" +
            "XUU=|Unidentified;";

        private static readonly Dictionary<string, string[]> _countries = buildCountryTable();

        private static readonly object _langLock = new object();
        private static Dictionary<string, string> _languageNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static Dictionary<string, string[]> buildCountryTable()
        {
            Dictionary<string, string[]> map = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

            foreach (string part in CountryTable.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                if (eq <= 0) continue;

                string code = part.Substring(0, eq).Trim();
                string value = part.Substring(eq + 1);
                int bar = value.IndexOf('|');
                if (bar < 0) continue;

                string iso = value.Substring(0, bar).Trim();
                string name = value.Substring(bar + 1).Trim();
                map[code] = new[] { iso, name };
            }

            return map;
        }

        //----------------------------------------------------------------------------------------
        // Season-derived schedule URL. EiBi names its files sked-Xzz.csv where X is a=cSummer /
        // b=winter and zz is the two-digit year the season STARTS. The winter file for 2025/26 is
        // therefore b25; Jan-Mar still belongs to the previous year's winter season.
        //----------------------------------------------------------------------------------------
        public static string SeasonUrl(DateTime utc)
        {
            int year = utc.Year;
            int month = utc.Month;
            char season;

            if (month >= 4 && month <= 10)
            {
                season = 'a';
            }
            else if (month >= 11)
            {
                season = 'b';
            }
            else
            {
                season = 'b';
                year -= 1;
            }

            string code = season + (year % 100).ToString("00", CultureInfo.InvariantCulture);
            return string.Format(CultureInfo.InvariantCulture, DefaultUrlTemplate, code);
        }

        public static string DefaultUrl()
        {
            return SeasonUrl(DateTime.UtcNow);
        }

        //----------------------------------------------------------------------------------------
        // CSV parsing.
        //----------------------------------------------------------------------------------------
        public static List<DxClusterSwlEntry> ParseCsv(string text)
        {
            List<DxClusterSwlEntry> list = new List<DxClusterSwlEntry>();
            if (string.IsNullOrEmpty(text)) return list;

            string[] lines = text.Split('\n');
            foreach (string rawLine in lines)
            {
                string line = rawLine.TrimEnd('\r');
                if (line.Length == 0) continue;

                string[] f = line.Split(';');
                if (f.Length < 11) continue;

                string time = f[1].Trim();
                int dash = time.IndexOf('-');
                if (dash <= 0) continue; // header ("Time(UTC):93") or malformed

                if (!TryParseHHMM(time.Substring(0, dash).Trim(), out int startMin)) continue;
                if (!TryParseHHMM(time.Substring(dash + 1).Trim(), out int stopMin)) continue;

                if (!double.TryParse(f[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double khz)) continue;
                if (khz <= 0) continue;

                DxClusterSwlEntry entry = new DxClusterSwlEntry
                {
                    FrequencyHz = (long)Math.Round(khz * 1000.0),
                    StartMin = startMin,
                    StopMin = stopMin,
                    TimeText = f[1].Trim(),
                    Days = f[2].Trim(),
                    Itu = f[3].Trim().ToUpperInvariant(),
                    Station = f[4].Trim(),
                    LanguageCode = f[5].Trim(),
                    Target = f[6].Trim(),
                    Remarks = f[7].Trim(),
                    Persistence = int.TryParse(f[8].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int p) ? p : 0,
                    StartDate = f[9].Trim(),
                    StopDate = f[10].Trim(),
                };

                entry.DayMask = parseDays(entry.Days);
                list.Add(entry);
            }

            return list;
        }

        private static bool TryParseHHMM(string s, out int minutes)
        {
            minutes = 0;
            if (s == null || s.Length != 4) return false;

            if (!int.TryParse(s.Substring(0, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int hh)) return false;
            if (!int.TryParse(s.Substring(2, 2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int mm)) return false;
            if (mm > 59) return false;

            if (hh == 24)
            {
                if (mm != 0) return false;
                minutes = 1440;
                return true;
            }

            if (hh > 24) return false;
            minutes = hh * 60 + mm;
            return true;
        }

        //----------------------------------------------------------------------------------------
        // "On air now" selection.
        //----------------------------------------------------------------------------------------
        public static List<DxClusterSwlEntry> GetActive(List<DxClusterSwlEntry> entries, DateTime utc)
        {
            List<DxClusterSwlEntry> result = new List<DxClusterSwlEntry>();
            if (entries == null) return result;

            int nowMin = utc.Hour * 60 + utc.Minute;
            int dow = ((int)utc.DayOfWeek + 6) % 7; // 0 = Monday .. 6 = Sunday

            foreach (DxClusterSwlEntry e in entries)
            {
                if (e == null) continue;
                if (!dayMatches(e, dow)) continue;

                bool inWindow;
                if (e.StopMin <= e.StartMin)
                    inWindow = nowMin >= e.StartMin || nowMin < e.StopMin; // wraps midnight
                else
                    inWindow = nowMin >= e.StartMin && nowMin < e.StopMin;

                if (inWindow) result.Add(e);
            }

            return result;
        }

        public static bool TryGetWindowUtc(DxClusterSwlEntry e, DateTime utc, out DateTime startUtc, out DateTime stopUtc)
        {
            startUtc = utc.Date.AddMinutes(e.StartMin);
            stopUtc = utc.Date.AddMinutes(e.StopMin);
            if (e.StopMin <= e.StartMin) stopUtc = stopUtc.AddDays(1);
            return true;
        }

        private static bool dayMatches(DxClusterSwlEntry e, int dow)
        {
            if (e.DayMask == null) return true;
            return dow >= 0 && dow < 7 && e.DayMask[dow];
        }

        //----------------------------------------------------------------------------------------
        // Day-of-week parsing (best effort). Anything that is not a clean weekly spec (irr, alt,
        // test, 24Oct, 156, 1.Sa, Last7, MF-15, ...) is treated as "every day" (null mask).
        //----------------------------------------------------------------------------------------
        private static bool[] parseDays(string spec)
        {
            if (string.IsNullOrWhiteSpace(spec)) return null;

            bool[] mask = new bool[7];
            bool any = false;

            foreach (string rawGroup in spec.Trim().Split(','))
            {
                string g = rawGroup.Trim();
                if (g.Length == 0) continue;

                int dash = g.IndexOf('-');
                if (dash > 0)
                {
                    int a = dayIndex(g.Substring(0, dash));
                    int b = dayIndex(g.Substring(dash + 1));
                    if (a < 0 || b < 0) return null;

                    for (int d = a; ; d = (d + 1) % 7)
                    {
                        mask[d] = true;
                        any = true;
                        if (d == b) break;
                    }
                }
                else if (g.Length == 2)
                {
                    int d = dayIndex(g);
                    if (d < 0) return null;
                    mask[d] = true;
                    any = true;
                }
                else if (g.Length % 2 == 0)
                {
                    for (int i = 0; i < g.Length; i += 2)
                    {
                        int d = dayIndex(g.Substring(i, 2));
                        if (d < 0) return null;
                        mask[d] = true;
                        any = true;
                    }
                }
                else
                {
                    return null;
                }
            }

            return any ? mask : null;
        }

        private static int dayIndex(string d)
        {
            switch (d)
            {
                case "Mo": return 0;
                case "Tu": return 1;
                case "We": return 2;
                case "Th": return 3;
                case "Fr": return 4;
                case "Sa": return 5;
                case "Su": return 6;
                default: return -1;
            }
        }

        public static string EntryKey(DxClusterSwlEntry e)
        {
            return e.FrequencyHz.ToString(CultureInfo.InvariantCulture)
                + "|" + e.Station
                + "|" + e.TimeText
                + "|" + e.Itu
                + "|" + e.Target;
        }

        //----------------------------------------------------------------------------------------
        // Country lookup.
        //----------------------------------------------------------------------------------------
        public static string CountryIso(string itu)
        {
            if (string.IsNullOrWhiteSpace(itu)) return "";
            return _countries.TryGetValue(itu.Trim(), out string[] v) ? (v[0] ?? "") : "";
        }

        public static string CountryName(string itu)
        {
            if (string.IsNullOrWhiteSpace(itu)) return "";
            itu = itu.Trim();
            if (_countries.TryGetValue(itu, out string[] v) && !string.IsNullOrWhiteSpace(v[1])) return v[1];
            return itu;
        }

        //----------------------------------------------------------------------------------------
        // Language legend (parsed from EiBi README.TXT at runtime).
        //----------------------------------------------------------------------------------------
        public static void SetLanguageLegend(string readmeText)
        {
            if (string.IsNullOrWhiteSpace(readmeText)) return;

            Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string[] lines = readmeText.Split('\n');
            bool inSection = false;

            foreach (string raw in lines)
            {
                string line = raw.TrimEnd('\r');
                string trimmed = line.Trim();

                if (trimmed.StartsWith("I) Language codes", StringComparison.OrdinalIgnoreCase))
                {
                    inSection = true;
                    continue;
                }
                if (inSection && trimmed.StartsWith("II)", StringComparison.OrdinalIgnoreCase)) break;
                if (!inSection || trimmed.Length == 0) continue;

                // real entries are a code column followed by a 2+ space gap then the name; prose
                // lines in the section preamble use single spaces and are skipped by this test
                string t = line.TrimStart();
                int sp = t.IndexOf("  ", StringComparison.Ordinal);
                if (sp <= 0) continue;

                string code = t.Substring(0, sp).Trim();
                if (code.Length == 0 || code.Length > 4 || code[0] == '[') continue;

                string name = shortLanguageName(t.Substring(sp).Trim());
                if (string.IsNullOrEmpty(name)) continue;

                map[code] = name;
            }

            if (map.Count == 0) return;

            lock (_langLock) _languageNames = map;
        }

        public static string LanguageName(string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return "";
            code = code.Trim();

            if (code.IndexOf(',') >= 0)
            {
                List<string> names = new List<string>();
                foreach (string part in code.Split(','))
                {
                    string n = LanguageName(part);
                    if (!string.IsNullOrEmpty(n)) names.Add(n);
                }
                return string.Join(", ", names);
            }

            lock (_langLock)
            {
                if (_languageNames.TryGetValue(code, out string name)) return name;
            }

            return code;
        }

        // "Arabic (300m)" -> "Arabic";  "Abkhaz: Georgia-Abkhazia (0.1m) [abk]" -> "Abkhaz"
        private static string shortLanguageName(string name)
        {
            name = name.Trim();

            int bracket = name.LastIndexOf('[');
            if (bracket >= 0 && name.EndsWith("]")) name = name.Substring(0, bracket).Trim();

            int colon = name.IndexOf(':');
            if (colon >= 0) name = name.Substring(0, colon);

            int paren = name.IndexOf(" (", StringComparison.Ordinal);
            if (paren >= 0) name = name.Substring(0, paren);

            return name.Trim();
        }

        //----------------------------------------------------------------------------------------
        // Mode heuristics for a broadcast/utility entry. The EiBi list is broadcast-first, so the
        // default is AM. A few signals are flagged in the data itself:
        //   Days (col 2) may carry "USB"/"LSB" -> explicit sideband.
        //   Language (col 5) special codes: -CW morse, -TY/-HF digital teletype/HFDL.
        //   Persistence (col 8) >= 90 means a utility station, which conventionally uses USB.
        // Mode numbers: 1=CW, 2=DIGI, 14=AM, 15=USB, 16=LSB (see SpotManager2.SpotModeNumberToDSPMode).
        //----------------------------------------------------------------------------------------
        public static void Mode(DxClusterSwlEntry e, out string label, out int number)
        {
            if (e != null)
            {
                string days = e.Days ?? "";
                if (days.IndexOf("USB", StringComparison.OrdinalIgnoreCase) >= 0) { label = "USB"; number = 15; return; }
                if (days.IndexOf("LSB", StringComparison.OrdinalIgnoreCase) >= 0) { label = "LSB"; number = 16; return; }

                string lang = (e.LanguageCode ?? "").Trim();
                if (lang.Equals("-CW", StringComparison.OrdinalIgnoreCase)) { label = "CW"; number = 1; return; }
                if (lang.Equals("-TY", StringComparison.OrdinalIgnoreCase) ||
                    lang.Equals("-HF", StringComparison.OrdinalIgnoreCase)) { label = "DIGI"; number = 2; return; }

                if (e.Persistence >= 90) { label = "USB"; number = 15; return; } // utility station default
            }

            label = "AM";
            number = 14;
        }
    }
}
