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

using Communication;
using Xunit;

namespace NefMotoOpenSource.Tests
{
    public class Me7Eeprom95040ChecksumTests
    {
        private const ushort ChecksumPresentMask = 0x0001;

        [Fact]
        public void Validate_rejects_all_ff_image()
        {
            var data = new byte[Me7Eeprom95040Checksum.EepromSize];
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = 0xFF;
            }

            var result = Me7Eeprom95040Checksum.Validate(data);

            Assert.True(result.PagesChecked > 0);
            Assert.NotEqual(0, result.PagesInvalid);
            Assert.False(result.AllChecksumPagesValid);
        }

        [Fact]
        public void CalculatePageChecksum_matches_reference_page_1()
        {
            // Forum sample block 01: first 14 bytes from ME7 EEPROM checksum guide output
            var page = new byte[]
            {
                0x05, 0x01, 0x01, 0x00, 0xCB, 0x28, 0x00, 0x00,
                0x00, 0x00, 0x69, 0xC1, 0x00, 0xA5, 0x36, 0xFD,
            };

            ushort expected = Me7Eeprom95040Checksum.CalculatePageChecksum(page, 0, 1, 0x0017);

            Assert.Equal(0xFD36, expected);
        }

        [Fact]
        public void Validate_counts_matching_pages_28_and_29_as_ok()
        {
            var data = BuildImageWithValidChecksums();

            var result = Me7Eeprom95040Checksum.Validate(data);

            Assert.Equal(0, result.PagesInvalid);
            Assert.True(result.AllChecksumPagesValid);
            Assert.Equal(Me7Eeprom95040Checksum.PageChecksumKind.Ok, result.PageChecksumKinds[28]);
            Assert.Equal(Me7Eeprom95040Checksum.PageChecksumKind.Ok, result.PageChecksumKinds[29]);
            Assert.DoesNotContain("failed", result.FormatStatusMessage());
        }

        [Fact]
        public void Validate_rejects_mismatched_pages_28_and_29()
        {
            var data = BuildImageWithValidChecksums();
            data[28 * 16 + 14] ^= 0xFF;
            data[28 * 16 + 15] ^= 0xFF;
            data[29 * 16 + 14] ^= 0xFF;
            data[29 * 16 + 15] ^= 0xFF;

            var result = Me7Eeprom95040Checksum.Validate(data);

            Assert.Equal(2, result.PagesInvalid);
            Assert.False(result.AllChecksumPagesValid);
            Assert.Contains("failed", result.FormatStatusMessage());
            Assert.Equal(Me7Eeprom95040Checksum.PageChecksumKind.Bad, result.PageChecksumKinds[28]);
            Assert.Equal(Me7Eeprom95040Checksum.PageChecksumKind.Bad, result.PageChecksumKinds[29]);
        }

        [Fact]
        public void Validate_still_fails_when_non_exempt_page_is_corrupt()
        {
            var data = BuildImageWithValidChecksums();
            data[1 * 16 + 14] ^= 0xFF;
            data[1 * 16 + 15] ^= 0xFF;

            var result = Me7Eeprom95040Checksum.Validate(data);

            Assert.Equal(1, result.PagesInvalid);
            Assert.False(result.AllChecksumPagesValid);
            Assert.Contains("failed", result.FormatStatusMessage());
            Assert.Equal(Me7Eeprom95040Checksum.PageChecksumKind.Bad, result.PageChecksumKinds[1]);
            Assert.Equal(Me7Eeprom95040Checksum.PageChecksumKind.Ok, result.PageChecksumKinds[2]);
            Assert.Equal(Me7Eeprom95040Checksum.PageChecksumKind.Ok, result.PageChecksumKinds[28]);
            Assert.Equal(Me7Eeprom95040Checksum.PageChecksumKind.None, result.PageChecksumKinds[0]);
        }

