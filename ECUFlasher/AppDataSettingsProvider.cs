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

using Shared;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace ECUFlasher
{
    /// <summary>
    /// Stores user settings next to the log under roaming AppData. The default
    /// LocalFileSettingsProvider hashes the exe path, so moving from Program
    /// Files (x86) to Program Files (or Debug vs MSI) looks like a first run.
    /// </summary>
    internal sealed class AppDataSettingsProvider : SettingsProvider, IApplicationSettingsProvider
    {
        const string DefaultGroupName = "ECUFlasher.Properties.Settings";
        const string SectionGroupType = "System.Configuration.UserSettingsGroup, System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";
        const string SectionType = "System.Configuration.ClientSettingsSection, System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089";

        readonly string mPath;
        bool mMigrated;

        internal static string MigratedFromPath { get; private set; }

        public AppDataSettingsProvider()
        {
            mPath = UserConfigLocator.GetStablePath(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Properties.Resources.CompanyName,
                Properties.Resources.ApplicationName);
        }

        public override string ApplicationName
        {
            get { return Properties.Resources.ApplicationName ?? ""; }
            set { }
        }

        public override void Initialize(string name, NameValueCollection config)
        {
            base.Initialize(string.IsNullOrEmpty(name) ? nameof(AppDataSettingsProvider) : name, config);
        }

        public override SettingsPropertyValueCollection GetPropertyValues(SettingsContext context, SettingsPropertyCollection collection)
        {
            EnsureMigrated();

            var stored = ReadStoredSettings();
            var values = new SettingsPropertyValueCollection();
            foreach (SettingsProperty property in collection)
            {
                var value = new SettingsPropertyValue(property);
                if (stored.TryGetValue(property.Name, out string serialized))
                {
                    value.SerializedValue = serialized;
                }
                else
                {
                    value.SerializedValue = property.DefaultValue;
                }
                value.Deserialized = false;
                values.Add(value);
            }

            return values;
        }

        public override void SetPropertyValues(SettingsContext context, SettingsPropertyValueCollection collection)
        {
            string groupName = GroupName(context);
            var settingsElement = new XElement(groupName);
            foreach (SettingsPropertyValue propertyValue in collection)
            {
                settingsElement.Add(ToSettingElement(propertyValue));
            }

            var document = new XDocument(
                new XDeclaration("1.0", "utf-8", null),
                new XElement("configuration",
                    new XElement("configSections",
                        new XElement("sectionGroup",
                            new XAttribute("name", "userSettings"),
                            new XAttribute("type", SectionGroupType),
                            new XElement("section",
                                new XAttribute("name", groupName),
                                new XAttribute("type", SectionType),
                                new XAttribute("allowExeDefinition", "MachineToLocalUser"),
                                new XAttribute("requirePermission", "false")))),
                    new XElement("userSettings", settingsElement)));

            string directory = Path.GetDirectoryName(mPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            document.Save(mPath);
        }

        public SettingsPropertyValue GetPreviousVersion(SettingsContext context, SettingsProperty property)
        {
            return new SettingsPropertyValue(property)
            {
                SerializedValue = property.DefaultValue,
                Deserialized = false,
                IsDirty = false
            };
        }

        public void Reset(SettingsContext context)
        {
            try
            {
                if (File.Exists(mPath))
                {
                    File.Delete(mPath);
                }
            }
            catch
            {
            }
        }

        public void Upgrade(SettingsContext context, SettingsPropertyCollection properties)
        {
            // One stable file; version-folder copy is not used. Legacy
            // LocalAppData files are copied in GetPropertyValues.
        }

        void EnsureMigrated()
        {
            if (mMigrated)
            {
                return;
            }

            mMigrated = true;
            try
            {
                string sourcePath;
                if (!UserConfigLocator.TryMigrate(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    mPath,
                    out sourcePath))
                {
                    return;
                }

                MigratedFromPath = sourcePath;
                string xml = File.ReadAllText(mPath);
                string remapped = UserConfigLocator.RemapNefMotoInstallPaths(
                    xml, Directory.Exists(UserConfigLocator.X64InstallPrefix));
                if (!string.Equals(xml, remapped, StringComparison.Ordinal))
                {
                    File.WriteAllText(mPath, remapped);
                }
            }
            catch
            {
            }
        }

        Dictionary<string, string> ReadStoredSettings()
        {
            var stored = new Dictionary<string, string>(StringComparer.Ordinal);
            if (!File.Exists(mPath))
            {
                return stored;
            }

            XDocument document;
            try
            {
                document = XDocument.Load(mPath);
            }
            catch
            {
                return stored;
            }

            foreach (XElement setting in document.Descendants("setting"))
            {
                string name = (string)setting.Attribute("name");
                if (string.IsNullOrEmpty(name) || stored.ContainsKey(name))
                {
                    continue;
                }

                stored[name] = ReadSettingValue(setting);
            }

            return stored;
        }

        static string GroupName(SettingsContext context)
        {
            if (context != null && context["GroupName"] is string groupName && !string.IsNullOrEmpty(groupName))
            {
                return groupName;
            }

            return DefaultGroupName;
        }

        static XElement ToSettingElement(SettingsPropertyValue propertyValue)
        {
            var valueElement = new XElement("value");
            string serialized = propertyValue.SerializedValue as string ?? Convert.ToString(propertyValue.SerializedValue) ?? "";
            if (propertyValue.Property.SerializeAs == SettingsSerializeAs.Xml && !string.IsNullOrWhiteSpace(serialized))
            {
                try
                {
                    valueElement.Add(XElement.Parse(serialized));
                }
                catch
                {
                    valueElement.Value = serialized;
                }
            }
            else
            {
                valueElement.Value = serialized;
            }

            return new XElement("setting",
                new XAttribute("name", propertyValue.Property.Name),
                new XAttribute("serializeAs", propertyValue.Property.SerializeAs.ToString()),
                valueElement);
        }

        static string ReadSettingValue(XElement setting)
        {
            XElement value = setting.Element("value");
            if (value == null)
            {
                return "";
            }

            string serializeAs = (string)setting.Attribute("serializeAs") ?? "String";
            if (string.Equals(serializeAs, "Xml", StringComparison.OrdinalIgnoreCase))
            {
                return string.Concat(value.Nodes().Select(node => node.ToString()));
            }

            return value.Value ?? "";
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
