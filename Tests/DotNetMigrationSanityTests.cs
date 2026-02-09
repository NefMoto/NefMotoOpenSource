/*
Nefarious Motorsports ME7 ECU Flasher
Copyright (C) 2026  Nefarious Motorsports Inc

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

Sanity checks for .NET runtime environment. Failures after a .NET upgrade
indicate possible breaking changes or unsupported configuration.
*/

using System;
using System.Runtime.InteropServices;
using Xunit;

namespace NefMotoOpenSource.Tests;

public sealed class DotNetMigrationSanityTests
{
    /// <summary>
    /// We rely on little-endian layout for binary I/O. A change would break
    /// DataUtils and checksum logic.
    /// </summary>
    [Fact]
    public void Process_IsLittleEndian()
    {
        Assert.True(BitConverter.IsLittleEndian);
    }

    /// <summary>
    /// Ensures we're running on a .NET version we expect (e.g. 8+). Update
    /// when bumping minimum supported runtime.
    /// </summary>
    [Fact]
    public void Runtime_IsAtLeastNet8()
    {
        var version = Environment.Version;
        Assert.True(version.Major >= 8,
            $"Expected .NET 8 or higher, got {version.Major}.{version.Minor}. Runtime: {RuntimeInformation.FrameworkDescription}");
    }
}