        [Fact]
        public void CorrectChecksums_fixes_non_exempt_pages_only()
        {
            var data = BuildImageWithValidChecksums();

            data[1 * 16 + 14] ^= 0xFF;
            data[1 * 16 + 15] ^= 0xFF;
            byte p28_14 = data[28 * 16 + 14] = 0x7F;
            byte p28_15 = data[28 * 16 + 15] = 0x09;
            byte p29_14 = data[29 * 16 + 14] = 0x39;
            byte p29_15 = data[29 * 16 + 15] = 0x03;

            Assert.False(Me7Eeprom95040Checksum.Validate(data).AllChecksumPagesValid);

            int updated = Me7Eeprom95040Checksum.CorrectChecksums(data);

            Assert.Equal(1, updated);
            Assert.False(Me7Eeprom95040Checksum.Validate(data).AllChecksumPagesValid);
            Assert.Equal(p28_14, data[28 * 16 + 14]);
            Assert.Equal(p28_15, data[28 * 16 + 15]);
            Assert.Equal(p29_14, data[29 * 16 + 14]);
            Assert.Equal(p29_15, data[29 * 16 + 15]);
            Assert.Equal(Me7Eeprom95040Checksum.PageChecksumKind.Ok, Me7Eeprom95040Checksum.Validate(data).PageChecksumKinds[1]);
            Assert.Equal(Me7Eeprom95040Checksum.PageChecksumKind.Bad, Me7Eeprom95040Checksum.Validate(data).PageChecksumKinds[28]);
        }

        [Fact]
        public void Validate_without_table_counts_matching_page_20_only()
        {
            var data = BuildImageWithValidChecksums();
            WriteStoredChecksum(data, 20, 0x0037);

            var matched = Me7Eeprom95040Checksum.Validate(data);

            Assert.Equal(Me7Eeprom95040Checksum.PageChecksumKind.Ok, matched.PageChecksumKinds[20]);
            Assert.True(matched.AllChecksumPagesValid);

            byte stored14 = data[20 * 16 + 14];
            byte stored15 = data[20 * 16 + 15];
            data[20 * 16 + 14] ^= 0xFF;
            data[20 * 16 + 15] ^= 0xFF;

            var mismatched = Me7Eeprom95040Checksum.Validate(data);

            Assert.Equal(Me7Eeprom95040Checksum.PageChecksumKind.None, mismatched.PageChecksumKinds[20]);
            Assert.True(mismatched.AllChecksumPagesValid);

            int updated = Me7Eeprom95040Checksum.CorrectChecksums(data);

            Assert.Equal(0, updated);
            Assert.Equal((byte)(stored14 ^ 0xFF), data[20 * 16 + 14]);
            Assert.Equal((byte)(stored15 ^ 0xFF), data[20 * 16 + 15]);
        }

        [Fact]
        public void Image_table_rewrites_page_20_and_leaves_pages_28_and_29()
        {
            ushort[] table = ShippedTable();
            table[20] = 0x0C37;
            var data = BuildImageWithValidChecksums(table);
            data[20 * 16 + 14] ^= 0xFF;
            data[20 * 16 + 15] ^= 0xFF;
            byte p28_14 = data[28 * 16 + 14] = 0x11;
            byte p28_15 = data[28 * 16 + 15] = 0x22;

            var before = Me7Eeprom95040Checksum.Validate(data, 0, table);

            Assert.Equal(Me7Eeprom95040Checksum.PageChecksumKind.Bad, before.PageChecksumKinds[20]);
            Assert.False(before.AllChecksumPagesValid);

            int updated = Me7Eeprom95040Checksum.CorrectChecksums(data, 0, table);

            Assert.Equal(1, updated);
            Assert.Equal(p28_14, data[28 * 16 + 14]);
            Assert.Equal(p28_15, data[28 * 16 + 15]);
            Assert.Equal(Me7Eeprom95040Checksum.PageChecksumKind.Ok, Me7Eeprom95040Checksum.Validate(data, 0, table).PageChecksumKinds[20]);
            Assert.Equal(Me7Eeprom95040Checksum.PageChecksumKind.Bad, Me7Eeprom95040Checksum.Validate(data, 0, table).PageChecksumKinds[28]);
        }

