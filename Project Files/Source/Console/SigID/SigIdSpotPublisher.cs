/*  SigIdSpotPublisher.cs

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
// SigID - publishing the identification as a panadapter spot.                                      //
//                                                                                              //
// The DX cluster already draws labelled tags on the panadapter, and that renderer is the one   //
// that gets the fonts, the box, the stalk and the label stacking right.  So the identification //
// is published as a spot with the source "sigid:" and the existing spot renderer draws it -     //
// no second, parallel tag renderer to keep in step.                                            //
//============================================================================================//

using System;
using System.Drawing;

namespace Thetis
{
    public static class SigIdSpotPublisher
    {
        /// <summary>The source prefix that marks the identification spot.</summary>
        public const string SOURCE = "sigid:";

        private static string _lastText = string.Empty;
        private static long _lastFrequency;

        /// <summary>
        /// Publishes the identification.  The spot is only replaced when the name or the
        /// frequency changes, so the spot list does not churn every analysis cycle.
        /// </summary>
        /// <param name="text">The name shown on the tag.</param>
        /// <param name="frequencyHz">The frequency the tag is placed at.</param>
        /// <param name="colour">The tag colour.</param>
        /// <param name="details">Extra text shown in the hover bubble.</param>
        public static void Publish(string text, long frequencyHz, Color colour, string details)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                Clear();
                return;
            }

            if (text == _lastText && frequencyHz == _lastFrequency &&
                SpotManager2.HasSpotsFromPrefix(SOURCE))
            {
                return;
            }

            SpotManager2.ClearSpotsBySource(SOURCE);

            // A JSON payload has to be supplied: without one the spot manager treats the
            // text as a callsign and looks up a country prefix for it, which put the flag
            // of a random country next to the tag.
            SpotManager2.JsonSpotData data = new SpotManager2.JsonSpotData();
            data.Spotter = string.Empty;
            data.Comment = details ?? string.Empty;
            data.Continent = string.Empty;
            data.Country = string.Empty;
            data.Flag = string.Empty;
            data.FlagSpotter = string.Empty;
            data.UtcTime = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
            data.Heading = -1;
            data.Distance = -1;

            SpotManager2.AddSpot(text, DSPMode.FIRST, frequencyHz, colour, string.Empty, data, SOURCE);

            // The spot manager guesses a country from the callsign whenever it has no flag,
            // using a callsign prefix lookup.  The text of an identification is a signal
            // name, not a callsign, so "People's Republic of China" used to come out with
            // the flag of the People's Republic of China.  Clear the guess again.
            SpotManager2.smSpot[] spots = SpotManager2.GetFrequencySortedSpots();
            foreach (SpotManager2.smSpot spot in spots)
            {
                if (spot == null || spot.source == null) continue;
                if (!spot.source.StartsWith(SOURCE, StringComparison.OrdinalIgnoreCase)) continue;
                spot.flag = null;
                spot.flag_spotter = null;
                spot.country = string.Empty;
                spot.continent = string.Empty;
                spot.spot_flag_in_use = false;
                spot.spotter_flag_in_use = false;
            }

            _lastText = text;
            _lastFrequency = frequencyHz;
        }

        /// <summary>Removes the identification spot.</summary>
        public static void Clear()
        {
            SpotManager2.ClearSpotsBySource(SOURCE);
            _lastText = string.Empty;
            _lastFrequency = 0;
        }
    }
}
