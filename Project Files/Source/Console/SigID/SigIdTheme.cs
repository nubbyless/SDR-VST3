/*  SigIdTheme.cs

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
// SigID - the light and dark palettes of the Digi Id window.                                      //
//                                                                                              //
// The pattern follows the DX Cluster window (DxClusterTheme): one palette object per theme, a  //
// SetDark() switch and a ThemeChanged event that the window listens to.                          //
//============================================================================================//

using System;
using System.Drawing;

namespace Thetis
{
    internal static class SigIdTheme
    {
        internal sealed class Palette
        {
            public Color Void;
            public Color Face;
            public Color Inset;
            public Color Border;
            public Color TextPrimary;
            public Color TextSecondary;
            public Color Accent;
            public Color Warning;
            public Color Error;
            public Color Ok;
            public Color Button;
            public Color Gutter;
            public Color Selection;
        }

        private static readonly Palette Dark = new Palette
        {
            Void = Color.FromArgb(0x1E, 0x1E, 0x1E),
            Face = Color.FromArgb(0x4A, 0x4A, 0x4A),
            Inset = Color.FromArgb(0x33, 0x33, 0x33),
            Border = Color.FromArgb(0x1A, 0x1A, 0x1A),
            TextPrimary = Color.FromArgb(0xD8, 0xD8, 0xD8),
            TextSecondary = Color.FromArgb(0x9A, 0x9A, 0x9A),
            Accent = Color.FromArgb(0x7E, 0xC8, 0xF0),
            Warning = Color.FromArgb(0xE0, 0xA0, 0x30),
            Error = Color.FromArgb(0xE0, 0x60, 0x50),
            Ok = Color.FromArgb(0x70, 0xC0, 0x70),
            Button = Color.FromArgb(0x45, 0x45, 0x45),
            Gutter = Color.FromArgb(0x33, 0x33, 0x33),
            Selection = Color.FromArgb(0x3D, 0x5A, 0x78)
        };

        private static readonly Palette Light = new Palette
        {
            Void = Color.FromArgb(0xF0, 0xF0, 0xF0),
            Face = Color.FromArgb(0xE8, 0xE8, 0xE8),
            Inset = Color.FromArgb(0xFF, 0xFF, 0xFF),
            Border = Color.FromArgb(0xC8, 0xC8, 0xC8),
            TextPrimary = Color.FromArgb(0x20, 0x20, 0x20),
            TextSecondary = Color.FromArgb(0x60, 0x60, 0x60),
            Accent = Color.FromArgb(0x1E, 0x6F, 0xA8),
            Warning = Color.FromArgb(0xA0, 0x64, 0x00),
            Error = Color.FromArgb(0xB0, 0x28, 0x20),
            Ok = Color.FromArgb(0x1E, 0x7A, 0x2E),
            Button = Color.FromArgb(0xDC, 0xDC, 0xDC),
            Gutter = Color.FromArgb(0xFF, 0xFF, 0xFF),
            Selection = Color.FromArgb(0xCC, 0xE4, 0xFF)
        };

        private static bool _dark = true;

        public static bool IsDark
        {
            get { return _dark; }
        }

        public static event Action ThemeChanged;

        public static void SetDark(bool dark)
        {
            if (_dark == dark) return;
            _dark = dark;
            ThemeChanged?.Invoke();
        }

        private static Palette P { get { return _dark ? Dark : Light; } }

        public static Color Void { get { return P.Void; } }
        public static Color Face { get { return P.Face; } }
        public static Color Inset { get { return P.Inset; } }
        public static Color Border { get { return P.Border; } }
        public static Color TextPrimary { get { return P.TextPrimary; } }
        public static Color TextSecondary { get { return P.TextSecondary; } }
        public static Color Accent { get { return P.Accent; } }
        public static Color Warning { get { return P.Warning; } }
        public static Color Error { get { return P.Error; } }
        public static Color Ok { get { return P.Ok; } }
        public static Color Button { get { return P.Button; } }
        public static Color Gutter { get { return P.Gutter; } }
        public static Color Selection { get { return P.Selection; } }
    }
}