        [Fact]
        public void TryCopyImageTable_reads_32_words_at_the_prefix()
        {
            ushort[] table = ShippedTable();
            table[20] = 0x0C37;
            table[28] = 0x1335;
            var flash = new byte[80 + (Me7Eeprom95040Checksum.PageCount * 2)];
            WriteWords(flash, 40, table);

            ushort[] found = Me7Eeprom95040Checksum.TryCopyImageTable(flash);

            Assert.NotNull(found);
            Assert.Equal(0x0C37, found[20]);
            Assert.Equal(0x1335, found[28]);
            Assert.Equal(Me7Eeprom95040Checksum.GetPageDescriptor(0), found[0]);
            Assert.Equal(Me7Eeprom95040Checksum.GetPageDescriptor(19), found[19]);
        }

        [Fact]
        public void TryCopyImageTable_misses_a_short_prefix()
        {
            ushort[] prefix = ShippedTable();
            var flash = new byte[40];
            WriteWords(flash, 0, prefix, 20);

            Assert.Null(Me7Eeprom95040Checksum.TryCopyImageTable(flash));
            Assert.Null(Me7Eeprom95040Checksum.TryCopyImageTable(new byte[64]));
        }

        [Fact]
        public void OversizedReadStatus_names_a_95040_and_an_unverified_size()
        {
            const string device = "95080 SSC 1024 B";
            var image = new byte[1024];
            BuildImageWithValidChecksums().CopyTo(image, 0);

            Assert.Equal(
                "EEPROM looks like a 95040; selected device is " + device + ".",
                Me7Eeprom95040Checksum.OversizedReadStatus(image, device));

            image[1 * 16 + 14] ^= 0xFF;
            image[15] = 0x00;

            Assert.Equal(
                "EEPROM size is unverified; selected device is " + device + ".",
                Me7Eeprom95040Checksum.OversizedReadStatus(image, device));

            image[15] = Me7Eeprom95040Checksum.Page0Signature;

            Assert.Null(Me7Eeprom95040Checksum.OversizedReadStatus(image, device));
        }

        [Fact]
        public void OversizedReadStatus_scores_a_95P08_on_the_first_512()
        {
            const string device = "95P08 SSC 1024 B";
            ushort[] table = ShippedTable();
            table[20] = 0x0C37;
            var image = new byte[Me7Eeprom95P08Immo.EepromSize];
            BuildImageWithValidChecksums(table).CopyTo(image, 0);
            image[600] = 0xA5;

            var ok = Me7Eeprom95040Checksum.Validate(image, 0, table);
            Assert.True(ok.AllChecksumPagesValid);
            Assert.Equal(ok.FormatStatusMessage(chip: "95P08"), Me7Eeprom95040Checksum.OversizedReadStatus(image, device, table));

            image[20 * 16 + 14] ^= 0xFF;
            var failed = Me7Eeprom95040Checksum.Validate(image, 0, table);
            Assert.Equal(Me7Eeprom95040Checksum.PageChecksumKind.Bad, failed.PageChecksumKinds[20]);
            Assert.Equal(failed.FormatStatusMessage(chip: "95P08"), Me7Eeprom95040Checksum.OversizedReadStatus(image, device, table));

            Assert.Equal(1, Me7Eeprom95040Checksum.CorrectChecksums(image, 0, table));
            Assert.Equal(0xA5, image[600]);
            Assert.Equal(Me7Eeprom95040Checksum.PageChecksumKind.Ok, Me7Eeprom95040Checksum.Validate(image, 0, table).PageChecksumKinds[20]);
        }

