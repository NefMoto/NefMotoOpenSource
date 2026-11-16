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
    public static class BootmodeEcuInfo
    {
        public struct Entry
        {
            public string Name;
            public string Value;
        }

        const byte CoreAlreadyRunning = 0xAA;

        public static List<Entry> Read(BootstrapInterface boot)
        {
            var entries = new List<Entry>();
            byte deviceID = boot.DeviceID;
            byte actualDeviceID = deviceID;
            if ((deviceID == CoreAlreadyRunning) && (boot.LastKnownDeviceID != 0))
            {
                actualDeviceID = boot.LastKnownDeviceID;
                Add(entries, "Device ID (Current)", "0x" + deviceID.ToString("X2") + " (Core Already Running)");
                Add(entries, "Device ID (Last Known)", "0x" + actualDeviceID.ToString("X2") + " (" + DeviceName(actualDeviceID) + ")");
            }
            else
            {
                Add(entries, "Device ID", "0x" + deviceID.ToString("X2") + " (" + DeviceName(deviceID) + ")");
            }

            Add(entries, "CPU Family", CpuFamily(actualDeviceID));
            AddWord(entries, boot, 0x00FF12, "SYSCON Register (0x00FF12)", FormatSyscon);
            AddWord(entries, boot, 0x00FF0C, "BUSCON0 Register (0x00FF0C)", FormatBuscon);
            AddWord(entries, boot, 0x00FF14, "BUSCON1 Register (0x00FF14)", FormatBuscon);

            uint port2;
            if (boot.MiniMon_ReadWord(0xFFC0, out port2))
            {
                string binary = System.Convert.ToString(port2, 2).PadLeft(16, '0');
                Add(entries, "Port 2 Register (0xFFC0)", "0x" + port2.ToString("X4") + " (Binary: " + binary + ")");
            }

            AddWord(entries, boot, 0x00FE18, "ADDRSEL1 Register (0x00FE18)", FormatAddrsel);
            AddWord(entries, boot, 0x00FE1A, "ADDRSEL2 Register (0x00FE1A)", FormatAddrsel);

            uint addrsel3;
            if (boot.MiniMon_ReadWord(0x00FE1C, out addrsel3) && (addrsel3 != 0))
            {
                Add(entries, "ADDRSEL3 Register (0x00FE1C)", FormatAddrsel(addrsel3));
            }

            byte[] ram;
            if (boot.MiniMon_ReadBlock(0x00F600, 16, out ram))
            {
                bool allZero = true;
                bool allFF = true;
                for (int i = 0; i < ram.Length; i++)
                {
                    if (ram[i] != 0)
                    {
                        allZero = false;
                    }

                    if (ram[i] != 0xFF)
                    {
                        allFF = false;
                    }
                }

                string ramStatus = allZero ? "All zeros (uninitialized)" : (allFF ? "All 0xFF (erased)" : "Contains data");
                Add(entries, "Internal RAM (0xF600, 16 bytes)", ramStatus);
            }

            boot.GetBootmodeFlashLayout(out _, out _);
            var connState = boot.GetBootmodeConnectionState();
            switch (connState.FlashLayoutStatus)
            {
                case BootstrapInterface.BootmodeFlashLayoutStatus.Available:
                    var layout = connState.FlashLayout;
                    string details = "Available";
                    if ((layout != null) && layout.Validate() && (layout.SectorSizes != null))
                    {
                        details = "Available: Base 0x" + layout.BaseAddress.ToString("X6") + ", " + (layout.Size / 1024) + " KB, " + layout.SectorSizes.Count + " sectors";
                    }

                    Add(entries, "Flash Layout", details);
                    break;
                case BootstrapInterface.BootmodeFlashLayoutStatus.Unavailable:
                    string reason = string.IsNullOrEmpty(connState.FlashLayoutUnavailableReason) ? "Unknown reason" : connState.FlashLayoutUnavailableReason;
                    Add(entries, "Flash Layout", "Unavailable: " + reason);
                    break;
                default:
                    Add(entries, "Flash Layout", "Not yet detected");
                    break;
            }

            return entries;
        }

        static void Add(List<Entry> entries, string name, string value)
        {
            entries.Add(new Entry { Name = name, Value = value });
        }

        static void AddWord(List<Entry> entries, BootstrapInterface boot, uint address, string name, System.Func<uint, string> format)
        {
            uint value;
            if (boot.MiniMon_ReadWord(address, out value))
            {
                Add(entries, name, format(value));
            }
        }

        static string DeviceName(byte deviceID)
        {
            switch (deviceID)
            {
                case 0x55: return "C166";
                case 0xA5: return "C167 (Old)";
                case 0xB5: return "C165";
                case 0xC5: return "C167";
                case 0xD5: return "C167 (With ID) / ST10";
                case 0xAA: return "Core Already Running";
                default: return "Unknown";
            }
        }

        static string CpuFamily(byte deviceID)
        {
            switch (deviceID)
            {
                case 0x55: return "C16x Family (C166)";
                case 0xA5: return "C16x Family (C167 Old)";
                case 0xB5: return "C16x Family (C165)";
                case 0xC5: return "C16x Family (C167)";
                case 0xD5: return "C16x/ST10 Family (C167 with ID or ST10)";
                case 0xAA: return "C16x/ST10 Family (Cannot determine specific variant - Core Already Running)";
                default: return "Unknown CPU Family";
            }
        }

        static string FormatAddrsel(uint value)
        {
            if (value == 0)
            {
                return "0x" + value.ToString("X4") + " - Disabled/Not configured";
            }

            uint segmentAddr = (value >> 12) & 0xF;
            uint windowSize = (value >> 8) & 0xF;
            string windowSizeStr;
            switch (windowSize)
            {
                case 0: windowSizeStr = "32 KB"; break;
                case 1: windowSizeStr = "64 KB"; break;
                case 2: windowSizeStr = "128 KB"; break;
                case 3: windowSizeStr = "256 KB"; break;
                case 4: windowSizeStr = "512 KB"; break;
                case 5: windowSizeStr = "1 MB"; break;
                case 6: windowSizeStr = "2 MB"; break;
                case 7: windowSizeStr = "4 MB"; break;
                case 8: windowSizeStr = "1024 KB"; break;
                default: windowSizeStr = windowSize + " (unknown)"; break;
            }

            uint baseAddress = segmentAddr << 20;
            return "0x" + value.ToString("X4") + " - Base: 0x" + baseAddress.ToString("X6") + ", Window: " + windowSizeStr;
        }

        static string FormatSyscon(uint value)
        {
            uint stackSize = (value >> 13) & 0x7;
            bool romEnabled = ((value >> 10) & 0x1) != 0;
            bool byteDisable = ((value >> 9) & 0x1) != 0;
            bool extBusEnabled = ((value >> 2) & 0x1) != 0;
            bool visibleMode = ((value >> 1) & 0x1) != 0;
            return "0x" + value.ToString("X4")
                + " - Stack: " + stackSize
                + ", ROM: " + (romEnabled ? "On" : "Off")
                + ", ByteMode: " + (byteDisable ? "Word" : "Byte")
                + ", ExtBus: " + (extBusEnabled ? "On" : "Off")
                + ", Visible: " + (visibleMode ? "Yes" : "No");
        }

        static string FormatBuscon(uint value)
        {
            uint waitStates = (value >> 12) & 0xF;
            uint busConfig = (value >> 8) & 0xF;
            bool extBusActive = ((value >> 2) & 0x1) != 0;
            string busType = "Unknown";
            if ((busConfig & 0x8) != 0)
            {
                busType = ((busConfig & 0x4) != 0) ? "16-bit Demux" : "8-bit Demux";
            }

            return "0x" + value.ToString("X4")
                + " - Wait States: " + waitStates
                + ", Bus: " + busType
                + ", ExtBus: " + (extBusActive ? "Active" : "Inactive");
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
