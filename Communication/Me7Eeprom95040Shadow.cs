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

using System.Collections.Generic;

namespace Communication
{
    /// <summary>
    /// Fills 95040 pages the KWP mirror at 0x600000 withholds (pages 3-4, SKC and cluster
    /// code, answer 7F 23 12) from the firmware's RAM shadow. The shadow holds one 16-byte
    /// slot per record id (descriptor high byte), at a build-specific base. The base is
    /// found by matching the pages the mirror did serve, and a filled page is kept only
    /// when its own checksum is valid.
    /// </summary>
    public static class Me7Eeprom95040Shadow
    {
        public const byte NoRecord = 0xFF;

        /// <summary>Pages that must agree with the shadow before its base is trusted.</summary>
        public const int MinMatchedPages = 4;

        public sealed class FillResult
        {
            public bool ShadowFound { get; set; }
            public uint ShadowAddress { get; set; }
            public int MatchedPages { get; set; }
            public List<int> FilledPages { get; } = new List<int>();

            /// <summary>Missing pages whose shadow slot failed the page checksum.</summary>
            public List<int> RejectedPages { get; } = new List<int>();
        }

        public static byte GetRecordId(int page)
        {
            return (byte)(Me7Eeprom95040Checksum.GetPageDescriptor(page) >> 8);
        }

        /// <summary>
        /// Fills the pages with havePage[page] == false in image from ram (read at ramAddress).
        /// Filled pages are marked in havePage.
        /// </summary>
        public static FillResult Fill(byte[] image, bool[] havePage, byte[] ram, uint ramAddress)
        {
            var result = new FillResult();
            if (image == null || image.Length < Me7Eeprom95040Checksum.EepromSize || havePage == null
                || havePage.Length < Me7Eeprom95040Checksum.PageCount || ram == null)
            {
                return result;
            }

            int bestBase = -1;
            int bestScore = 0;
            bool tied = false;
            var scored = new HashSet<int>();

            for (int page = 0; page < Me7Eeprom95040Checksum.PageCount; page++)
            {
                if (!havePage[page] || GetRecordId(page) == NoRecord || !IsDistinctive(image, page))
                {
                    continue;
                }

                for (int offset = 0; offset + Me7Eeprom95040Checksum.PageSize <= ram.Length; offset += 2)
                {
                    int shadowBase = offset - (GetRecordId(page) * Me7Eeprom95040Checksum.PageSize);
                    if (shadowBase < 0 || !SlotMatches(image, page, ram, offset) || !scored.Add(shadowBase))
                    {
                        continue;
                    }

                    int score = Score(image, havePage, ram, shadowBase);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestBase = shadowBase;
                        tied = false;
                    }
                    else if (score == bestScore)
                    {
                        tied = true;
                    }
                }
            }

            if (bestBase < 0 || tied || bestScore < MinMatchedPages)
            {
                return result;
            }

            result.ShadowFound = true;
            result.ShadowAddress = ramAddress + (uint)bestBase;
            result.MatchedPages = bestScore;

            for (int page = 0; page < Me7Eeprom95040Checksum.PageCount; page++)
            {
                byte record = GetRecordId(page);
                if (havePage[page] || record == NoRecord)
                {
                    continue;
                }

                int slot = bestBase + (record * Me7Eeprom95040Checksum.PageSize);
                if (slot + Me7Eeprom95040Checksum.PageSize > ram.Length)
                {
                    continue;
                }

                var candidate = (byte[])image.Clone();
                System.Buffer.BlockCopy(ram, slot, candidate, page * Me7Eeprom95040Checksum.PageSize, Me7Eeprom95040Checksum.PageSize);
                if (Me7Eeprom95040Checksum.Validate(candidate).PageChecksumKinds[page] != Me7Eeprom95040Checksum.PageChecksumKind.Ok)
                {
                    result.RejectedPages.Add(page);
                    continue;
                }

                System.Buffer.BlockCopy(ram, slot, image, page * Me7Eeprom95040Checksum.PageSize, Me7Eeprom95040Checksum.PageSize);
                havePage[page] = true;
                result.FilledPages.Add(page);
            }

            return result;
        }

        private static int Score(byte[] image, bool[] havePage, byte[] ram, int shadowBase)
        {
            int score = 0;
            for (int page = 0; page < Me7Eeprom95040Checksum.PageCount; page++)
            {
                byte record = GetRecordId(page);
                if (!havePage[page] || record == NoRecord || !IsDistinctive(image, page))
                {
                    continue;
                }

                int slot = shadowBase + (record * Me7Eeprom95040Checksum.PageSize);
                if (slot + Me7Eeprom95040Checksum.PageSize <= ram.Length && SlotMatches(image, page, ram, slot))
                {
                    score++;
                }
            }

            return score;
        }

        private static bool SlotMatches(byte[] image, int page, byte[] ram, int offset)
        {
            int pageOffset = page * Me7Eeprom95040Checksum.PageSize;
            for (int i = 0; i < Me7Eeprom95040Checksum.PageSize; i++)
            {
                if (image[pageOffset + i] != ram[offset + i])
                {
                    return false;
                }
            }

            return true;
        }

        // A page of zeros or FF matches anywhere, so it neither locates nor confirms the shadow.
        private static bool IsDistinctive(byte[] image, int page)
        {
            var values = new HashSet<byte>();
            int pageOffset = page * Me7Eeprom95040Checksum.PageSize;
            for (int i = 0; i < Me7Eeprom95040Checksum.PageSize; i++)
            {
                values.Add(image[pageOffset + i]);
            }

            return values.Count >= 4;
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