        [Fact]
        public void P63SizeAdvice_only_when_every_page_fails_on_port_6_pin_3()
        {
            var blank = new byte[Me7Eeprom95040Checksum.EepromSize];
            Array.Fill(blank, (byte)0xFF);
            var failed = Me7Eeprom95040Checksum.Validate(blank);
            var p63 = Pin(6, 3);

            Assert.Equal("Try 95P08, 1024 bytes.", Me7Eeprom95040Checksum.P63SizeAdvice(p63, failed));
            Assert.Null(Me7Eeprom95040Checksum.P63SizeAdvice(Pin(4, 7), failed));

            byte[] almost = BuildImageWithValidChecksums();
            Assert.Null(Me7Eeprom95040Checksum.P63SizeAdvice(p63, Me7Eeprom95040Checksum.Validate(almost)));
            almost[1 * 16 + 14] ^= 0xFF;
            Assert.Null(Me7Eeprom95040Checksum.P63SizeAdvice(p63, Me7Eeprom95040Checksum.Validate(almost)));
        }

        [Fact]
        public void MapOffset_stays_at_byte_0_when_any_shipped_page_matches()
        {
            byte[] image = BuildImageWithValidChecksums();

            Assert.Equal(0, Me7Eeprom95040Checksum.MapOffset(image));
            Assert.Null(Me7Eeprom95040Checksum.ShiftedMatchStatus(image, "95040"));

            image[1 * 16 + 14] ^= 0xFF;
            Assert.Equal(0, Me7Eeprom95040Checksum.MapOffset(image));
            Assert.Null(Me7Eeprom95040Checksum.ShiftedMatchStatus(image, "95040"));
        }

        [Fact]
        public void MapOffset_uses_byte_1_only_after_eight_formula_matches()
        {
            byte[] eight = ShiftedImage(1024, 8);
            Assert.Equal(0, Me7Eeprom95040Checksum.CountFormulaMatches(eight, 0));
            Assert.Equal(8, Me7Eeprom95040Checksum.CountFormulaMatches(eight, 1));
            Assert.Equal(1, Me7Eeprom95040Checksum.MapOffset(eight));
            Assert.Equal(
                "ME7 95P08 page checksums match (8 pages).",
                Me7Eeprom95040Checksum.OversizedReadStatus(eight, "95P08 SSC 1024 B"));

            byte[] seven = ShiftedImage(1024, 7);
            Assert.Equal(0, Me7Eeprom95040Checksum.CountFormulaMatches(seven, 0));
            Assert.Equal(7, Me7Eeprom95040Checksum.CountFormulaMatches(seven, 1));
            Assert.Equal(0, Me7Eeprom95040Checksum.MapOffset(seven));
            Assert.Null(Me7Eeprom95040Checksum.ShiftedMatchStatus(seven, "95P08"));
        }

        [Fact]
        public void CorrectEditedPages_rewrites_only_a_page_that_already_matched()
        {
            byte[] image = ShiftedImage(Me7Eeprom95P08Immo.EepromSize, 8);
            image[1 + Me7Eeprom95P08Immo.ImmoOffset1] = Me7Eeprom95P08Immo.ImmoOn;
            image[1 + Me7Eeprom95P08Immo.ImmoOffset2] = Me7Eeprom95P08Immo.ImmoOn;
            StoreFormula(image, 1, 1);
            StoreFormula(image, 1, 2);
            int unmatched = 1 + (10 * Me7Eeprom95040Checksum.PageSize);
            image[unmatched + 14] = 0x12;
            image[unmatched + 15] = 0x34;
            Assert.False(Me7Eeprom95040Checksum.TryMatchBit6(image, 10, 1, out _));
            Assert.True(Me7Eeprom95040Checksum.TryMatchBit6(image, 0, 1, out bool bit6));

            byte[] edited = (byte[])image.Clone();
            edited[1] ^= 0x01;
            Assert.Equal(1, Me7Eeprom95040Checksum.CorrectEditedPages(edited, image, 1));
            Assert.Equal(0x12, edited[unmatched + 14]);
            Assert.Equal(0x34, edited[unmatched + 15]);
            ushort stored = (ushort)(edited[1 + 14] | (edited[1 + 15] << 8));
            Assert.Equal(Formula(edited, 1, 0, bit6), stored);
            Assert.NotEqual(Formula(edited, 1, 0, !bit6), stored);
        }

