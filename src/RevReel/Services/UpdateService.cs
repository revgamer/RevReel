using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace RevReel.Services
{
    public enum UpdateStatus
    {
        UpToDate,
        UpdateAvailable,
        NoReleaseFound,
        Failed
    }

    /// <summary>Result of an update check, with a plain-English message for the user.</summary>
    public sealed record UpdateResult(UpdateStatus Status, string Message, string? LatestVersion = null, string? ReleaseUrl = null);

    /// <summary>
    /// Asks GitHub Releases for the newest published version. Never throws:
    /// problems come back as a result with a message. A release must be
    /// tagged like "v0.2.0" for the version to be understood.
    /// </summary>
    public static class UpdateService
    {
        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            // GitHub rejects requests with no User-Agent.
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(AppInfo.Name, AppInfo.VersionText));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return client;
        }

        public static async Task<UpdateResult> CheckAsync()
        {
            try
            {
                using HttpResponseMessage response = await Http.GetAsync(AppInfo.LatestReleaseApiUrl).ConfigureAwait(false);

                if (response.StatusCode == HttpStatusCode.NotFound)
                {
                    // GitHub also answers 404 for a private repository.
                    return new UpdateResult(UpdateStatus.NoReleaseFound,
                        "No published release was found yet. (If the repository is private, the update check cannot see it.)");
                }

                if (!response.IsSuccessStatusCode)
                {
                    return new UpdateResult(UpdateStatus.Failed,
                        $"Could not check for updates. GitHub answered with status {(int)response.StatusCode}.");
                }

                string json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                using JsonDocument doc = JsonDocument.Parse(json);

                string? tag = doc.RootElement.TryGetProperty("tag_name", out JsonElement tagElement)
                    ? tagElement.GetString()
                    : null;

                if (string.IsNullOrWhiteSpace(tag))
                {
                    return new UpdateResult(UpdateStatus.Failed, "Could not read the latest version from GitHub.");
                }

                string versionText = tag.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? tag[1..] : tag;
                if (!Version.TryParse(versionText, out Version? latest))
                {
                    return new UpdateResult(UpdateStatus.Failed, $"The latest release tag \"{tag}\" is not a version number.");
                }

                // Compare as Major.Minor.Build only, so 0.1.0 equals 0.1.0.0.
                latest = new Version(latest.Major, latest.Minor, Math.Max(latest.Build, 0));

                if (latest <= AppInfo.CurrentVersion)
                {
                    return new UpdateResult(UpdateStatus.UpToDate,
                        $"You are using the latest version ({AppInfo.VersionText}).");
                }

                string releaseUrl = doc.RootElement.TryGetProperty("html_url", out JsonElement urlElement)
                    ? urlElement.GetString() ?? AppInfo.ReleasesUrl
                    : AppInfo.ReleasesUrl;

                return new UpdateResult(UpdateStatus.UpdateAvailable,
                    $"Version {latest} is available. You are using {AppInfo.VersionText}.",
                    latest.ToString(), releaseUrl);
            }
            catch (Exception ex)
            {
                return new UpdateResult(UpdateStatus.Failed,
                    $"Could not check for updates. Check your internet connection. ({ex.Message})");
            }
        }
    }
}
