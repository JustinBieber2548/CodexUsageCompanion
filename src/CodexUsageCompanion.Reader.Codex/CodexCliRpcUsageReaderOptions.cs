namespace CodexUsageCompanion.Reader.Codex;

public sealed record CodexCliRpcUsageReaderOptions
{
    public string ExecutablePath { get; init; } = "codex";

    public TimeSpan InitializeTimeout { get; init; } = TimeSpan.FromSeconds(8);

    public TimeSpan RequestTimeout { get; init; } = TimeSpan.FromSeconds(3);

    public int MaximumLineBytes { get; init; } = 1024 * 1024;
}
