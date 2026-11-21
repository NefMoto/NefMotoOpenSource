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
        public void Bootmode_verify_takes_an_image()
        {
            Program.Request request = Parse("bootmode", "verify", "-i", "image.bin", "-f");
            Assert.True(request.Verify);
            Assert.False(request.Read);
            Assert.False(request.Kwp);
            Assert.Equal("image.bin", request.InputPath);
            Assert.True(request.Force);
        }

        [Theory]
        [InlineData("bootmode verify", "verify needs -i image.bin.")]
        [InlineData("bootmode verify -i image.bin -o out.bin", "verify does not take -o.")]
        [InlineData("bootmode verify -i image.bin --layout 29F800BB", "Boot mode reads the chip id. Do not pass --layout.")]
        [InlineData("bootmode verify -i image.bin --no-verify", "verify does not take --no-verify.")]
        [InlineData("kwp verify -i image.bin", "Full-chip verify is a bootmode command.")]
        [InlineData("bootmode verify full -i image.bin", "verify does not take full or diff. Pass bootmode verify -i FILE.")]
        public void Bootmode_verify_rejects_a_bad_command(string command, string expected)
        {
            Assert.False(Program.TryParse(command.Split(' '), out _, out string? error));
            Assert.Equal(expected, error);
        }

        [Fact]
        public void Kwp_dtc_read_is_a_standard_session_command()
        {
            Program.Request request = Parse("kwp", "dtc", "read", "--slow", "--baud", "10400", "--usb", "0");
            Assert.True(request.Kwp);
            Assert.True(request.DtcRead);
            Assert.False(request.DtcClear);
            Assert.True(request.Slow);
            Assert.Equal(10400u, request.Baud);
            Assert.Equal(0, request.UsbIndex);
            Assert.Null(request.OutputPath);
        }

        [Theory]
        [InlineData("kwp", true)]
        [InlineData("bootmode", false)]
        public void Info_keeps_the_output_file(string protocol, bool kwp)
        {
            Program.Request request = Parse(protocol, "info", "-o", "info.txt");
            Assert.True(request.Info);
            Assert.Equal(kwp, request.Kwp);
            Assert.Equal("info.txt", request.OutputPath);
        }

        [Fact]
        public void Kwp_dtc_read_keeps_the_output_file()
        {
            Program.Request request = Parse("kwp", "dtc", "read", "-o", "dtcs.txt");
            Assert.True(request.DtcRead);
            Assert.Equal("dtcs.txt", request.OutputPath);
        }

        [Fact]
        public void Kwp_dtc_clear_is_a_kwp_command()
        {
            Program.Request request = Parse("KWP", "DTC", "CLEAR");
            Assert.True(request.DtcClear);
            Assert.False(request.DtcRead);
            Assert.True(request.Kwp);
        }

        [Fact]
        public void Bootmode_immo_off_keeps_the_backup_and_the_95p08_preset()
        {
            Program.Request request = Parse("bootmode", "immo", "off", "--95p08", "-o", "backup.bin", "--no-verify");
            Assert.True(request.ImmoOff);
            Assert.False(request.Kwp);
            Assert.True(request.Eeprom95P08);
            Assert.Equal("backup.bin", request.OutputPath);
            Assert.True(request.NoVerify);
            Assert.False(request.Force);
        }

        [Theory]
        [InlineData("bootmode dtc read", "DTC read and clear are KWP commands.")]
        [InlineData("kwp dtc write", "DTC command must be read or clear.")]
        [InlineData("kwp dtc read -i image.bin", "dtc does not take -i, --layout, --no-verify, or --95p08.")]
        [InlineData("kwp dtc clear -o dtcs.txt", "dtc clear does not take -o.")]
        [InlineData("kwp immo off", "Immo off is a bootmode command.")]
        [InlineData("bootmode immo on", "Immo command must be off.")]
        [InlineData("bootmode immo off -i image.bin", "immo off does not take -i.")]
        [InlineData("bootmode immo off --layout 29F800BB", "immo off does not take --layout.")]
        [InlineData("bootmode immo off --slow", "--slow applies to KWP.")]
        [InlineData("kwp info --95p08", "--95p08 applies to bootmode immo off.")]
        [InlineData("kwp read dtc", "Pass kwp dtc read, kwp dtc clear, or bootmode immo off.")]
        [InlineData("bootmode immo off --95p08 --95p08", "--95p08 was given twice.")]
        public void Dtc_and_immo_reject_a_bad_command(string command, string expected)
        {
            Assert.False(Program.TryParse(command.Split(' '), out _, out string? error));
            Assert.Equal(expected, error);
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
