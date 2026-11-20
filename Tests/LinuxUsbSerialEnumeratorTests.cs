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
using System.Diagnostics;
using System.IO;
using Communication;
using Xunit;

namespace NefMotoOpenSource.Tests;

public sealed class LinuxUsbSerialEnumeratorTests
{
    [Fact]
    public void SysfsTree_MapsFtdiAndCh340AndSkipsTtyS()
    {
        string root = Path.Combine(Path.GetTempPath(), "nm-sysfs-" + Guid.NewGuid().ToString("N"));
        try
        {
            WriteUsb(root, "1-1", "ttyUSB0", "0403", "FT232R USB UART\n", "A1\n");
            WriteUsb(root, "1-2", "ttyUSB1", "1a86", "USB Serial\n", "SER\n");
            WritePlain(root, "ttyS0");

            var byPath = new Dictionary<string, DeviceInfo>();
            foreach (UsbSerialPort port in LinuxUsbSerialEnumerator.Query(root))
            {
                DeviceInfo info = UsbSerialDeviceMapping.TryCreate(
                    port.VendorId, port.PortPath, port.ProductName, port.SerialNumber);
                Assert.NotNull(info);
                byPath.Add(port.PortPath, info);
            }

            Assert.Equal(2, byPath.Count);

            SerialPortDeviceInfo ftdi = Assert.IsType<SerialPortDeviceInfo>(byPath["/dev/ttyUSB0"]);
            Assert.Equal(DeviceType.FTDI, ftdi.Type);
            Assert.Equal("FT232R USB UART", ftdi.Description);
            Assert.Equal("A1", ftdi.SerialNumber);

            SerialPortDeviceInfo ch340 = Assert.IsType<SerialPortDeviceInfo>(byPath["/dev/ttyUSB1"]);
            Assert.Equal(DeviceType.CH340, ch340.Type);
            Assert.Equal("USB Serial", ch340.Description);
            Assert.Equal("SER", ch340.SerialNumber);
            Assert.Equal("/dev/ttyUSB1_USB Serial", ch340.DeviceID);
        }
        finally
        {
            RemoveLink(Path.Combine(root, "class", "tty", "ttyUSB0", "device"));
            RemoveLink(Path.Combine(root, "class", "tty", "ttyUSB1", "device"));
            RemoveLink(Path.Combine(root, "class", "tty", "ttyS0", "device"));
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }

    private static void WriteUsb(string root, string usbName, string ttyName, string vendor, string product, string serial)
    {
        // Real sysfs uses "1-1:1.0". ':' is illegal in a Windows path, and the walk only needs a child of the USB device.
        string ifaceName = usbName + ".1.0";
        string iface = Path.Combine(root, "devices", "usb", usbName, ifaceName);
        Directory.CreateDirectory(iface);
        string usb = Path.Combine(root, "devices", "usb", usbName);
        File.WriteAllText(Path.Combine(usb, "idVendor"), vendor + "\n");
        File.WriteAllText(Path.Combine(usb, "product"), product);
        File.WriteAllText(Path.Combine(usb, "serial"), serial);

        string tty = Path.Combine(root, "class", "tty", ttyName);
        Directory.CreateDirectory(tty);
        LinkDevice(Path.Combine(tty, "device"), Path.Combine("..", "..", "..", "devices", "usb", usbName, ifaceName));
    }

    private static void LinkDevice(string linkPath, string relativeTarget)
    {
        try
        {
            Directory.CreateSymbolicLink(linkPath, relativeTarget);
            return;
        }
        catch (IOException) when (OperatingSystem.IsWindows())
        {
        }

        string directory = Path.GetDirectoryName(linkPath) ?? throw new IOException("No directory for " + linkPath);
        string absolute = Path.GetFullPath(Path.Combine(directory, relativeTarget));
        using Process process = Process.Start(new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = "/c mklink /J \"" + linkPath + "\" \"" + absolute + "\"",
            UseShellExecute = false,
            CreateNoWindow = true,
        }) ?? throw new IOException("Could not start cmd");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new IOException("Could not link " + linkPath);
        }
    }

    private static void RemoveLink(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path);
        }
    }

    private static void WritePlain(string root, string ttyName)
    {
        string plain = Path.Combine(root, "devices", "platform", "serial");
        Directory.CreateDirectory(plain);
        File.WriteAllText(Path.Combine(plain, "idVendor"), "0403\n");
        string tty = Path.Combine(root, "class", "tty", ttyName);
        Directory.CreateDirectory(tty);
        LinkDevice(Path.Combine(tty, "device"), Path.Combine("..", "..", "..", "devices", "platform", "serial"));
    }
}
