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
using Xunit;

namespace NefMotoOpenSource.Tests;

public sealed class LegacyUserConfigTests
{
    const string RichXml = """
        <configuration>
          <userSettings>
            <ECUFlasher.Properties.Settings>
              <setting name="WindowState" serializeAs="String"><value>Normal</value></setting>
              <setting name="WindowTop" serializeAs="String"><value>98</value></setting>
              <setting name="WindowLeft" serializeAs="String"><value>221</value></setting>
              <setting name="WindowWidth" serializeAs="String"><value>1475</value></setting>
              <setting name="WindowHeight" serializeAs="String"><value>1044</value></setting>
              <setting name="FlashFile" serializeAs="String"><value>D:\bins\ecu.bin</value></setting>
              <setting name="MemoryLayoutFile" serializeAs="String"><value>C:\Program Files (x86)\NefMotoECUFlasher\MemoryLayouts\ME7 29F800BB.MemoryLayout.xml</value></setting>
              <setting name="EnableSlowInitTimingLog" serializeAs="String"><value>True</value></setting>
              <setting name="DesiredKWP2000BaudRate" serializeAs="String"><value>124800</value></setting>
              <setting name="DesiredBootModeBaudRate" serializeAs="String"><value>124800</value></setting>
              <setting name="FTDIUSBDevice" serializeAs="Xml"><value>
                <FTDIDeviceInfo>
                  <Description>K+DCAN</Description>
                  <Flags>0</Flags>
                  <ID>67330049</ID>
                  <LocId>5649</LocId>
                  <SerialNumber>A6000001</SerialNumber>
                  <Type>FT_DEVICE_232R</Type>
                  <ChipID>0</ChipID>
                </FTDIDeviceInfo>
              </value></setting>
              <setting name="DesiredKWP2000ConnectionMethod" serializeAs="String"><value>FastInit</value></setting>
            </ECUFlasher.Properties.Settings>
          </userSettings>
        </configuration>
        """;

    const string SparseXml = """
        <configuration>
          <userSettings>
            <ECUFlasher.Properties.Settings>
              <setting name="WindowState" serializeAs="String"><value>Maximized</value></setting>
              <setting name="WindowWidth" serializeAs="String"><value>700</value></setting>
              <setting name="WindowHeight" serializeAs="String"><value>600</value></setting>
              <setting name="SettingsUpgraded" serializeAs="String"><value>True</value></setting>
            </ECUFlasher.Properties.Settings>
          </userSettings>
        </configuration>
        """;

    [Fact]
    public void Parse_CopiesWindowFilesAndTimingLog_IgnoresConnectMethod()
    {
        var parsed = LegacyUserConfig.Parse(RichXml);
        Assert.Equal("Normal", parsed.WindowState);
        Assert.Equal(1475, parsed.WindowWidth);
        Assert.Equal(@"D:\bins\ecu.bin", parsed.FlashFile);
        Assert.True(parsed.EnableSlowInitTimingLog);
        Assert.Equal(124800u, parsed.DesiredKWP2000BaudRate);
        Assert.Equal(124800u, parsed.DesiredBootModeBaudRate);
        Assert.Equal("A6000001", parsed.FtdiUsbDevice.SerialNumber);
        Assert.Equal(7, parsed.Score());
    }

    [Fact]
    public void Parse_SparseDefaults_ScoreZero()
    {
        var parsed = LegacyUserConfig.Parse(SparseXml);
        Assert.False(parsed.HasNonDefaultWindow());
        Assert.Equal(0, parsed.Score());
    }

    [Fact]
    public void TryFindBest_PrefersRicherFile_NotNewestSparse()
    {
        var root = Path.Combine(Path.GetTempPath(), "NefMotoLegacyConfigTests-" + Guid.NewGuid().ToString("N"));
        var local = Path.Combine(root, "Local");
        var richDir = Path.Combine(local, "NefMotoECUFlasher", "hash_old", "1.9.6.2");
        var sparseDir = Path.Combine(local, "NefMotoECUFlasher", "hash_new", "1.9.7.1");
        Directory.CreateDirectory(richDir);
        Directory.CreateDirectory(sparseDir);
        var richPath = Path.Combine(richDir, "user.config");
        var sparsePath = Path.Combine(sparseDir, "user.config");
        File.WriteAllText(richPath, RichXml);
        File.WriteAllText(sparsePath, SparseXml);
        File.SetLastWriteTimeUtc(richPath, DateTime.UtcNow.AddDays(-2));
        File.SetLastWriteTimeUtc(sparsePath, DateTime.UtcNow);

        try
        {
            Assert.True(LegacyUserConfig.TryFindBest(local, out var source, out var settings));
            Assert.Equal(richPath, source);
            Assert.Equal(@"D:\bins\ecu.bin", settings.FlashFile);
            Assert.True(settings.EnableSlowInitTimingLog);
            Assert.Equal("A6000001", settings.FtdiUsbDevice.SerialNumber);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RemapInstallPath_UsesProgramFilesWhenLayoutExists()
    {
        var x86 = LegacyUserConfig.X86InstallPrefix + @"\MemoryLayouts\ME7 29F800BB.MemoryLayout.xml";
        var x64 = LegacyUserConfig.X64InstallPrefix + @"\MemoryLayouts\ME7 29F800BB.MemoryLayout.xml";
        Assert.Equal(x64, LegacyUserConfig.RemapInstallPath(x86, path => path == x64));
        Assert.Equal(x86, LegacyUserConfig.RemapInstallPath(x86, _ => false));
    }
}

// vi: set sw=4 ts=8 expandtab:
