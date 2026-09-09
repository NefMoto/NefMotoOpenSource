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
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using Communication;
using Shared;

namespace ECUFlasher
{
    /// <summary>
    /// Path-independent user prefs next to the log. Replaces hashed user.config.
    /// </summary>
    public sealed class UserPreferences
    {
        public const string FileName = "preferences.json";

        static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };

        public WindowState WindowState { get; set; } = WindowState.Maximized;
        public double WindowTop { get; set; }
        public double WindowLeft { get; set; }
        public double WindowWidth { get; set; } = 700;
        public double WindowHeight { get; set; } = 600;
        public string FlashFile { get; set; } = "";
        public string MemoryLayoutFile { get; set; } = "";
        public string LastBrowseDirectory { get; set; } = "";
        public SavedDevice LastDevice { get; set; }
        public CommunicationInterface.Protocol DesiredProtocol { get; set; } = CommunicationInterface.Protocol.KWP2000;
        public uint DesiredKWP2000BaudRate { get; set; }
        public KwpConnectionMethod DesiredKWP2000ConnectionMethod { get; set; } = KwpConnectionMethod.SlowInit;
        public bool EnableSlowInitTimingLog { get; set; }
        public uint DesiredBootModeBaudRate { get; set; }

        public static UserPreferences Load(string path, out string error, out string migratedFrom)
        {
            error = null;
            migratedFrom = null;

            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    var loaded = JsonSerializer.Deserialize<UserPreferences>(json, JsonOptions);
                    if (loaded == null)
                    {
                        return new UserPreferences();
                    }

                    loaded.FlashFile ??= "";
                    loaded.MemoryLayoutFile ??= "";
                    loaded.LastBrowseDirectory ??= "";
                    loaded.InheritBrowseDirectoryFromFlashFile();
                    return loaded;
                }

                var prefs = new UserPreferences();
                var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (LegacyUserConfig.TryFindBest(localAppData, out string sourcePath, out LegacyMigratedSettings legacy))
                {
                    prefs.ApplyLegacy(legacy);
                    migratedFrom = sourcePath;
                }

                return prefs;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return new UserPreferences();
            }
        }

        public void ApplyLegacy(LegacyMigratedSettings legacy)
        {
            if (legacy == null)
            {
                return;
            }

            if (!string.IsNullOrEmpty(legacy.WindowState) &&
                Enum.TryParse(legacy.WindowState, true, out WindowState windowState))
            {
                WindowState = windowState;
            }

            if (legacy.WindowTop.HasValue)
            {
                WindowTop = legacy.WindowTop.Value;
            }
            if (legacy.WindowLeft.HasValue)
            {
                WindowLeft = legacy.WindowLeft.Value;
            }
            if (legacy.WindowWidth.HasValue)
            {
                WindowWidth = legacy.WindowWidth.Value;
            }
            if (legacy.WindowHeight.HasValue)
            {
                WindowHeight = legacy.WindowHeight.Value;
            }

            if (!string.IsNullOrWhiteSpace(legacy.FlashFile))
            {
                FlashFile = LegacyUserConfig.RemapInstallPath(legacy.FlashFile);
            }
            if (!string.IsNullOrWhiteSpace(legacy.MemoryLayoutFile))
            {
                MemoryLayoutFile = LegacyUserConfig.RemapInstallPath(legacy.MemoryLayoutFile);
            }

            if (legacy.EnableSlowInitTimingLog.HasValue)
            {
                EnableSlowInitTimingLog = legacy.EnableSlowInitTimingLog.Value;
            }

            if (legacy.DesiredKWP2000BaudRate.GetValueOrDefault() != 0)
            {
                DesiredKWP2000BaudRate = legacy.DesiredKWP2000BaudRate.Value;
            }
            if (legacy.DesiredBootModeBaudRate.GetValueOrDefault() != 0)
            {
                DesiredBootModeBaudRate = legacy.DesiredBootModeBaudRate.Value;
            }

            LastDevice = SavedDevice.FromLegacy(legacy.FtdiUsbDevice);

            InheritBrowseDirectoryFromFlashFile();
        }

        public void InheritBrowseDirectoryFromFlashFile()
        {
            if (SafeFileDialogDirectory.Exists(LastBrowseDirectory))
            {
                return;
            }

            var fromFlash = SafeFileDialogDirectory.ParentIfExists(FlashFile);
            if (fromFlash != null)
            {
                LastBrowseDirectory = fromFlash;
            }
        }

        public void RememberBrowsePath(string filePath)
        {
            var dir = SafeFileDialogDirectory.ParentIfExists(filePath);
            if (dir != null)
            {
                LastBrowseDirectory = dir;
            }
        }

        public static void Save(string path, UserPreferences prefs)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("path is required", nameof(path));
            }

            prefs ??= new UserPreferences();
            prefs.LastBrowseDirectory ??= "";
            prefs.FlashFile ??= "";
            prefs.MemoryLayoutFile ??= "";

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tmp = path + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(prefs, JsonOptions));
            File.Move(tmp, path, overwrite: true);
        }
    }

    public enum KwpConnectionMethod
    {
        SlowInit = 0,
        FastInit
    }

    public sealed class SavedDevice
    {
        public DeviceType Kind { get; set; }
        public string Description { get; set; }
        public string SerialNumber { get; set; }
        public string PortName { get; set; }

        public static SavedDevice FromDevice(DeviceInfo info)
        {
            if (info is FtdiDeviceInfo ftdi)
            {
                return new SavedDevice
                {
                    Kind = DeviceType.FTDI,
                    Description = ftdi.Description,
                    SerialNumber = ftdi.SerialNumber,
                };
            }

            if (info is Ch340DeviceInfo ch340)
            {
                return new SavedDevice
                {
                    Kind = DeviceType.CH340,
                    Description = ch340.Description,
                    SerialNumber = ch340.SerialNumber,
                    PortName = ch340.PortName,
                };
            }

            return null;
        }

        public static SavedDevice FromLegacy(LegacyFtdiDevice ftdi)
        {
            if (string.IsNullOrWhiteSpace(ftdi?.SerialNumber))
            {
                return null;
            }

            return new SavedDevice
            {
                Kind = DeviceType.FTDI,
                Description = ftdi.Description,
                SerialNumber = ftdi.SerialNumber,
            };
        }

        public bool Matches(DeviceInfo info)
        {
            if (info == null || info.Type != Kind)
            {
                return false;
            }

            if (info is FtdiDeviceInfo ftdi)
            {
                return !string.IsNullOrEmpty(SerialNumber) && ftdi.SerialNumber == SerialNumber;
            }

            if (info is Ch340DeviceInfo ch340)
            {
                if (!string.IsNullOrEmpty(SerialNumber) && !string.IsNullOrEmpty(ch340.SerialNumber) &&
                    ch340.SerialNumber == SerialNumber)
                {
                    return true;
                }

                return !string.IsNullOrEmpty(PortName) && ch340.PortName == PortName;
            }

            return false;
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
