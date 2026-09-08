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

using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using Shared;
using Xunit;

namespace NefMotoOpenSource.Tests;

public sealed class MemoryLayoutBootTests
{
    private const uint Size8K = 8192;
    private const uint Size16K = 16384;
    private const uint Size32K = 32768;
    private const uint Size64K = 65536;

    public static List<uint> F800BT()
    {
        return Concat(Repeat(Size64K, 15), new uint[] { Size32K, Size8K, Size8K, Size16K });
    }

    public static List<uint> F800BB()
    {
        return Concat(new uint[] { Size16K, Size8K, Size8K, Size32K }, Repeat(Size64K, 15));
    }

    public static List<uint> F400BB()
    {
        return Concat(new uint[] { Size16K, Size8K, Size8K, Size32K }, Repeat(Size64K, 7));
    }

    [Fact]
    public void IsBootClusterSector_BT_OnlyLastFour()
    {
        var layout = BootLayout(F800BT(), 1024 * 1024, FlashBootOrientation.TopBoot, 4);
        Assert.False(layout.IsBootClusterSector(0));
        Assert.False(layout.IsBootClusterSector(14));
        Assert.True(layout.IsBootClusterSector(15));
        Assert.True(layout.IsBootClusterSector(18));
        Assert.False(layout.IsBootClusterSector(-1));
        Assert.False(layout.IsBootClusterSector(19));
    }

    [Fact]
    public void IsBootClusterSector_BB_OnlyFirstFour()
    {
        var layout = BootLayout(F800BB(), 1024 * 1024, FlashBootOrientation.BottomBoot, 4);
        Assert.True(layout.IsBootClusterSector(0));
        Assert.True(layout.IsBootClusterSector(3));
        Assert.False(layout.IsBootClusterSector(4));
        Assert.False(layout.IsBootClusterSector(18));
    }

    [Fact]
    public void IsBootClusterSector_Uniform64K_Never()
    {
        var layout = new MemoryLayout(0x800000, Size64K * 16, Repeat(Size64K, 16));
        Assert.True(layout.Validate(), layout.Error);
        Assert.False(layout.IsBootClusterSector(0));
        Assert.False(layout.IsBootClusterSector(15));
    }

    [Fact]
    public void IsBootClusterSector_F400BT_UsesClusterCount()
    {
        var sizes = Concat(Repeat(Size64K, 7), new uint[] { Size32K, Size8K, Size8K, Size16K });
        var layout = BootLayout(sizes, 512 * 1024, FlashBootOrientation.TopBoot, 4);
        Assert.True(layout.IsBootClusterSector(7));
        Assert.False(layout.IsBootClusterSector(6));
    }

    [Fact]
    public void Validate_BootOrientationWithoutClusterCount_Fails()
    {
        var layout = new MemoryLayout(0x800000, 1024 * 1024, F800BT());
        layout.BootOrientation = FlashBootOrientation.TopBoot;
        Assert.False(layout.Validate());
    }

    [Fact]
    public void ShippedLayoutXml_HasBootMetadata()
    {
        string dir = MemoryLayout.GetLayoutsDirectory();
        Assert.False(string.IsNullOrEmpty(dir));

        var bt = DeserializeLayout(dir, "ME7 29F800BT");
        Assert.Equal(FlashBootOrientation.TopBoot, bt.BootOrientation);
        Assert.Equal(4, bt.BootClusterSectors);
        Assert.Equal("ME7 29F800BB", bt.OppositeLayout);
        Assert.True(bt.IsBootClusterSector(15));
        Assert.False(bt.IsBootClusterSector(14));

        var bb = DeserializeLayout(dir, "ME7 29F800BB");
        Assert.Equal(FlashBootOrientation.BottomBoot, bb.BootOrientation);
        Assert.Equal("ME7 29F800BT", bb.OppositeLayout);
        Assert.True(bb.IsBootClusterSector(0));
        Assert.False(bb.IsBootClusterSector(4));

        var f400 = DeserializeLayout(dir, "ME7 29F400BB");
        Assert.Equal(FlashBootOrientation.BottomBoot, f400.BootOrientation);
        Assert.True(string.IsNullOrEmpty(f400.OppositeLayout));
        Assert.True(f400.IsBootClusterSector(3));
    }

    private static MemoryLayout BootLayout(IList<uint> sizes, uint size, FlashBootOrientation orientation, int clusterSectors)
    {
        var layout = new MemoryLayout(0x800000, size, new List<uint>(sizes));
        layout.BootOrientation = orientation;
        layout.BootClusterSectors = clusterSectors;
        Assert.True(layout.Validate(), layout.Error);
        return layout;
    }

    private static MemoryLayout DeserializeLayout(string directory, string basename)
    {
        string path = Path.Combine(directory, basename + MemoryLayout.MEMORY_LAYOUT_FILE_EXT);
        using (var stream = File.OpenRead(path))
        {
            var layout = Assert.IsType<MemoryLayout>(new XmlSerializer(typeof(MemoryLayout)).Deserialize(stream));
            Assert.True(layout.Validate(), layout.Error);
            return layout;
        }
    }

    private static List<uint> Repeat(uint size, int count)
    {
        var list = new List<uint>(count);
        for (int i = 0; i < count; i++)
        {
            list.Add(size);
        }
        return list;
    }

    private static List<uint> Concat(IList<uint> a, IList<uint> b)
    {
        var list = new List<uint>(a.Count + b.Count);
        list.AddRange(a);
        list.AddRange(b);
        return list;
    }
}

// vi: set sw=4 ts=8 expandtab:
