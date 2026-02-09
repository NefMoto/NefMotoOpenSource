/*
Nefarious Motorsports ME7 ECU Flasher
Copyright (C) 2026  Nefarious Motorsports Inc

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

Unit tests to detect .NET runtime migration regressions (e.g. byte order,
numeric formatting, culture behavior). Failures here suggest checking
release notes when upgrading .NET.
*/

using System;
using Shared;
using Xunit;

namespace NefMotoOpenSource.Tests;

public sealed class DataUtilsTests
{
    [Theory]
    [InlineData(Shared.DataUtils.DataType.Int8, 1u)]
    [InlineData(Shared.DataUtils.DataType.UInt8, 1u)]
    [InlineData(Shared.DataUtils.DataType.Int16, 2u)]
    [InlineData(Shared.DataUtils.DataType.UInt16, 2u)]
    [InlineData(Shared.DataUtils.DataType.Int32, 4u)]
    [InlineData(Shared.DataUtils.DataType.UInt32, 4u)]
    public void GetDataTypeSize_ReturnsExpectedSizes(Shared.DataUtils.DataType type, uint expectedSize)
    {
        Assert.Equal(expectedSize, Shared.DataUtils.GetDataTypeSize(type));
    }

    // Undefined is not supported (GetDataTypeSize hits Debug.Assert); skip testing to avoid assert in test runs.

    /// <summary>
    /// Round-trip write/read for each integer type. Catches endianness or
    /// unsafe layout changes across .NET versions.
    /// </summary>
    [Theory]
    [InlineData(Shared.DataUtils.DataType.UInt8, 0xABu)]
    [InlineData(Shared.DataUtils.DataType.UInt8, 0u)]
    [InlineData(Shared.DataUtils.DataType.UInt16, 0x1234u)]
    [InlineData(Shared.DataUtils.DataType.UInt32, 0xDEADBEEFu)]
    public void WriteReadRawInt_RoundTrip_PreservesValue(Shared.DataUtils.DataType type, uint value)
    {
        var size = Shared.DataUtils.GetDataTypeSize(type);
        var buffer = new byte[size + 4];
        Assert.True(Shared.DataUtils.WriteRawIntValueByType(value, type, buffer, 0));
        Assert.True(Shared.DataUtils.ReadRawIntValueByType(out var read, type, buffer, 0));
        Assert.Equal(value, read);
    }

    /// <summary>
    /// We rely on little-endian layout on Windows. This test would fail if
    /// .NET ever changed default byte order for the process.
    /// </summary>
    [Fact]
    public void RawIntLayout_IsLittleEndian_UInt16()
    {
        var buffer = new byte[4];
        Assert.True(Shared.DataUtils.WriteRawIntValueByType(0x1234, Shared.DataUtils.DataType.UInt16, buffer, 0));
        Assert.Equal(0x34, buffer[0]);
        Assert.Equal(0x12, buffer[1]);
    }

    [Fact]
    public void RawIntLayout_IsLittleEndian_UInt32()
    {
        var buffer = new byte[8];
        Assert.True(Shared.DataUtils.WriteRawIntValueByType(0xDEADBEEFu, Shared.DataUtils.DataType.UInt32, buffer, 0));
        Assert.Equal(0xEFu, buffer[0]);
        Assert.Equal(0xBEu, buffer[1]);
        Assert.Equal(0xADu, buffer[2]);
        Assert.Equal(0xDEu, buffer[3]);
    }

    [Fact]
    public void ClampedScale_ClampsToMinMax()
    {
        Assert.Equal(0f, Shared.DataUtils.ClampedScale(-1f, 1f, 0f, 5f));
        Assert.Equal(5f, Shared.DataUtils.ClampedScale(10f, 1f, 0f, 5f));
        Assert.Equal(3f, Shared.DataUtils.ClampedScale(0.3f, 10f, 0f, 5f));
    }

    [Fact]
    public void ClampedOffset_ClampsToMinMax()
    {
        Assert.Equal(0f, Shared.DataUtils.ClampedOffset(-1f, 0f, 0f, 5f));
        Assert.Equal(5f, Shared.DataUtils.ClampedOffset(10f, 0f, 0f, 5f));
        Assert.Equal(4f, Shared.DataUtils.ClampedOffset(3f, 1f, 0f, 5f));
    }

    /// <summary>
    /// Hex parsing can be sensitive to culture (e.g. digit shapes). We expect
    /// invariant-style hex to parse consistently across .NET versions.
    /// Implementation only treats as hex when string starts with "0x" / "0X".
    /// </summary>
    [Theory]
    [InlineData("0x1A", 0x1Au)]
    [InlineData("0X1A", 0x1Au)]
    [InlineData("0xdeadbeef", 0xDEADBEEFu)]
    [InlineData("0x0", 0u)]
    [InlineData("0x", 0u)]
    public void ReadHexString_ParsesExpectedValues(string hex, uint expected)
    {
        Assert.Equal(expected, Shared.DataUtils.ReadHexString(hex));
    }

    [Fact]
    public void ReadHexString_NullOrEmpty_ReturnsZero()
    {
        Assert.Equal(0u, Shared.DataUtils.ReadHexString(null));
        Assert.Equal(0u, Shared.DataUtils.ReadHexString(""));
    }

    [Fact]
    public void WriteHexString_FormatsConsistently()
    {
        Assert.Equal("0x1A", Shared.DataUtils.WriteHexString(0x1A));
        Assert.Equal("0xDEADBEEF", Shared.DataUtils.WriteHexString(0xDEADBEEFu));
    }

    /// <summary>
    /// Scale/offset math must remain deterministic (floating point behavior
    /// is specified but worth locking in for ECU calibration logic).
    /// </summary>
    [Fact]
    public void GetCorrectedValueFromRaw_And_GetRawValueFromCorrected_RoundTrip()
    {
        const double scale = 0.1;
        const double offset = -50.0;
        const double raw = 100.0;
        var corrected = Shared.DataUtils.GetCorrectedValueFromRaw(raw, scale, offset);
        Assert.Equal(100.0 * 0.1 - 50.0, corrected);
        var back = Shared.DataUtils.GetRawValueFromCorrected(corrected, scale, offset);
        Assert.Equal(raw, back);
    }
}
