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
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Shared
{
    /// <summary>
    /// Finds leftover LocalFileSettingsProvider user.config files under
    /// LocalAppData. Those paths hash the exe location, so Debug vs MSI vs
    /// Program Files vs Program Files (x86) each get an empty first-run store.
    /// </summary>
    public static class UserConfigLocator
    {
        public const string FileName = "user.config";
        public const string X86InstallPrefix = @"C:\Program Files (x86)\NefMotoECUFlasher";
        public const string X64InstallPrefix = @"C:\Program Files\NefMotoECUFlasher";

        public static string GetStablePath(string roamingAppData, string companyName, string applicationName)
        {
            return Path.Combine(roamingAppData, companyName, applicationName, FileName);
        }

        public static string FindBest(string localAppData)
        {
            return EnumerateLegacyUserConfigs(localAppData)
                .Select(path => new
                {
                    Path = path,
                    Score = Score(path),
                    Time = SafeLastWriteUtc(path)
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Time)
                .Select(x => x.Path)
                .FirstOrDefault();
        }

        public static bool TryMigrate(string localAppData, string stablePath, out string sourcePath)
        {
            sourcePath = null;
            if (string.IsNullOrEmpty(stablePath) || File.Exists(stablePath))
            {
                return false;
            }

            sourcePath = FindBest(localAppData);
            if (sourcePath == null)
            {
                return false;
            }

            string directory = Path.GetDirectoryName(stablePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(sourcePath, stablePath, false);
            return true;
        }

        public static string RemapNefMotoInstallPaths(string xml, bool x64InstallExists)
        {
            if (string.IsNullOrEmpty(xml) || !x64InstallExists)
            {
                return xml;
            }

            return xml.Replace(X86InstallPrefix, X64InstallPrefix, StringComparison.OrdinalIgnoreCase);
        }

        public static int Score(string userConfigPath)
        {
            XDocument document;
            try
            {
                document = XDocument.Load(userConfigPath);
            }
            catch
            {
                return 0;
            }

            int score = 0;
            foreach (XElement setting in document.Descendants("setting"))
            {
                string name = (string)setting.Attribute("name");
                if (string.Equals(name, "SettingsUpgraded", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (HasNonDefaultValue(setting))
                {
                    score++;
                }
            }

            return score;
        }

        public static IEnumerable<string> EnumerateLegacyUserConfigs(string localAppData)
        {
            if (string.IsNullOrEmpty(localAppData) || !Directory.Exists(localAppData))
            {
                yield break;
            }

            IEnumerable<string> topLevel;
            try
            {
                topLevel = Directory.EnumerateDirectories(localAppData).ToList();
            }
            catch
            {
                yield break;
            }

            foreach (string top in topLevel)
            {
                if (!LooksLikeOurs(Path.GetFileName(top)))
                {
                    continue;
                }

                foreach (string urlDir in EnumerateUrlDirs(top, 0, 3))
                {
                    foreach (string file in EnumerateUserConfigs(urlDir))
                    {
                        yield return file;
                    }
                }
            }
        }

        static bool LooksLikeOurs(string folderName)
        {
            return folderName.IndexOf("NefMoto", StringComparison.OrdinalIgnoreCase) >= 0
                || folderName.IndexOf("Nefarious", StringComparison.OrdinalIgnoreCase) >= 0
                || folderName.IndexOf("ECUFlasher", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        static IEnumerable<string> EnumerateUrlDirs(string directory, int depth, int maxDepth)
        {
            IEnumerable<string> children;
            try
            {
                children = Directory.EnumerateDirectories(directory).ToList();
            }
            catch
            {
                yield break;
            }

            foreach (string child in children)
            {
                string name = Path.GetFileName(child);
                if (name.IndexOf("_Url_", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    yield return child;
                }
                else if (depth < maxDepth)
                {
                    foreach (string nested in EnumerateUrlDirs(child, depth + 1, maxDepth))
                    {
                        yield return nested;
                    }
                }
            }
        }

        static IEnumerable<string> EnumerateUserConfigs(string urlDir)
        {
            IEnumerable<string> versions;
            try
            {
                versions = Directory.EnumerateDirectories(urlDir).ToList();
            }
            catch
            {
                yield break;
            }

            foreach (string versionDir in versions)
            {
                string file = Path.Combine(versionDir, FileName);
                if (File.Exists(file))
                {
                    yield return file;
                }
            }
        }

        static bool HasNonDefaultValue(XElement setting)
        {
            string serializeAs = (string)setting.Attribute("serializeAs") ?? "String";
            XElement value = setting.Element("value");
            if (value == null)
            {
                return false;
            }

            if (string.Equals(serializeAs, "Xml", StringComparison.OrdinalIgnoreCase))
            {
                return value.Elements().Any();
            }

            string text = (value.Value ?? "").Trim();
            if (text.Length == 0)
            {
                return false;
            }

            return text != "0"
                && text != "False"
                && text != "Maximized"
                && text != "SlowInit"
                && text != "700"
                && text != "600";
        }

        static DateTime SafeLastWriteUtc(string path)
        {
            try
            {
                return File.GetLastWriteTimeUtc(path);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
