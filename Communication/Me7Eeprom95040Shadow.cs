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

using System;
using System.Collections.Generic;
using System.Linq;

namespace Communication
{
    /// <summary>
    /// Fills 95040 pages the KWP mirror withholds from the firmware's RAM shadow: one 16-byte slot
    /// per record id (descriptor high byte) at a build-specific base. The base is where the most
    /// served pages match, and a filled page is kept only when its own checksum is valid.
    /// </summary>
    public static class Me7Eeprom95040Shadow
    {
        public const byte NoRecord = 0xFF;

        /// <summary>Pages that must agree with the shadow before its base is trusted.</summary>
        public const int MinMatchedPages = 4;

        private const int PageSize = Me7Eeprom95040Checksum.PageSize;

        public static byte GetRecordId(int page)
        {
            return (byte)(Me7Eeprom95040Checksum.GetPageDescriptor(page) >> 8);
        }

        /// <summary>
        /// Fills the pages with havePage[page] == false in image from ram (read at ramAddress) and
        /// marks them in havePage. Returns a status line for the log.
        /// </summary>
        public static string Fill(byte[] image, bool[] havePage, byte[] ram, uint ramAddress)
        {
            // A page of zeros or FF matches anywhere, so it neither locates nor confirms the shadow.
            var known = Enumerable.Range(0, Me7Eeprom95040Checksum.PageCount)
                .Where(page => havePage[page] && (GetRecordId(page) != NoRecord) && (image.Skip(page * PageSize).Take(PageSize).Distinct().Count() >= 4))
                .ToList();

            int Score(int shadowBase) => known.Count(page => SlotMatches(image, page, ram, shadowBase + (GetRecordId(page) * PageSize)));

            var ranked = known
                .SelectMany(page => Enumerable.Range(0, (ram.Length - PageSize) / 2 + 1)
                    .Where(half => SlotMatches(image, page, ram, half * 2))
                    .Select(half => (half * 2) - (GetRecordId(page) * PageSize)))
                .Where(shadowBase => shadowBase >= 0)
                .Distinct()
                .Select(shadowBase => (shadowBase, score: Score(shadowBase)))
                .OrderByDescending(candidate => candidate.score)
                .Take(2)
                .ToList();

            if ((ranked.Count == 0) || (ranked[0].score < MinMatchedPages) || ((ranked.Count > 1) && (ranked[1].score == ranked[0].score)))
            {
                return "EEPROM shadow not found in RAM, so withheld pages stay FF.";
            }

            int bestBase = ranked[0].shadowBase;
            var filled = new List<int>();
            var rejected = new List<int>();
            for (int page = 0; page < havePage.Length; page++)
            {
                int slot = bestBase + (GetRecordId(page) * PageSize);
                if (havePage[page] || (GetRecordId(page) == NoRecord) || (slot + PageSize > ram.Length))
                {
                    continue;
                }

                if (!SlotChecksumValid(ram, slot, page))
                {
                    rejected.Add(page);
                    continue;
                }

                Buffer.BlockCopy(ram, slot, image, page * PageSize, PageSize);
                havePage[page] = true;
                filled.Add(page);
            }

            return "EEPROM shadow found at 0x" + (ramAddress + (uint)bestBase).ToString("X6") + " (" + ranked[0].score + " pages agree with the chip)."
                + ((filled.Count > 0) ? " Filled page(s) " + string.Join(", ", filled) + "; their checksums are valid." : "")
                + ((rejected.Count > 0) ? " Page(s) " + string.Join(", ", rejected) + " failed the checksum and stay FF." : "");
        }

        private static bool SlotMatches(byte[] image, int page, byte[] ram, int offset)
        {
            return (offset >= 0) && (offset + PageSize <= ram.Length)
                && image.AsSpan(page * PageSize, PageSize).SequenceEqual(ram.AsSpan(offset, PageSize));
        }

        // Pages without a checksum can't be verified, so they are never filled.
        private static bool SlotChecksumValid(byte[] ram, int slot, int page)
        {
            ushort descriptor = Me7Eeprom95040Checksum.GetPageDescriptor(page);
            return ((descriptor & 1) != 0) && !Me7Eeprom95040Checksum.IsChecksumExemptPage(page)
                && (Me7Eeprom95040Checksum.CalculatePageChecksum(ram, slot, (ushort)page, descriptor) == (ram[slot + 14] | (ram[slot + 15] << 8)));
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
