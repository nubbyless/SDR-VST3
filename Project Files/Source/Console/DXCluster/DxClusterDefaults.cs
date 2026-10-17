/*  DxClusterDefaults.cs

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
// DX Cluster - built-in list of well known public telnet nodes offered as defaults in the      //
// "Servers..." picker. Users can tick the ones they want (each connects immediately and is     //
// remembered) or add their own. Nothing here connects automatically: auto-connect is only      //
// ever driven by the per-node "Auto" flag the user sets explicitly.                            //
//============================================================================================//

using System.Collections.Generic;

namespace Thetis
{
    public sealed class DxClusterServerEntry
    {
        public string Name { get; set; }
        public string Host { get; set; }
        public int Port { get; set; }

        public DxClusterServerEntry(string name, string host, int port)
        {
            Name = name ?? "";
            Host = host ?? "";
            Port = port;
        }

        public string Display
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Name)) return Host + ":" + Port;
                return Name + "  (" + Host + ":" + Port + ")";
            }
        }

        public override string ToString()
        {
            return Display;
        }
    }

    public static class DxClusterDefaults
    {
        //====================================================================================//
        // USER SERVER LIST - add entries as:                                                    //
        //     new DxClusterServerEntry("Name", "host.example.org", 7300),                        //
        //====================================================================================//
        public static readonly DxClusterServerEntry[] Servers = new DxClusterServerEntry[]
        {
            new DxClusterServerEntry("", "ve7cc.net", 23),
            new DxClusterServerEntry("", "dxspider.co.uk", 7300),
            new DxClusterServerEntry("", "db0erf.de", 41113),
            new DxClusterServerEntry("", "gb7baa.com", 7300),
            new DxClusterServerEntry("", "gb7djk.dxcluster.net", 7300),
            new DxClusterServerEntry("", "it9obk.org", 41112),
            new DxClusterServerEntry("", "dx.harwood.com", 8000),
            new DxClusterServerEntry("", "dxusa.net", 7300),
            new DxClusterServerEntry("", "dxc.middlebrook.ca", 8000),
            new DxClusterServerEntry("", "dxc.jg1vgx.net", 7300),
        };
        //====================================================================================//
    }
}
