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
using Shared;
using Xunit;

namespace NefMotoOpenSource.Tests;

public sealed class UserConfigLocatorTests : IDisposable
{
    readonly string mRoot;

    public UserConfigLocatorTests()
    {
        mRoot = Path.Combine(Path.GetTempPath(), "NefMotoUserConfigTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(mRoot);
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(mRoot, true);
        }
        catch
        {
        }
    }

    [Fact]
    public void FindBest_PrefersRicherConfigOverNewerSparse()
    {
        string sparse = WriteLegacy("newhash", "1.9.7.0", Config(
            ("SettingsUpgraded", "True"),
            ("WindowState", "Maximized"),
            ("WindowWidth", "2582"),
            ("WindowHeight", "1422")), DateTime.UtcNow);
        string rich = WriteLegacy("oldhash", "1.9.7.0", Config(
            ("SettingsUpgraded", "True"),
            ("WindowState", "Normal"),
            ("WindowWidth", "1280"),
            ("WindowHeight", "800"),
            ("FlashFile", @"D:\home\nyet\chips\1M.bin"),
            ("MemoryLayoutFile", @"C:\Program Files (x86)\NefMotoECUFlasher\MemoryLayouts\ME7.MemoryLayout.xml"),
            ("DesiredKWP2000BaudRate", "124800")), DateTime.UtcNow.AddDays(-1));

        Assert.True(UserConfigLocator.Score(rich) > UserConfigLocator.Score(sparse));
        Assert.Equal(rich, UserConfigLocator.FindBest(mRoot));
    }

    [Fact]
    public void Score_IgnoresSettingsUpgradedAndDefaults()
    {
        string path = WriteLegacy("emptyhash", "1.9.7.0", Config(
            ("SettingsUpgraded", "True"),
            ("WindowState", "Maximized"),
            ("WindowWidth", "700"),
            ("WindowHeight", "600"),
            ("DesiredKWP2000ConnectionMethod", "SlowInit"),
            ("EnableSlowInitTimingLog", "False"),
            ("DesiredKWP2000BaudRate", "0"),
            ("FlashFile", "")), DateTime.UtcNow);

        Assert.Equal(0, UserConfigLocator.Score(path));
        Assert.Null(UserConfigLocator.FindBest(mRoot));
    }

    [Fact]
    public void TryMigrate_CopiesBestToStablePath()
    {
        string rich = WriteLegacy("oldhash", "1.9.6.2", Config(
            ("FlashFile", @"D:\chips\file.bin"),
            ("DesiredKWP2000BaudRate", "124800")), DateTime.UtcNow.AddDays(-2));
        WriteLegacy("newhash", "1.9.7.0", Config(
            ("WindowWidth", "2582")), DateTime.UtcNow);

        string stable = Path.Combine(mRoot, "roaming", "user.config");
        Assert.True(UserConfigLocator.TryMigrate(mRoot, stable, out string source));
        Assert.Equal(rich, source);
        Assert.True(File.Exists(stable));
        Assert.Contains(@"D:\chips\file.bin", File.ReadAllText(stable), StringComparison.Ordinal);
        Assert.False(UserConfigLocator.TryMigrate(mRoot, stable, out _));
    }

    [Fact]
    public void Remap_ReplacesProgramFilesX86WhenNewRootExists()
    {
        string xml = $@"<value>{UserConfigLocator.X86InstallPrefix}\MemoryLayouts\ME7.MemoryLayout.xml</value>";
        string remapped = UserConfigLocator.RemapNefMotoInstallPaths(xml, true);
        Assert.Contains(UserConfigLocator.X64InstallPrefix, remapped, StringComparison.Ordinal);
        Assert.DoesNotContain("Program Files (x86)", remapped, StringComparison.Ordinal);
        Assert.Equal(xml, UserConfigLocator.RemapNefMotoInstallPaths(xml, false));
    }

    [Fact]
    public void FindBest_IgnoresUnrelatedTopLevelFolders()
    {
        string other = Path.Combine(mRoot, "Microsoft", "OtherApp_Url_abc", "1.0.0.0");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, UserConfigLocator.FileName), Config(("FlashFile", @"C:\other.bin")));

        Assert.Null(UserConfigLocator.FindBest(mRoot));
    }

    string WriteLegacy(string hash, string version, string xml, DateTime utc)
    {
        string directory = Path.Combine(mRoot, "NefMotoECUFlasher", "NefMotoECUFlasher_Url_" + hash, version);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, UserConfigLocator.FileName);
        File.WriteAllText(path, xml);
        File.SetLastWriteTimeUtc(path, utc);
        return path;
    }

    static string Config(params (string Name, string Value)[] settings)
    {
        var inner = new System.Text.StringBuilder();
        foreach ((string name, string value) in settings)
        {
            inner.Append("            <setting name=\"").Append(name).Append("\" serializeAs=\"String\"><value>");
            inner.Append(System.Security.SecurityElement.Escape(value));
            inner.Append("</value></setting>").AppendLine();
        }

        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine
            + "<configuration>" + Environment.NewLine
            + "    <userSettings>" + Environment.NewLine
            + "        <ECUFlasher.Properties.Settings>" + Environment.NewLine
            + inner
            + "        </ECUFlasher.Properties.Settings>" + Environment.NewLine
            + "    </userSettings>" + Environment.NewLine
            + "</configuration>" + Environment.NewLine;
    }
}

// vi: set sw=4 ts=8 expandtab:
