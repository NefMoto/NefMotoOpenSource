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

using System.Linq;
using Shared;
using Xunit;

namespace NefMotoOpenSource.Tests;

public sealed class ReleaseVersionTests
{
    static ReleaseVersion V(string text)
    {
        Assert.True(ReleaseVersion.TryParse(text, out var version), "failed to parse " + text);
        return version;
    }

    static ReleaseInfo R(string tag, bool prerelease = false, bool draft = false)
    {
        return new ReleaseInfo
        {
            TagName = tag,
            HtmlUrl = "https://example.invalid/" + tag,
            Prerelease = prerelease,
            Draft = draft,
        };
    }

    [Theory]
    [InlineData("v1.9.4.4", "v1.9.4.4", false)]
    [InlineData("1.9.4.4", "v1.9.4.4", false)]
    [InlineData("v1.9.4.4-rc2", "v1.9.4.4-rc2", true)]
    [InlineData("v1.9.4.4-3-gab12", "v1.9.4.4", false)]
    [InlineData("v1.9.4.4-3-gab12-dirty", "v1.9.4.4", false)]
    [InlineData("v1.9.4.4-dirty", "v1.9.4.4", false)]
    [InlineData("v1.9.4.4-rc2-3-gab12-dirty", "v1.9.4.4-rc2", true)]
    [InlineData("v1.2", "v1.2", false)]
    public void TryParse_accepts_tags_and_describe_output(string text, string expected, bool prerelease)
    {
        var version = V(text);
        Assert.Equal(expected, version.ToString());
        Assert.Equal(prerelease, version.IsPrerelease);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ab12")]
    [InlineData("ab12-dirty")]
    [InlineData("1234")]
    [InlineData("AssemblyInformationalVersion is not set")]
    public void TryParse_rejects_hashes_and_junk(string? text)
    {
        Assert.False(ReleaseVersion.TryParse(text, out _));
    }

    [Fact]
    public void Ordering_stable_above_rc_and_rc_numbers_numeric()
    {
        Assert.True(V("v1.9.4.4").CompareTo(V("v1.9.4.4-rc2")) > 0);
        Assert.True(V("v1.9.4.4-rc2").CompareTo(V("v1.9.4.4-rc1")) > 0);
        Assert.True(V("v1.9.4.4-rc10").CompareTo(V("v1.9.4.4-rc2")) > 0);
        Assert.True(V("v1.9.4.5-rc1").CompareTo(V("v1.9.4.4")) > 0);
        Assert.True(V("v1.10.0.0").CompareTo(V("v1.9.4.4")) > 0);
        Assert.Equal(0, V("v1.9.4.4-3-gab12-dirty").CompareTo(V("v1.9.4.4")));
        Assert.Equal(0, V("v1.2").CompareTo(V("v1.2.0.0")));
    }

    [Theory]
    [InlineData("v1.9.4.4", "v1.9.4.5")]
    [InlineData("v1.9.4.4-rc2", "v1.9.4.5")]
    [InlineData("v1.9.4.4-3-gab12-dirty", "v1.9.4.5")]
    [InlineData("v1.2", "v1.3")]
    public void Bump_increments_last_part_and_is_newer(string text, string expected)
    {
        var current = V(text);
        var bumped = current.Bump();
        Assert.Equal(expected, bumped.ToString());
        Assert.True(bumped.CompareTo(current) > 0);
    }

    [Fact]
    public void SelectNewer_stable_build_ignores_prereleases()
    {
        var releases = new[] { R("v1.9.4.4"), R("v1.9.4.5-rc1", prerelease: true) };
        Assert.Null(UpdateCheck.SelectNewer(V("v1.9.4.4"), releases));

        releases = new[] { R("v1.9.4.4"), R("v1.9.4.5-rc1", prerelease: true), R("v1.9.4.5") };
        Assert.Equal("v1.9.4.5", UpdateCheck.SelectNewer(V("v1.9.4.4"), releases)?.TagName);
    }

    [Fact]
    public void SelectNewer_rc_build_sees_newer_rc_and_stable()
    {
        var releases = new[] { R("v1.9.4.4-rc1", prerelease: true), R("v1.9.4.4-rc2", prerelease: true) };
        Assert.Equal("v1.9.4.4-rc2", UpdateCheck.SelectNewer(V("v1.9.4.4-rc1"), releases)?.TagName);

        releases = new[] { R("v1.9.4.4-rc2", prerelease: true), R("v1.9.4.4") };
        Assert.Equal("v1.9.4.4", UpdateCheck.SelectNewer(V("v1.9.4.4-rc1"), releases)?.TagName);
    }

    [Fact]
    public void SelectNewer_ignores_drafts_and_unparseable_tags()
    {
        var releases = new[] { R("v2.0.0.0", draft: true), R("nightly"), R("v1.9.4.4") };
        Assert.Null(UpdateCheck.SelectNewer(V("v1.9.4.4"), releases));
    }

    [Fact]
    public void SelectAllNewer_returns_newer_releases_newest_first()
    {
        var releases = new[] { R("v1.9.4.5"), R("v1.9.4.3"), R("v1.9.4.7"), R("v1.9.4.6-rc1", prerelease: true), R("v1.9.4.6") };
        var newer = UpdateCheck.SelectAllNewer(V("v1.9.4.4"), releases);
        Assert.Equal(new[] { "v1.9.4.7", "v1.9.4.6", "v1.9.4.5" }, newer.Select(r => r.TagName));
    }

    [Fact]
    public void ReleaseNotes_flattens_git_cliff_markdown()
    {
        const string body =
            "## What's Changed\r\n\r\n" +
            "### Bug Fixes\r\n\r\n" +
            "* **kwp**: fix `0x11` connect by @nyet in [#120](https://github.com/x/y/pull/120)\r\n\r\n\r\n\r\n" +
            "<!-- generated -->\r\n" +
            "**Full Changelog**: https://github.com/x/y/compare/a...b";

        var text = ReleaseNotesText.ToPlainText(body);

        Assert.Equal(
            "What's Changed\n\n" +
            "Bug Fixes\n\n" +
            "- kwp: fix 0x11 connect by @nyet in #120 (https://github.com/x/y/pull/120)\n\n" +
            "Full Changelog: https://github.com/x/y/compare/a...b",
            text);
    }

    [Fact]
    public void ReleaseNotes_combine_labels_each_release()
    {
        var a = R("v1.9.4.6");
        a.Body = "- second";
        var b = R("v1.9.4.5");

        Assert.Equal("=== v1.9.4.6 ===\n\n- second\n\n=== v1.9.4.5 ===\n\n(no release notes)",
            ReleaseNotesText.Combine(new[] { a, b }));
    }

    [Fact]
    public void SelectNewer_returns_null_when_up_to_date()
    {
        var releases = new[] { R("v1.9.4.3"), R("v1.9.4.4") };
        Assert.Null(UpdateCheck.SelectNewer(V("v1.9.4.4-2-gab12"), releases));
        Assert.Null(UpdateCheck.SelectNewer(V("v1.9.4.4"), System.Array.Empty<ReleaseInfo>()));
    }
}

// vi: set sw=4 ts=8 expandtab:
