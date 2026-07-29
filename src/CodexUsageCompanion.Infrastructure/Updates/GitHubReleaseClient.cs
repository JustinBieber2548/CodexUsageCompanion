using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;
using CodexUsageCompanion.Core.Versioning;

namespace CodexUsageCompanion.Infrastructure.Updates;

public sealed partial class GitHubReleaseClient
{
    private readonly HttpClient _httpClient;

    public GitHubReleaseClient(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    public async Task<UpdateCheckResult> CheckAsync(
        string repository,
        SemanticVersion currentVersion,
        CancellationToken cancellationToken = default)
    {
        if (!RepositoryPattern().IsMatch(repository)
            || repository.StartsWith("OWNER/", StringComparison.OrdinalIgnoreCase))
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.NotConfigured,
                SanitizedMessage: "Set the GitHub owner/repository in Settings.");
        }

        var endpoint = new Uri($"https://api.github.com/repos/{repository}/releases/latest");
        using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("CodexUsageCompanion", "1.0"));
        request.Headers.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2022-11-28");

        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return new UpdateCheckResult(
                    UpdateCheckStatus.Unavailable,
                    SanitizedMessage: "No GitHub Release is available for this repository.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(
                    UpdateCheckStatus.Unavailable,
                    SanitizedMessage: "GitHub Releases is temporarily unavailable.");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(
                stream,
                cancellationToken: cancellationToken).ConfigureAwait(false);
            var release = ParseRelease(document.RootElement);
            return release.Version.CompareTo(currentVersion) > 0
                ? new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, release)
                : new UpdateCheckResult(UpdateCheckStatus.UpToDate, release);
        }
        catch (HttpRequestException)
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.Unavailable,
                SanitizedMessage: "GitHub Releases could not be reached.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.Unavailable,
                SanitizedMessage: "The GitHub update check timed out.");
        }
        catch (JsonException)
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.InvalidRelease,
                SanitizedMessage: "GitHub returned an invalid release description.");
        }
        catch (InvalidDataException exception)
        {
            return new UpdateCheckResult(
                UpdateCheckStatus.InvalidRelease,
                SanitizedMessage: exception.Message);
        }
    }

    internal static GitHubReleaseInfo ParseRelease(JsonElement root)
    {
        var tag = root.GetProperty("tag_name").GetString();
        if (!SemanticVersion.TryParse(tag, out var version))
        {
            throw new InvalidDataException("The latest GitHub tag is not a semantic version.");
        }

        if (root.TryGetProperty("draft", out var draft) && draft.ValueKind == JsonValueKind.True)
        {
            throw new InvalidDataException("The latest GitHub release is still a draft.");
        }

        if (root.TryGetProperty("prerelease", out var prerelease)
            && prerelease.ValueKind == JsonValueKind.True)
        {
            throw new InvalidDataException("The latest GitHub release is a prerelease.");
        }

        var releasePageText = root.GetProperty("html_url").GetString();
        if (!Uri.TryCreate(releasePageText, UriKind.Absolute, out var releasePage)
            || releasePage.Scheme != Uri.UriSchemeHttps
            || !string.Equals(releasePage.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("The GitHub release page URL is invalid.");
        }

        var assets = new List<GitHubReleaseAsset>();
        if (root.TryGetProperty("assets", out var assetsElement)
            && assetsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assetsElement.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                var urlText = asset.GetProperty("browser_download_url").GetString();
                var size = asset.TryGetProperty("size", out var sizeElement)
                    && sizeElement.TryGetInt64(out var parsedSize)
                    ? parsedSize
                    : 0;
                var digest = asset.TryGetProperty("digest", out var digestElement)
                    && digestElement.ValueKind == JsonValueKind.String
                    ? digestElement.GetString()
                    : null;
                if (string.IsNullOrWhiteSpace(name)
                    || !Uri.TryCreate(urlText, UriKind.Absolute, out var url)
                    || url.Scheme != Uri.UriSchemeHttps
                    || !string.Equals(url.Host, "github.com", StringComparison.OrdinalIgnoreCase)
                    || size <= 0)
                {
                    continue;
                }

                assets.Add(new GitHubReleaseAsset(name, url, size, digest));
            }
        }

        var publishedAt = root.TryGetProperty("published_at", out var published)
            && published.ValueKind == JsonValueKind.String
            && DateTimeOffset.TryParse(published.GetString(), out var parsedPublished)
            ? parsedPublished
            : (DateTimeOffset?)null;
        return new GitHubReleaseInfo(
            version,
            root.TryGetProperty("name", out var nameElement)
                ? nameElement.GetString() ?? $"Version {version}"
                : $"Version {version}",
            root.TryGetProperty("body", out var bodyElement)
                ? bodyElement.GetString() ?? "No release notes were supplied."
                : "No release notes were supplied.",
            releasePage,
            publishedAt,
            assets);
    }

    [GeneratedRegex(
        @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$",
        RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryPattern();
}
