/*
Nefarious Motorsports ME7 ECU Flasher
Copyright (C) 2026  Nefarious Motorsports Inc

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

This program is distributed in the hope that it will be useful,
but WITHOUT ANY WARRANTY; without even the implied warranty of
MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
GNU General Public License for more details.

You should have received a copy of the GNU General Public License
along with this program.  If not, see <http://www.gnu.org/licenses/>.

Contact by Email: nyet@nyet.org
*/

using Ecuflash;
using Xunit;

namespace NefMotoOpenSource.Tests
{
    public class EcuflashParseTests
    {
        static Program.Request Parse(params string[] args)
        {
            Assert.True(Program.TryParse(args, out Program.Request? request, out string? error), error);
            Assert.NotNull(request);
            return request!;
        }

        [Theory]
        [InlineData("1m", "29F800BB")]
        [InlineData("1MB", "29F800BB")]
        [InlineData("512k", "29F400BB")]
        [InlineData("512KB", "29F400BB")]
        [InlineData("256K", "29F200")]
        [InlineData("256kb", "29F200")]
        [InlineData("29F800BT", "29F800BT")]
        [InlineData("29F800BB.MemoryLayout.xml", "29F800BB")]
        public void Size_words_select_the_bottom_boot_layouts(string name, string expected)
        {
            Assert.Equal(expected, Program.ResolveLayoutName(name));
        }

        [Theory]
        [InlineData("--layout")]
        [InlineData("kwp", "read", "full", "--layout", "-o", "out.bin")]
        public void Layout_with_no_name_lists_layouts(params string[] args)
        {
            Program.Request request = Parse(args);
            Assert.True(request.LayoutList);
            Assert.Null(request.LayoutName);
        }

        [Theory]
        [InlineData("-f")]
        [InlineData("--force")]
        public void Force_skips_prompts(string flag)
        {
            Program.Request request = Parse("kwp", "read", "full", "-o", "out.bin", flag);
            Assert.True(request.Force);
            Assert.Null(request.LayoutName);
        }

        [Fact]
        public void Force_given_twice_fails()
        {
            Assert.False(Program.TryParse(new[] { "kwp", "read", "full", "-o", "out.bin", "-f", "--force" }, out _, out string? error));
            Assert.Equal("--force was given twice.", error);
        }

        [Fact]
        public void Full_kwp_read_keeps_the_layout_word()
        {
            Program.Request request = Parse("kwp", "write", "full", "-i", "image.bin", "--layout", "512k");
            Assert.Equal("512k", request.LayoutName);
            Assert.Equal("29F400BB", Program.ResolveLayoutName(request.LayoutName!));
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