        [Fact]
        public void Byte_1_immo_off_rewrites_pages_that_already_matched()
        {
            byte[] image = ShiftedImage(Me7Eeprom95P08Immo.EepromSize, 8);
            Assert.Equal(Me7Eeprom95040Immo.Result.Applied, Me7Eeprom95P08Immo.TryDisable(image));
            Assert.Equal(Me7Eeprom95P08Immo.ImmoOff, image[1 + Me7Eeprom95P08Immo.ImmoOffset1]);
            Assert.Equal(Me7Eeprom95P08Immo.ImmoOff, image[1 + Me7Eeprom95P08Immo.ImmoOffset2]);
            Assert.True(Me7Eeprom95040Checksum.TryMatchBit6(image, 1, 1, out _));
            Assert.True(Me7Eeprom95040Checksum.TryMatchBit6(image, 2, 1, out _));
            Assert.Equal(0xA5, image[0]);

            byte[] afterImmo = (byte[])image.Clone();
            Assert.Equal(Me7Eeprom95040Page3031.Result.Refused, Me7Eeprom95040Page3031.TryClearP0602(image));
            Assert.Equal(Me7Eeprom95040Page3031.Result.Refused, Me7Eeprom95040Page3031.TryResetLockout(image));
            Assert.Equal(afterImmo, image);

            byte[] half = ShiftedImage(Me7Eeprom95040Checksum.EepromSize, 8);
            Assert.Equal(Me7Eeprom95040Immo.Result.Applied, Me7Eeprom95040Immo.TryDisable(half));
            Assert.Equal(Me7Eeprom95040Fields.ImmoOff, half[1 + Me7Eeprom95P08Immo.ImmoOffset1]);
            Assert.Equal(0xA5, half[0]);
        }

        [Fact]
        public void Byte_1_immo_off_refuses_when_a_page_does_not_match()
        {
            byte[] image = ShiftedImage(Me7Eeprom95P08Immo.EepromSize, 0);
            foreach (int page in new[] { 0, 2, 3, 4, 5, 6, 7, 8 })
            {
                StoreFormula(image, 1, page);
            }

            Assert.Equal(1, Me7Eeprom95040Checksum.MapOffset(image));
            Assert.False(Me7Eeprom95040Checksum.TryMatchBit6(image, 1, 1, out _));
            byte[] before = (byte[])image.Clone();
            Assert.Equal(Me7Eeprom95040Immo.Result.Refused, Me7Eeprom95P08Immo.TryDisable(image));
            Assert.Equal(before, image);
        }

        [Fact]
        public void A_512_byte_window_does_not_score_or_rewrite_an_incomplete_last_page()
        {
            byte[] full = ShiftedImage(Me7Eeprom95P08Immo.EepromSize, 8);
            StoreFormula(full, 1, 31);
            Assert.Equal(9, Me7Eeprom95040Checksum.CountFormulaMatches(full, 1));

            var image = new byte[Me7Eeprom95040Checksum.EepromSize];
            Array.Copy(full, image, image.Length);
            Assert.Equal(8, Me7Eeprom95040Checksum.CountFormulaMatches(image, 1));
            Assert.Equal(1, Me7Eeprom95040Checksum.MapOffset(image));
            Assert.False(Me7Eeprom95040Checksum.TryMatchBit6(image, 31, 1, out _));

            byte[] edited = (byte[])image.Clone();
            edited[1] ^= 0x01;
            byte tail = edited[edited.Length - 1];
            Assert.Equal(1, Me7Eeprom95040Checksum.CorrectEditedPages(edited, image, 1));
            Assert.Equal(tail, edited[edited.Length - 1]);

            var shipped = Me7Eeprom95040Checksum.Validate(image);
            Assert.True(shipped.PagesChecked > 0);
            Assert.Equal(shipped.PagesChecked, shipped.PagesInvalid);
            Assert.Equal(
                "Try 95P08, 1024 bytes.",
                Me7Eeprom95040Checksum.P63SizeAdvice(Pin(6, 3), shipped));
            Assert.Equal(
                "ME7 95040 page checksums match (8 pages).",
                Me7Eeprom95040Checksum.ShiftedMatchStatus(image, "95040"));
        }

