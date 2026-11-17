/*  SigIdPaths.cs

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
// SigID - storage location for the Artemis signal-identification database.                    //
//                                                                                              //
// The database is NOT shipped with the application (it is ~288 MB compressed).  The user       //
// installs it at runtime from the "Digi Id" form.                                               //
//                                                                                              //
// Resolution order (SIGID_HANDOFF.md section 4.1):                                             //
//   1. %SDRVST3_SIGID_PATH%   - explicit developer override, wins over everything.              //
//   2. <exeDir>\SigId         - used ONLY when that folder already exists.  A Program Files     //
//                               install can never satisfy this, so end users always fall      //
//                               through to (3).  Developers can drop an unpacked database next  //
//                               to Thetis.exe for local testing.                                //
//   3. %AppData%\...\SigId    - shipping default.                                              //
//============================================================================================//

using System;
using System.IO;

namespace Thetis
{
    public static class SigIdPaths
    {
        /// <summary>File name of the Artemis SQLite database inside the SigId folder.</summary>
        public const string DATABASE_FILE = "data.sqlite";

        /// <summary>Sub-folder holding the waterfall PNGs and reference audio clips.</summary>
        public const string MEDIA_FOLDER = "media";

        /// <summary>File holding our own install metadata (version, timestamps, last check).</summary>
        public const string INSTALL_INFO_FILE = "install.json";

        /// <summary>Folder name used both for the AppData install and the local test copy.</summary>
        public const string SIGID_FOLDER = "SigId";

        /// <summary>Environment variable that overrides the database location (developer use).</summary>
        public const string PATH_ENV_VAR = "SDRVST3_SIGID_PATH";

        // mirrors console.cs AppDataPath for the x64 build
        private const string APP_DATA_SUBPATH = @"OpenHPSDR\SDR-VST3-x64";

        private static string _cachedRoot;
        private static bool _cachedIsLocal;

        /// <summary>
        /// Root folder of the installed database.  Resolved once and then cached; the only
        /// thing that can change it is the developer override, which is read on first use.
        /// </summary>
        public static string Root
        {
            get
            {
                if (_cachedRoot != null) return _cachedRoot;

                string env = SafeEnv(PATH_ENV_VAR);
                if (!string.IsNullOrWhiteSpace(env))
                {
                    _cachedRoot = env.Trim();
                    _cachedIsLocal = true;
                    return _cachedRoot;
                }

                string local = Path.Combine(AppContext.BaseDirectory, SIGID_FOLDER);
                if (Directory.Exists(local))
                {
                    _cachedRoot = local;
                    _cachedIsLocal = true;
                    return _cachedRoot;
                }

                _cachedRoot = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    APP_DATA_SUBPATH,
                    SIGID_FOLDER);
                _cachedIsLocal = false;
                return _cachedRoot;
            }
        }

        /// <summary>
        /// True when the database is being used from a local (developer / test) folder rather
        /// than from the shipping AppData location.
        /// </summary>
        public static bool IsLocalCopy
        {
            get { string ignored = Root; return _cachedIsLocal; }
        }

        /// <summary>Full path of the Artemis SQLite database file.</summary>
        public static string DatabaseFile
        {
            get { return Path.Combine(Root, DATABASE_FILE); }
        }

        /// <summary>Full path of the folder holding the waterfall images and audio clips.</summary>
        public static string MediaDirectory
        {
            get { return Path.Combine(Root, MEDIA_FOLDER); }
        }

        /// <summary>Full path of our install metadata file.</summary>
        public static string InstallInfoFile
        {
            get { return Path.Combine(Root, INSTALL_INFO_FILE); }
        }

        /// <summary>True when a database has been installed (the SQLite file is present).</summary>
        public static bool IsInstalled
        {
            get { try { return File.Exists(DatabaseFile); } catch { return false; } }
        }

        /// <summary>Creates the root folder if it does not exist yet.</summary>
        public static bool EnsureRoot()
        {
            try
            {
                if (!Directory.Exists(Root)) Directory.CreateDirectory(Root);
                return true;
            }
            catch { return false; }
        }

        /// <summary>
        /// Resolves the absolute path of a media asset (file name is the database DOC_ID plus
        /// its extension, e.g. "410.png").  Returns an empty string when it cannot be found.
        /// </summary>
        public static string ResolveMediaFile(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return string.Empty;
            try
            {
                string path = Path.Combine(MediaDirectory, fileName);
                return File.Exists(path) ? path : string.Empty;
            }
            catch { return string.Empty; }
        }

        /// <summary>Forgets the cached root so the next access re-resolves it.</summary>
        public static void ResetCache()
        {
            _cachedRoot = null;
            _cachedIsLocal = false;
        }

        private static string SafeEnv(string name)
        {
            try { return Environment.GetEnvironmentVariable(name); }
            catch { return null; }
        }
    }
}
