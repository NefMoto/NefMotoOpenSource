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

public sealed class SafeFileDialogDirectoryTests
{
    const string MissingFile = @"Q:\nefmoto-missing-folder-test\ecu.bin";
    const string MissingDir = @"Q:\nefmoto-missing-folder-test";

    [Fact]
    public void Exists_is_false_for_missing_and_empty_paths()
    {
        Assert.False(SafeFileDialogDirectory.Exists(null));
        Assert.False(SafeFileDialogDirectory.Exists(""));
        Assert.False(SafeFileDialogDirectory.Exists(MissingDir));
    }

    [Fact]
    public void ParentIfExists_returns_null_when_folder_is_gone()
    {
        Assert.Null(SafeFileDialogDirectory.ParentIfExists(null));
        Assert.Null(SafeFileDialogDirectory.ParentIfExists(""));
        Assert.Null(SafeFileDialogDirectory.ParentIfExists(MissingFile));
        Assert.Null(SafeFileDialogDirectory.ParentIfExists(@"C:\foo|bar\x.bin"));
    }

    [Fact]
    public void ParentIfExists_returns_existing_parent()
    {
        var dir = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var file = Path.Combine(dir, "ecu.bin");
        var parent = SafeFileDialogDirectory.ParentIfExists(file);
        Assert.NotNull(parent);
        Assert.Equal(Path.GetFullPath(dir), Path.GetFullPath(parent));
    }

    [Fact]
    public void Resolve_skips_missing_saved_folders_and_returns_an_existing_dir()
    {
        var resolved = SafeFileDialogDirectory.Resolve(MissingFile, MissingDir, MissingFile);
        Assert.True(Directory.Exists(resolved));
    }

    [Fact]
    public void Resolve_prefers_existing_preferred_file_folder()
    {
        var dir = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var file = Path.Combine(dir, "ecu.bin");
        var resolved = SafeFileDialogDirectory.Resolve(file, MissingDir, MissingFile);
        Assert.Equal(Path.GetFullPath(dir), Path.GetFullPath(resolved));
    }

    [Fact]
    public void Fallback_is_an_existing_directory()
    {
        Assert.True(Directory.Exists(SafeFileDialogDirectory.Fallback()));
    }
}

// vi: set sw=4 ts=8 expandtab:
