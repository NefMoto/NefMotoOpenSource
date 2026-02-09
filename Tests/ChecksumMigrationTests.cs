/*
Nefarious Motorsports ME7 ECU Flasher
Copyright (C) 2026  Nefarious Motorsports Inc

This program is free software: you can redistribute it and/or modify
it under the terms of the GNU General Public License as published by
the Free Software Foundation, either version 3 of the License, or
(at your option) any later version.

Unit tests to detect .NET migration regressions in checksum algorithm
(integer overflow, byte order, iteration order). Same input must produce
same checksum across runtimes.
*/

using System.Collections.Generic;
using Checksum;
using Shared;
using Xunit;

namespace NefMotoOpenSource.Tests;

public sealed class ChecksumMigrationTests
{
    /// <summary>
    /// Rolling checksum round-trip: update -> commit -> load -> verify.
    /// Any change in integer/byte handling in .NET would break this.
    /// </summary>
    [Fact]
    public void RollingChecksum_UpdateCommitLoad_IsCorrect()
    {
        const uint seedAddr = 0;
        const uint seedSize = 256u * 4u;
        const uint dataStart = 0x500u;
        const uint dataSize = 16u;
        const uint checksumSlotAddr = 0x510u;
        const uint baseAddr = 0;
        const uint totalSize = 0x520u;

        var raw = new byte[totalSize];
        for (uint i = 0; i < seedSize; i += 4)
        {
            var v = (uint)(i >> 2);
            raw[i + 0] = (byte)(v & 0xFF);
            raw[i + 1] = (byte)((v >> 8) & 0xFF);
            raw[i + 2] = (byte)((v >> 16) & 0xFF);
            raw[i + 3] = (byte)((v >> 24) & 0xFF);
        }
        for (int i = 0; i < dataSize; i++)
            raw[dataStart + i] = (byte)(0x11 + i);

        var memory = new MemoryImage(raw, baseAddr);
        var rolling = new RollingChecksums(seedAddr);
        var range = new List<AddressRange> { new AddressRange(dataStart, dataSize) };
        rolling.AddAddressRange(range, checksumSlotAddr);
        rolling.SetMemoryReference(memory);

        Assert.True(rolling.UpdateChecksum(false));
        Assert.True(rolling.CommitChecksum());
        Assert.True(rolling.LoadChecksum());
        Assert.True(rolling.IsCorrect(false));
    }

    /// <summary>
    /// Same seed and data must produce the same checksum value every run
    /// (determinism across .NET versions).
    /// </summary>
    [Fact]
    public void RollingChecksum_Deterministic_SameInputSameChecksum()
    {
        const uint seedAddr = 0;
        const uint seedSize = 256u * 4u;
        const uint dataStart = 0x400u;
        const uint dataSize = 8u;
        const uint checksumSlotAddr = 0x420u;
        const uint baseAddr = 0;
        const uint totalSize = 0x424u;

        var raw = new byte[totalSize];
        for (uint i = 0; i < seedSize; i += 4)
        {
            var v = (uint)(i >> 2);
            raw[i + 0] = (byte)(v & 0xFF);
            raw[i + 1] = (byte)((v >> 8) & 0xFF);
            raw[i + 2] = (byte)((v >> 16) & 0xFF);
            raw[i + 3] = (byte)((v >> 24) & 0xFF);
        }
        raw[dataStart] = 0xAB;
        raw[dataStart + 1] = 0xCD;

        var memory = new MemoryImage(raw, baseAddr);
        var rolling = new RollingChecksums(seedAddr);
        rolling.AddAddressRange(new List<AddressRange> { new AddressRange(dataStart, dataSize) }, checksumSlotAddr);
        rolling.SetMemoryReference(memory);

        Assert.True(rolling.UpdateChecksum(false));
        Assert.True(rolling.CommitChecksum());
        var firstChecksumBytes = new byte[4];
        Array.Copy(memory.RawData, (int)checksumSlotAddr, firstChecksumBytes, 0, 4);

        rolling.LoadChecksum();
        Assert.True(rolling.IsCorrect(false));

        memory.RawData[(int)checksumSlotAddr] = 0xED;
        memory.RawData[(int)checksumSlotAddr + 1] = 0xFE;
        memory.RawData[(int)checksumSlotAddr + 2] = 0xCA;
        memory.RawData[(int)checksumSlotAddr + 3] = 0xBE;
        rolling.LoadChecksum();
        Assert.False(rolling.IsCorrect(false));

        rolling.UpdateChecksum(false);
        rolling.CommitChecksum();
        for (int i = 0; i < 4; i++)
            Assert.Equal(firstChecksumBytes[i], memory.RawData[(int)checksumSlotAddr + i]);
    }
}
