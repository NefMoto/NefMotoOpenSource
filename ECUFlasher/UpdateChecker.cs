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
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Shared;

namespace ECUFlasher
{
    public enum UpdateCheckStatus
    {
        UpToDate,
        NewerAvailable,
        CurrentVersionUnparseable,
        Failed,
    }

    public sealed class UpdateCheckResult
    {
        public UpdateCheckStatus Status { get; init; }
        public ReleaseInfo Release { get; init; }
        public IReadOnlyList<ReleaseInfo> NewerReleases { get; init; } = Array.Empty<ReleaseInfo>();
        public string Error { get; init; }
        public bool Fake { get; init; }
    }

    /// <summary>
    /// Notify-only check against GitHub releases. Never downloads or installs.
    /// </summary>
    public static class UpdateChecker
    {
        public const string FakeUpdateVariable = "NEFMOTO_FAKE_UPDATE";
        public const string ReleasesApiUrl = "https://api.github.com/repos/NefMoto/NefMotoOpenSource/releases?per_page=30";
        public const string LatestReleaseUrl = "https://github.com/NefMoto/NefMotoOpenSource/releases/latest";

        static readonly HttpClient Http = CreateHttpClient();

        static HttpClient CreateHttpClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NefMotoECUFlasher", "1.0"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return client;
        }

        public static bool FakeUpdateRequested =>
            Environment.GetEnvironmentVariable(FakeUpdateVariable) == "1";

        public static ReleaseInfo MakeFakeRelease(string currentVersionText)
        {
            string tag = ReleaseVersion.TryParse(currentVersionText, out var current)
                ? current.Bump().ToString()
                : "v99.0.0.0";

            return new ReleaseInfo
            {
                TagName = tag,
                HtmlUrl = LatestReleaseUrl,
                Body = "## Fake release\n\n- Reported because " + FakeUpdateVariable + "=1 is set.\n- No release was fetched from GitHub.",
            };
        }

        public static async Task<UpdateCheckResult> CheckAsync(string currentVersionText)
        {
            if (FakeUpdateRequested)
            {
                var fake = MakeFakeRelease(currentVersionText);
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.NewerAvailable,
                    Release = fake,
                    NewerReleases = new[] { fake },
                    Fake = true,
                };
            }

            if (!ReleaseVersion.TryParse(currentVersionText, out var current))
            {
                return new UpdateCheckResult { Status = UpdateCheckStatus.CurrentVersionUnparseable };
            }

            List<ReleaseInfo> releases;
            try
            {
                using var response = await Http.GetAsync(ReleasesApiUrl).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    return new UpdateCheckResult
                    {
                        Status = UpdateCheckStatus.Failed,
                        Error = "GitHub returned " + (int)response.StatusCode + " " + response.ReasonPhrase,
                    };
                }

                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var parsed = JsonSerializer.Deserialize<List<GitHubRelease>>(json) ?? new List<GitHubRelease>();
                releases = parsed.Select(r => new ReleaseInfo
                {
                    TagName = r.TagName,
                    HtmlUrl = r.HtmlUrl,
                    Prerelease = r.Prerelease,
                    Draft = r.Draft,
                    Body = r.Body,
                    InstallerUrl = FindInstallerUrl(r.Assets),
                }).ToList();
            }
            catch (Exception ex)
            {
                return new UpdateCheckResult
                {
                    Status = UpdateCheckStatus.Failed,
                    Error = ex.GetType().Name + ": " + ex.Message,
                };
            }

            var newer = UpdateCheck.SelectAllNewer(current, releases);
            return new UpdateCheckResult
            {
                Status = newer.Count > 0 ? UpdateCheckStatus.NewerAvailable : UpdateCheckStatus.UpToDate,
                Release = newer.FirstOrDefault(),
                NewerReleases = newer,
            };
        }

        static string FindInstallerUrl(List<GitHubAsset> assets)
        {
            return assets?
                .FirstOrDefault(a => (a.Name != null)
                    && a.Name.StartsWith("NefMotoECUFlasher", StringComparison.OrdinalIgnoreCase)
                    && a.Name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                ?.BrowserDownloadUrl;
        }

        sealed class GitHubRelease
        {
            [JsonPropertyName("tag_name")]
            public string TagName { get; set; }

            [JsonPropertyName("html_url")]
            public string HtmlUrl { get; set; }

            [JsonPropertyName("prerelease")]
            public bool Prerelease { get; set; }

            [JsonPropertyName("draft")]
            public bool Draft { get; set; }

            [JsonPropertyName("body")]
            public string Body { get; set; }

            [JsonPropertyName("assets")]
            public List<GitHubAsset> Assets { get; set; }
        }

        sealed class GitHubAsset
        {
            [JsonPropertyName("name")]
            public string Name { get; set; }

            [JsonPropertyName("browser_download_url")]
            public string BrowserDownloadUrl { get; set; }
        }
    }
}

// vi: set sw=4 ts=8 expandtab:
