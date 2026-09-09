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

namespace Shared
{
    /// <summary>
    /// Folders for Win32 file dialogs. Missing drives, deleted dirs, and
    /// invalid leftover paths must not be assigned to InitialDirectory.
    /// </summary>
    public static class SafeFileDialogDirectory
    {
        public static bool Exists(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                return Directory.Exists(path);
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static string ParentIfExists(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
            {
                return null;
            }

            try
            {
                var dir = Path.GetDirectoryName(filePath);
                if (Exists(dir))
                {
                    return dir;
                }
            }
            catch (Exception)
            {
            }

            return null;
        }

        public static string Fallback()
        {
            try
            {
                var cwd = Directory.GetCurrentDirectory();
                if (Exists(cwd))
                {
                    return cwd;
                }
            }
            catch (Exception)
            {
            }

            foreach (var folder in new[]
            {
                Environment.SpecialFolder.MyDocuments,
                Environment.SpecialFolder.UserProfile,
                Environment.SpecialFolder.ApplicationData,
            })
            {
                try
                {
                    var path = Environment.GetFolderPath(folder);
                    if (Exists(path))
                    {
                        return path;
                    }
                }
                catch (Exception)
                {
                }
            }

            return ".";
        }

        public static string Resolve(string preferredFile, string lastBrowse, string flashFile)
        {
            var fromPreferred = ParentIfExists(preferredFile);
            if (fromPreferred != null)
            {
                return fromPreferred;
            }

            if (Exists(lastBrowse))
            {
                return lastBrowse;
            }

            var fromFlash = ParentIfExists(flashFile);
            if (fromFlash != null)
            {
                return fromFlash;
            }

            return Fallback();
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
