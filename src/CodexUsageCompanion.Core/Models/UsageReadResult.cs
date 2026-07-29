namespace CodexUsageCompanion.Core.Models;

public sealed record UsageReadResult
{
    public bool IsSuccess { get; init; }

    public UsageSnapshot? Snapshot { get; init; }

    public UsageFreshness Status { get; init; }

    public string? ErrorCode { get; init; }

    public string? SanitizedErrorMessage { get; init; }

    public static UsageReadResult Success(UsageSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new UsageReadResult
        {
            IsSuccess = true,
            Snapshot = snapshot,
            Status = snapshot.Freshness
        };
    }

    public static UsageReadResult Failure(
        UsageFreshness status,
        string errorCode,
        string sanitizedMessage)
    {
        return new UsageReadResult
        {
            IsSuccess = false,
            Status = status,
            ErrorCode = errorCode,
            SanitizedErrorMessage = sanitizedMessage
        };
    }
}
