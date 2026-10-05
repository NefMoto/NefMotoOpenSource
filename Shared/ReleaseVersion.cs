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
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Shared
{
    /// <summary>
    /// Version from a release tag or git describe output, e.g. v1.9.4.4,
    /// v1.9.4.4-rc2, v1.9.4.4-3-gab12-dirty. Commits-ahead and dirty
    /// suffixes are ignored, so a dev build compares equal to its base tag.
    /// </summary>
    public sealed class ReleaseVersion : IComparable<ReleaseVersion>
    {
        static readonly Regex DirtySuffix = new Regex(@"-dirty$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex DescribeSuffix = new Regex(@"-\d+-g[0-9a-f]+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex VersionPattern = new Regex(@"^[vV]?(\d+(?:\.\d+){1,3})(?:-([A-Za-z][A-Za-z0-9]*))?$", RegexOptions.CultureInvariant);
        static readonly Regex LabelPattern = new Regex(@"^([A-Za-z]*)(\d*)$", RegexOptions.CultureInvariant);

        public IReadOnlyList<int> Parts { get; }
        public string Label { get; }
        public bool IsPrerelease => Label != null;

        ReleaseVersion(int[] parts, string label)
        {
            Parts = parts;
            Label = label;
        }

        public static bool TryParse(string text, out ReleaseVersion version)
        {
            version = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            var trimmed = DirtySuffix.Replace(text.Trim(), "");
            trimmed = DescribeSuffix.Replace(trimmed, "");

            var match = VersionPattern.Match(trimmed);
            if (!match.Success)
            {
                return false;
            }

            var parts = new List<int>();
            foreach (var part in match.Groups[1].Value.Split('.'))
            {
                if (!int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out int value))
                {
                    return false;
                }
                parts.Add(value);
            }

            var label = match.Groups[2].Success ? match.Groups[2].Value : null;
            version = new ReleaseVersion(parts.ToArray(), label);
            return true;
        }

        /// <summary>
        /// Next stable version: last numeric part plus one, label dropped.
        /// </summary>
        public ReleaseVersion Bump()
        {
            var parts = Parts.ToArray();
            parts[parts.Length - 1]++;
            return new ReleaseVersion(parts, null);
        }

        public int CompareTo(ReleaseVersion other)
        {
            if (other == null)
            {
                return 1;
            }

            int count = Math.Max(Parts.Count, other.Parts.Count);
            for (int i = 0; i < count; i++)
            {
                int a = i < Parts.Count ? Parts[i] : 0;
                int b = i < other.Parts.Count ? other.Parts[i] : 0;
                if (a != b)
                {
                    return a.CompareTo(b);
                }
            }

            if (!IsPrerelease || !other.IsPrerelease)
            {
                return other.IsPrerelease.CompareTo(IsPrerelease);
            }

            var la = LabelPattern.Match(Label);
            var lb = LabelPattern.Match(other.Label);
            if (!la.Success || !lb.Success)
            {
                return string.Compare(Label, other.Label, StringComparison.OrdinalIgnoreCase);
            }

            int byName = string.Compare(la.Groups[1].Value, lb.Groups[1].Value, StringComparison.OrdinalIgnoreCase);
            if (byName != 0)
            {
                return byName;
            }

            long na = la.Groups[2].Value.Length > 0 ? long.Parse(la.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
            long nb = lb.Groups[2].Value.Length > 0 ? long.Parse(lb.Groups[2].Value, CultureInfo.InvariantCulture) : 0;
            return na.CompareTo(nb);
        }

        public override string ToString()
        {
            var text = "v" + string.Join(".", Parts);
            return Label != null ? text + "-" + Label : text;
        }
    }

    public sealed class ReleaseInfo
    {
        public string TagName { get; set; }
        public string HtmlUrl { get; set; }
        public bool Prerelease { get; set; }
        public bool Draft { get; set; }
        public string Body { get; set; }
        public string InstallerUrl { get; set; }
    }

    public static class UpdateCheck
    {
        /// <summary>
        /// Highest release strictly newer than current. Pre-releases count
        /// only when current is itself a pre-release. Null when up to date.
        /// </summary>
        public static ReleaseInfo SelectNewer(ReleaseVersion current, IEnumerable<ReleaseInfo> releases)
        {
            return SelectAllNewer(current, releases).FirstOrDefault();
        }

        /// <summary>
        /// Every eligible release strictly newer than current, newest first.
        /// </summary>
        public static List<ReleaseInfo> SelectAllNewer(ReleaseVersion current, IEnumerable<ReleaseInfo> releases)
        {
            var newer = new List<(ReleaseVersion Version, ReleaseInfo Release)>();
            if (current == null || releases == null)
            {
                return new List<ReleaseInfo>();
            }

            foreach (var release in releases)
            {
                if (release == null || release.Draft)
                {
                    continue;
                }

                if (!ReleaseVersion.TryParse(release.TagName, out var version))
                {
                    continue;
                }

                if ((release.Prerelease || version.IsPrerelease) && !current.IsPrerelease)
                {
                    continue;
                }

                if (version.CompareTo(current) > 0)
                {
                    newer.Add((version, release));
                }
            }

            return newer
                .OrderByDescending(entry => entry.Version)
                .Select(entry => entry.Release)
                .ToList();
        }
    }

    /// <summary>
    /// GitHub release-note Markdown flattened for a plain TextBox.
    /// </summary>
    public static class ReleaseNotesText
    {
        static readonly Regex HtmlComment = new Regex(@"<!--.*?-->", RegexOptions.Singleline | RegexOptions.CultureInvariant);
        static readonly Regex Heading = new Regex(@"^[ \t]{0,3}#{1,6}[ \t]+", RegexOptions.Multiline | RegexOptions.CultureInvariant);
        static readonly Regex Bullet = new Regex(@"^([ \t]*)[*+-][ \t]+", RegexOptions.Multiline | RegexOptions.CultureInvariant);
        static readonly Regex Link = new Regex(@"\[([^\]]*)\]\(([^)\s]+)\)", RegexOptions.CultureInvariant);
        static readonly Regex Bold = new Regex(@"(\*\*|__)(.+?)\1", RegexOptions.CultureInvariant);
        static readonly Regex Code = new Regex(@"`([^`]*)`", RegexOptions.CultureInvariant);
        static readonly Regex ExtraBlankLines = new Regex(@"\n{3,}", RegexOptions.CultureInvariant);

        public static string ToPlainText(string markdown)
        {
            if (string.IsNullOrWhiteSpace(markdown))
            {
                return "";
            }

            var text = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
            text = HtmlComment.Replace(text, "");
            text = Heading.Replace(text, "");
            text = Bullet.Replace(text, "$1- ");
            text = Link.Replace(text, match =>
            {
                var label = match.Groups[1].Value;
                var url = match.Groups[2].Value;
                return (label.Length == 0 || label == url) ? url : label + " (" + url + ")";
            });
            text = Bold.Replace(text, "$2");
            text = Code.Replace(text, "$1");
            text = ExtraBlankLines.Replace(text, "\n\n");
            return text.Trim();
        }

        /// <summary>
        /// Notes for each release, newest first, separated by a tag line.
        /// </summary>
        public static string Combine(IEnumerable<ReleaseInfo> releases)
        {
            var sections = new List<string>();
            foreach (var release in releases ?? Enumerable.Empty<ReleaseInfo>())
            {
                var body = ToPlainText(release?.Body);
                sections.Add("=== " + release?.TagName + " ===\n\n" + (body.Length > 0 ? body : "(no release notes)"));
            }

            return string.Join("\n\n", sections);
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
