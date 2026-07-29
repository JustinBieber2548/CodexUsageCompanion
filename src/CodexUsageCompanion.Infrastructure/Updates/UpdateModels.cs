using CodexUsageCompanion.Core.Versioning;

namespace CodexUsageCompanion.Infrastructure.Updates;

public sealed record GitHubReleaseAsset(
    string Name,
    Uri DownloadUrl,
    long Size,
    string? Digest);

public sealed record GitHubReleaseInfo(
    SemanticVersion Version,
    string Name,
    string ReleaseNotes,
    Uri ReleasePage,
    DateTimeOffset? PublishedAtUtc,
    IReadOnlyList<GitHubReleaseAsset> Assets);

public enum UpdateCheckStatus
{
    UpdateAvailable,
    UpToDate,
    NotConfigured,
    Unavailable,
    InvalidRelease
}

public sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    GitHubReleaseInfo? Release = null,
    string? SanitizedMessage = null);

public sealed record UpdateDownloadResult(
    bool IsSuccess,
    string? FilePath = null,
    string? SanitizedMessage = null);
