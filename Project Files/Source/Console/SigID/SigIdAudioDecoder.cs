/*  SigIdAudioDecoder.cs

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
// SigID - decoding of the Artemis reference recordings.                                          //
//                                                                                              //
// The reference clips in the database are Ogg Vorbis (and some Ogg FLAC).  The application does  //
// not carry an Ogg decoder of its own, but the fldigi sidecar payload already ships            //
// libsndfile, which reads both.  The library is loaded from the Fldigi folder with              //
// LoadLibraryEx(..., LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR) so that its own dependencies are        //
// resolved next to it.  Pre-loading it also makes the later P/Invoke resolve to the same       //
// module, because the loader hands back an already loaded library with a matching name.        //
//                                                                                              //
// Nothing is installed twice: the MSI already deploys payload\Fldigi to <install>\Fldigi.       //
//============================================================================================//

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace Thetis
{
    public static class SigIdAudioDecoder
    {
        private const int SFM_READ = 0x10;

        private const uint LOAD_LIBRARY_SEARCH_DEFAULT_DIRS = 0x00001000;
        private const uint LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR = 0x00000100;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);

        [DllImport("libsndfile-1.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sf_open(string path, int mode, IntPtr info);

        [DllImport("libsndfile-1.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern long sf_readf_float(IntPtr handle, float[] buffer, long frames);

        [DllImport("libsndfile-1.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sf_close(IntPtr handle);

        [DllImport("libsndfile-1.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sf_strerror(IntPtr handle);

        [DllImport("libsndfile-1.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sf_version_string();

        private static bool _preloaded;
        private static bool _available;
        private static string _lastError = string.Empty;

        /// <summary>True when libsndfile could be loaded.</summary>
        public static bool Available
        {
            get { Preload(); return _available; }
        }

        /// <summary>Why the decoder is not available, empty when it is.</summary>
        public static string LastError
        {
            get { return _lastError; }
        }

        /// <summary>
        /// Loads libsndfile from the folders it can live in.  Safe to call repeatedly.
        /// </summary>
        public static void Preload()
        {
            if (_preloaded) return;
            _preloaded = true;

            foreach (string folder in CandidateFolders())
            {
                try
                {
                    string library = Path.Combine(folder, "libsndfile-1.dll");
                    if (!File.Exists(library)) continue;

                    IntPtr handle = LoadLibraryEx(library, IntPtr.Zero,
                        LOAD_LIBRARY_SEARCH_DEFAULT_DIRS | LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR);
                    if (handle == IntPtr.Zero) continue;

                    _available = true;
                    _lastError = string.Empty;
                    return;
                }
                catch (Exception ex)
                {
                    _lastError = ex.Message;
                }
            }

            _available = false;
            if (string.IsNullOrEmpty(_lastError))
                _lastError = "libsndfile-1.dll was not found in " + string.Join(", ", CandidateFolders().ToArray());
        }

        private static List<string> CandidateFolders()
        {
            List<string> folders = new List<string>();
            try { folders.Add(Path.Combine(AppContext.BaseDirectory, "Fldigi")); } catch { }
            try
            {
                // the development layout: <repo>/Project Files/bin/x64/Release -> <repo>/payload/Fldigi
                DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
                for (int i = 0; i < 10 && dir != null; i++)
                {
                    folders.Add(Path.Combine(dir.FullName, "payload", "Fldigi"));
                    dir = dir.Parent;
                }
            }
            catch { }
            try { folders.Add(AppContext.BaseDirectory); } catch { }
            return folders;
        }

        /// <summary>The libsndfile version string, or an empty string.</summary>
        public static string Version
        {
            get
            {
                if (!Available) return string.Empty;
                try { return Marshal.PtrToStringAnsi(sf_version_string()) ?? string.Empty; }
                catch { return string.Empty; }
            }
        }

        /// <summary>
        /// Decodes an audio file to mono float samples at its native rate.
        /// Returns false when the file cannot be read.
        /// </summary>
        public static bool TryDecode(string path, out float[] samples, out int rate)
        {
            samples = null;
            rate = 0;

            if (!Available) return false;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;

            IntPtr info = IntPtr.Zero;
            IntPtr handle = IntPtr.Zero;
            try
            {
                info = Marshal.AllocHGlobal(64);
                for (int i = 0; i < 64; i++) Marshal.WriteByte(info, i, 0);

                handle = sf_open(path, SFM_READ, info);
                if (handle == IntPtr.Zero) return false;

                long frames = Marshal.ReadInt64(info, 0);
                rate = Marshal.ReadInt32(info, 8);
                int channels = Marshal.ReadInt32(info, 12);
                if (rate <= 0 || channels <= 0 || frames <= 0) return false;

                float[] buffer = new float[8192 * channels];
                float[] mono = new float[frames];
                long written = 0;

                while (written < frames)
                {
                    long want = Math.Min(buffer.Length / channels, frames - written);
                    long got = sf_readf_float(handle, buffer, want);
                    if (got <= 0) break;

                    for (long i = 0; i < got; i++)
                        mono[written + i] = buffer[i * channels];      // take the left channel

                    written += got;
                }

                if (written <= 0) return false;
                if (written < frames)
                {
                    float[] trimmed = new float[written];
                    Array.Copy(mono, trimmed, written);
                    mono = trimmed;
                }

                samples = mono;
                return true;
            }
            catch (Exception ex)
            {
                _lastError = ex.Message;
                return false;
            }
            finally
            {
                if (handle != IntPtr.Zero) { try { sf_close(handle); } catch { } }
                if (info != IntPtr.Zero) Marshal.FreeHGlobal(info);
            }
        }

        /// <summary>
        /// Resamples to the given rate with a Hamming windowed sinc kernel.  The features
        /// are all in physical units (Hz, ms, baud), but the analysis has to run at the
        /// same rate for the reference clips and for the live signal, otherwise the
        /// instantaneous frequency histogram is not comparable.
        /// </summary>
        public static float[] Resample(float[] source, int sourceRate, int targetRate)
        {
            if (source == null || source.Length == 0) return new float[0];
            if (sourceRate == targetRate || sourceRate <= 0) return source;

            double ratio = (double)targetRate / sourceRate;
            int outputCount = (int)(source.Length * ratio);
            float[] output = new float[outputCount];

            const int half = 16;                                  // kernel half width in source samples
            double cutoff = Math.Min(1.0, ratio) * 0.90;          // anti alias

            for (int i = 0; i < outputCount; i++)
            {
                double centre = i / ratio;
                int first = (int)Math.Floor(centre) - half + 1;
                double sum = 0.0;
                double weight = 0.0;

                for (int k = 0; k < 2 * half; k++)
                {
                    int index = first + k;
                    if (index < 0 || index >= source.Length) continue;

                    double x = index + 0.5 - centre;
                    double sinc = Math.Abs(x) < 1e-9
                        ? cutoff
                        : Math.Sin(Math.PI * x * cutoff) / (Math.PI * x * cutoff);
                    double window = 0.54 - 0.46 * Math.Cos(2.0 * Math.PI * (k + 0.5) / (2 * half));
                    double w = sinc * window;

                    sum += source[index] * w;
                    weight += w;
                }

                output[i] = weight != 0.0 ? (float)(sum / weight) : 0.0f;
            }

            return output;
        }
    }
}
