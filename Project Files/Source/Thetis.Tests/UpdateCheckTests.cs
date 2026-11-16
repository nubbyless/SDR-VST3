/*  UpdateCheckTests.cs

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

using System;
using Xunit;

namespace Thetis.Tests
{
    /// <summary>
    /// The update check was reported as unreliable ("there was a new update but my
    /// console never told me").  The version comparison turned out to be correct; the
    /// weak point was delivery.  These tests pin down the version handling for the
    /// 2-part -&gt; 3-part tag transition (v6.3 -&gt; v6.3.1 -&gt; v6.3.2) so it cannot
    /// regress, and cover the new redirect based lookup.
    /// </summary>
    public class UpdateCheckTests
    {
        [Theory]
        [InlineData("v6.3", 6, 3, 0, 0)]
        [InlineData("v6.3.1", 6, 3, 1, 0)]
        [InlineData("v6.3.2", 6, 3, 2, 0)]
        [InlineData("v6.4", 6, 4, 0, 0)]
        [InlineData("6.3.1.0", 6, 3, 1, 0)]
        [InlineData("v6.3.1.0", 6, 3, 1, 0)]
        [InlineData("v6.3.2-beta", 6, 3, 2, 0)]
        [InlineData("v5.9", 5, 9, 0, 0)]
        public void Tag_names_parse_to_four_part_versions(string tag, int a, int b, int c, int d)
        {
            Version version = Console.ParseLooseVersion(tag);

            Assert.NotNull(version);
            Assert.Equal(a, version.Major);
            Assert.Equal(b, version.Minor);
            Assert.Equal(c, version.Build);
            Assert.Equal(d, version.Revision);
        }

        [Fact]
        public void Unparseable_versions_return_null()
        {
            Assert.Null(Console.ParseLooseVersion(null));
            Assert.Null(Console.ParseLooseVersion(""));
            Assert.Null(Console.ParseLooseVersion("vNext"));
            Assert.Null(Console.ParseLooseVersion("no digits here"));
        }

        /// <summary>
        /// The CI writes a four part file version (6.3.0.0 / 6.3.1.0 / 6.3.2.0) while the
        /// release tag keeps its natural length.  Both sides are padded to four parts, so
        /// every step of the 2-part -&gt; 3-part transition must compare correctly.
        /// </summary>
        [Theory]
        // running 6.3, the new release is 6.3.1
        [InlineData("6.3.0.0", "v6.3.1", true)]
        [InlineData("6.3.0.0", "v6.3", false)]
        // running 6.3.1, the new release is 6.3.2
        [InlineData("6.3.1.0", "v6.3.2", true)]
        [InlineData("6.3.1.0", "v6.3.1", false)]
        [InlineData("6.3.1.0", "v6.3", false)]
        // running 6.3.2, the new release is 6.4
        [InlineData("6.3.2.0", "v6.4", true)]
        [InlineData("6.3.2.0", "v6.3.2", false)]
        [InlineData("6.3.2.0", "v6.3.1", false)]
        // a development build from master is newer than the published release
        [InlineData("6.3.2.0", "v6.3.1", false)]
        [InlineData("6.4.0.0", "v6.3.2", false)]
        public void Update_is_flagged_only_for_a_newer_release(string current, string latestTag, bool expectUpdate)
        {
            Version running = Console.ParseLooseVersion(current);
            Version latest = Console.ParseLooseVersion(latestTag);

            Assert.NotNull(running);
            Assert.NotNull(latest);
            Assert.Equal(expectUpdate, latest > running);
        }

        [Theory]
        [InlineData("https://github.com/nubbyless/SDR-VST3-HL2/releases/tag/v6.3.1", "v6.3.1")]
        [InlineData("https://github.com/nubbyless/SDR-VST3-HL2/releases/tag/v6.3.2", "v6.3.2")]
        [InlineData("https://github.com/nubbyless/SDR-VST3-HL2/releases/tag/v6.3", "v6.3")]
        public void Release_tag_is_taken_from_the_redirect_location(string url, string expected)
        {
            Assert.Equal(expected, Console.ExtractReleaseTagFromUrl(url));
        }

        [Fact]
        public void Garbage_urls_yield_no_tag()
        {
            Assert.Equal(string.Empty, Console.ExtractReleaseTagFromUrl(null));
            Assert.Equal(string.Empty, Console.ExtractReleaseTagFromUrl("   "));
            Assert.Equal(string.Empty, Console.ExtractReleaseTagFromUrl("https://github.com/owner/repo/releases/"));
        }
    }
}
