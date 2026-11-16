/*  SigIdScope.cs

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
// SigID - which signals the identification should look at.                                         //
//                                                                                              //
// The database holds 583 signals and most of them are not what a ham is looking for -         //
// military, radar, numbers stations, aviation and so on.  Restricting the candidate set to   //
// the category the operator cares about makes the comparison decisive instead of a needle in a   //
// haystack, and the tag then says something actionable.                                          //
//                                                                                              //
// The scopes follow the same idea as the separate DX cluster and SWL switches: the operator      //
// picks what is relevant.                                                                       //
//============================================================================================//

using System;
using System.Collections.Generic;

namespace Thetis
{
    public enum SigIdScope
    {
        /// <summary>Only the amateur radio signals - the default.</summary>
        AmateurRadio,

        /// <summary>Digital signals that are used in the HF bands.</summary>
        HfDigitalModes,

        /// <summary>Everything in the database.</summary>
        AllSignals
    }

    public static class SigIdScopes
    {
        /// <summary>The lower edge of the HF band in Hz.</summary>
        public const long HF_LOW_HZ = 3000000;

        /// <summary>The upper edge of the HF band in Hz.</summary>
        public const long HF_HIGH_HZ = 30000000;

        public static string Describe(SigIdScope scope)
        {
            switch (scope)
            {
                case SigIdScope.AmateurRadio: return "Amateur radio";
                case SigIdScope.HfDigitalModes: return "HF digital modes";
                default: return "All signals";
            }
        }

        /// <summary>
        /// True when the signal belongs to the scope.  A signal with no category at all is
        /// only included in "All signals", otherwise it would be invisible in the narrow
        /// scopes and the operator would never see it.
        /// </summary>
        public static bool Includes(SigIdScope scope, SigIdSignal signal)
        {
            if (signal == null) return false;

            switch (scope)
            {
                case SigIdScope.AmateurRadio:
                    return signal.Categories.Contains("Amateur Radio");

                case SigIdScope.HfDigitalModes:
                    if (!signal.Categories.Contains("Digital")) return false;
                    return HasHfFrequency(signal);

                default:
                    return true;
            }
        }

        /// <summary>True when the signal is documented as being used in the HF bands.</summary>
        public static bool HasHfFrequency(SigIdSignal signal)
        {
            foreach (long frequency in signal.Frequencies)
            {
                if (frequency >= HF_LOW_HZ && frequency <= HF_HIGH_HZ) return true;
            }
            return false;
        }

        /// <summary>How many signals the scope covers.</summary>
        public static int Count(SigIdScope scope, SigIdDatabase database)
        {
            if (database == null) return 0;
            int count = 0;
            foreach (SigIdSignal signal in database.Signals)
            {
                if (Includes(scope, signal)) count++;
            }
            return count;
        }
    }
}
