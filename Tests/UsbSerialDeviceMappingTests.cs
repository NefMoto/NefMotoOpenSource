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

using Communication;
using Xunit;

namespace NefMotoOpenSource.Tests;

public sealed class UsbSerialDeviceMappingTests
{
    [Theory]
    [InlineData(0x1234, "/dev/cu.usbserial-1")]
    [InlineData(0x0403, "")]
    [InlineData(0x0403, "/dev/tty.usbserial-A1")]
    [InlineData(0x1A86, "/dev/tty.wchusbserial1410")]
    public void SkippedPorts_ReturnNull(int vendorId, string portName)
    {
        Assert.Null(UsbSerialDeviceMapping.TryCreate(vendorId, portName, "X", "S"));
    }

    [Fact]
    public void NullPort_IsSkipped()
    {
        Assert.Null(UsbSerialDeviceMapping.TryCreate(UsbSerialDeviceMapping.Ch340VendorId, null, "X", "S"));
    }

    [Fact]
    public void FtdiCallout_BuildsSerialInfo()
    {
        DeviceInfo info = UsbSerialDeviceMapping.TryCreate(
            UsbSerialDeviceMapping.FtdiVendorId, "/dev/cu.usbserial-A1", "FT232R USB UART", "A1");

        SerialPortDeviceInfo ftdi = Assert.IsType<SerialPortDeviceInfo>(info);
        Assert.Equal(DeviceType.FTDI, ftdi.Type);
        Assert.Equal("/dev/cu.usbserial-A1", ftdi.PortName);
        Assert.Equal("/dev/cu.usbserial-A1", ftdi.DeviceID);
        Assert.Equal("A1", ftdi.SerialNumber);
        Assert.Equal("FT232R USB UART", ftdi.Description);
    }

    [Fact]
    public void FtdiCallout_DefaultsDescription()
    {
        DeviceInfo info = UsbSerialDeviceMapping.TryCreate(
            UsbSerialDeviceMapping.FtdiVendorId, "/dev/cu.usbserial-A1", null, null);

        Assert.Equal("FTDI Device (/dev/cu.usbserial-A1)", info.Description);
        Assert.Equal(string.Empty, info.SerialNumber);
    }

    [Fact]
    public void Ch340Callout_BuildsSerialInfo()
    {
        DeviceInfo info = UsbSerialDeviceMapping.TryCreate(
            UsbSerialDeviceMapping.Ch340VendorId, "/dev/cu.wchusbserial1410", "USB Serial", "SER");

        SerialPortDeviceInfo ch340 = Assert.IsType<SerialPortDeviceInfo>(info);
        Assert.Equal(DeviceType.CH340, ch340.Type);
        Assert.Equal("/dev/cu.wchusbserial1410", ch340.PortName);
        Assert.Equal("USB Serial", ch340.Description);
        Assert.Equal("SER", ch340.SerialNumber);
        Assert.Equal("/dev/cu.wchusbserial1410_USB Serial", ch340.DeviceID);
    }

    [Fact]
    public void WindowsComPort_IsKeptForCh340()
    {
        SerialPortDeviceInfo ch340 = Assert.IsType<SerialPortDeviceInfo>(UsbSerialDeviceMapping.TryCreate(
            UsbSerialDeviceMapping.Ch340VendorId, "COM5", null, ""));

        Assert.Equal(DeviceType.CH340, ch340.Type);
        Assert.Equal("COM5", ch340.PortName);
        Assert.Equal("CH340 Device (COM5)", ch340.Description);
        Assert.Equal("COM5_CH340 Device (COM5)", ch340.DeviceID);
        Assert.Equal(string.Empty, ch340.SerialNumber);
    }
}