        private static byte[] ShiftedImage(int length, int matches)
        {
            var data = new byte[length];
            Array.Fill(data, (byte)0xA5);
            for (int page = 0; page < matches; page++)
            {
                StoreFormula(data, 1, page);
            }

            return data;
        }

        private static void StoreFormula(byte[] data, int dataOffset, int page)
        {
            int at = dataOffset + (page * Me7Eeprom95040Checksum.PageSize);
            data[at] = (byte)(0x30 + page);
            data[at + 3] = 0x5A;
            ushort cs = Formula(data, dataOffset, page, page % 2 == 0);
            data[at + 14] = (byte)(cs & 0xFF);
            data[at + 15] = (byte)(cs >> 8);
        }

        private static ushort Formula(byte[] data, int dataOffset, int page, bool bit6)
        {
            int at = dataOffset + (page * Me7Eeprom95040Checksum.PageSize);
            ushort descriptor = bit6 ? (ushort)0x0041 : ChecksumPresentMask;
            return Me7Eeprom95040Checksum.CalculatePageChecksum(data, at, (ushort)page, descriptor);
        }

        private static BootstrapInterface.BootmodeEepromSettings Pin(byte port, byte pin)
        {
            return new BootstrapInterface.BootmodeEepromSettings
            {
                PortNumber = port,
                PinNumber = pin,
                EepromType = BootstrapInterface.BootmodeEepromType.Type95040,
                Size = Me7Eeprom95040Checksum.EepromSize
            };
        }

        private static byte[] BuildImageWithValidChecksums(ushort[]? table = null)
        {
            var data = new byte[Me7Eeprom95040Checksum.EepromSize];
            for (int page = 0; page < Me7Eeprom95040Checksum.PageCount; page++)
            {
                ushort descriptor = table != null ? table[page] : Me7Eeprom95040Checksum.GetPageDescriptor(page);
                if ((descriptor & ChecksumPresentMask) == 0)
                {
                    continue;
                }

                WriteStoredChecksum(data, page, descriptor);
            }

            return data;
        }

        private static void WriteStoredChecksum(byte[] data, int page, ushort descriptor)
        {
            int pageOffset = page * Me7Eeprom95040Checksum.PageSize;
            ushort cs = Me7Eeprom95040Checksum.CalculatePageChecksum(
                data,
                pageOffset,
                (ushort)page,
                descriptor);
            data[pageOffset + 14] = (byte)(cs & 0xFF);
            data[pageOffset + 15] = (byte)(cs >> 8);
        }

        private static ushort[] ShippedTable()
        {
            var table = new ushort[Me7Eeprom95040Checksum.PageCount];
            for (int page = 0; page < table.Length; page++)
            {
                table[page] = Me7Eeprom95040Checksum.GetPageDescriptor(page);
            }

            return table;
        }

        private static void WriteWords(byte[] dest, int at, ushort[] words, int count = -1)
        {
            if (count < 0)
            {
                count = words.Length;
            }

            for (int i = 0; i < count; i++)
            {
                dest[at + (i * 2)] = (byte)words[i];
                dest[at + (i * 2) + 1] = (byte)(words[i] >> 8);
            }
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
