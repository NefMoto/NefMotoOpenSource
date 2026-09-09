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
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Shared
{
    /// <summary>
    /// One-time copy of leftover LocalFileSettingsProvider fields into JSON.
    /// Does not copy connect method. Does not delete sources.
    /// </summary>
    public sealed class LegacyFtdiDevice
    {
        public string Description { get; set; }
        public string SerialNumber { get; set; }
    }

    public sealed class LegacyMigratedSettings
    {
        public string WindowState { get; set; }
        public double? WindowTop { get; set; }
        public double? WindowLeft { get; set; }
        public double? WindowWidth { get; set; }
        public double? WindowHeight { get; set; }
        public string FlashFile { get; set; }
        public string MemoryLayoutFile { get; set; }
        public bool? EnableSlowInitTimingLog { get; set; }
        public uint? DesiredKWP2000BaudRate { get; set; }
        public uint? DesiredBootModeBaudRate { get; set; }
        public LegacyFtdiDevice FtdiUsbDevice { get; set; }

        public bool HasNonDefaultWindow()
        {
            if (!string.IsNullOrEmpty(WindowState) &&
                !string.Equals(WindowState, "Maximized", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (WindowTop.GetValueOrDefault() != 0 || WindowLeft.GetValueOrDefault() != 0)
            {
                return true;
            }

            if (WindowWidth.GetValueOrDefault(700) != 700 || WindowHeight.GetValueOrDefault(600) != 600)
            {
                return true;
            }

            return false;
        }

        public int Score()
        {
            int score = 0;
            if (HasNonDefaultWindow())
            {
                score++;
            }
            if (!string.IsNullOrWhiteSpace(FlashFile))
            {
                score++;
            }
            if (!string.IsNullOrWhiteSpace(MemoryLayoutFile))
            {
                score++;
            }
            if (EnableSlowInitTimingLog == true)
            {
                score++;
            }
            if (DesiredKWP2000BaudRate.GetValueOrDefault() != 0)
            {
                score++;
            }
            if (DesiredBootModeBaudRate.GetValueOrDefault() != 0)
            {
                score++;
            }
            if (!string.IsNullOrWhiteSpace(FtdiUsbDevice?.SerialNumber))
            {
                score++;
            }
            return score;
        }
    }

    public static class LegacyUserConfig
    {
        public const string FileName = "user.config";
        public const string X86InstallPrefix = @"C:\Program Files (x86)\NefMotoECUFlasher";
        public const string X64InstallPrefix = @"C:\Program Files\NefMotoECUFlasher";

        static readonly string[] CompanyFolders = { "NefMotoECUFlasher", "ECUFlasher" };

        public static IEnumerable<string> EnumerateUserConfigs(string localAppData)
        {
            if (string.IsNullOrEmpty(localAppData) || !Directory.Exists(localAppData))
            {
                yield break;
            }

            foreach (var company in CompanyFolders)
            {
                var root = Path.Combine(localAppData, company);
                if (!Directory.Exists(root))
                {
                    continue;
                }

                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(root, FileName, SearchOption.AllDirectories);
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var file in files)
                {
                    yield return file;
                }
            }
        }

        public static LegacyMigratedSettings Parse(string xml)
        {
            if (string.IsNullOrWhiteSpace(xml))
            {
                return new LegacyMigratedSettings();
            }

            var doc = XDocument.Parse(xml);
            var settings = new LegacyMigratedSettings();
            foreach (var setting in doc.Descendants("setting"))
            {
                var name = (string)setting.Attribute("name");
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var raw = setting.Element("value")?.Value ?? "";
                raw = raw.Trim();
                switch (name)
                {
                    case "WindowState":
                        settings.WindowState = raw;
                        break;
                    case "WindowTop":
                        settings.WindowTop = ParseDouble(raw);
                        break;
                    case "WindowLeft":
                        settings.WindowLeft = ParseDouble(raw);
                        break;
                    case "WindowWidth":
                        settings.WindowWidth = ParseDouble(raw);
                        break;
                    case "WindowHeight":
                        settings.WindowHeight = ParseDouble(raw);
                        break;
                    case "FlashFile":
                        settings.FlashFile = raw;
                        break;
                    case "MemoryLayoutFile":
                        settings.MemoryLayoutFile = raw;
                        break;
                    case "EnableSlowInitTimingLog":
                        if (bool.TryParse(raw, out bool timingLog))
                        {
                            settings.EnableSlowInitTimingLog = timingLog;
                        }
                        break;
                    case "DesiredKWP2000BaudRate":
                        settings.DesiredKWP2000BaudRate = ParseUInt(raw);
                        break;
                    case "DesiredBootModeBaudRate":
                        settings.DesiredBootModeBaudRate = ParseUInt(raw);
                        break;
                    case "FTDIUSBDevice":
                        settings.FtdiUsbDevice = ParseFtdi(setting.Element("value"));
                        break;
                }
            }

            return settings;
        }

        public static string RemapInstallPath(string path, Func<string, bool> fileExists = null)
        {
            if (string.IsNullOrEmpty(path))
            {
                return path;
            }

            fileExists ??= File.Exists;
            if (path.StartsWith(X86InstallPrefix, StringComparison.OrdinalIgnoreCase))
            {
                var mapped = X64InstallPrefix + path.Substring(X86InstallPrefix.Length);
                if (fileExists(mapped))
                {
                    return mapped;
                }
            }

            return path;
        }

        public static bool TryFindBest(string localAppData, out string sourcePath, out LegacyMigratedSettings settings)
        {
            sourcePath = null;
            settings = null;

            LegacyMigratedSettings best = null;
            string bestPath = null;
            int bestScore = 0;
            DateTime bestTime = DateTime.MinValue;

            foreach (var path in EnumerateUserConfigs(localAppData))
            {
                var parsed = TryParseFile(path);
                if (parsed == null)
                {
                    continue;
                }

                var score = parsed.Score();
                if (score <= 0)
                {
                    continue;
                }

                var time = SafeLastWriteUtc(path);
                if (score > bestScore || (score == bestScore && time > bestTime))
                {
                    best = parsed;
                    bestPath = path;
                    bestScore = score;
                    bestTime = time;
                }
            }

            if (best == null)
            {
                return false;
            }

            sourcePath = bestPath;
            settings = best;
            return true;
        }

        static LegacyMigratedSettings TryParseFile(string path)
        {
            try
            {
                return Parse(File.ReadAllText(path));
            }
            catch (Exception)
            {
                return null;
            }
        }

        static DateTime SafeLastWriteUtc(string path)
        {
            try
            {
                return File.GetLastWriteTimeUtc(path);
            }
            catch (Exception)
            {
                return DateTime.MinValue;
            }
        }

        static double? ParseDouble(string raw)
        {
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                return value;
            }

            return null;
        }

        static uint? ParseUInt(string raw)
        {
            if (uint.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out uint value))
            {
                return value;
            }

            return null;
        }

        static LegacyFtdiDevice ParseFtdi(XElement valueElement)
        {
            var node = valueElement?.Descendants().FirstOrDefault(e => e.Name.LocalName == "FTDIDeviceInfo");
            if (node == null)
            {
                return null;
            }

            var serial = Child(node, "SerialNumber");
            if (string.IsNullOrWhiteSpace(serial))
            {
                return null;
            }

            return new LegacyFtdiDevice
            {
                Description = Child(node, "Description"),
                SerialNumber = serial,
            };
        }

        static string Child(XElement parent, string localName)
        {
            return parent.Elements().FirstOrDefault(e => e.Name.LocalName == localName)?.Value?.Trim();
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
