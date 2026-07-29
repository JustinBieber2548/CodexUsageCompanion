namespace CodexUsageCompanion.Reader.Codex;

public sealed record CodexOAuthUsageReaderOptions
{
    public const int DefaultMaximumAuthFileBytes = 128 * 1024;
    public const int DefaultMaximumResponseBytes = 1024 * 1024;

    public string? CodexHomePath { get; init; }

    public int MaximumAuthFileBytes { get; init; } = DefaultMaximumAuthFileBytes;

    public int MaximumResponseBytes { get; init; } = DefaultMaximumResponseBytes;

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(10);
}
