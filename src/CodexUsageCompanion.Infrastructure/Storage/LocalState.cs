using CodexUsageCompanion.Core.Models;
using CodexUsageCompanion.Core.Notifications;

namespace CodexUsageCompanion.Infrastructure.Storage;

public sealed record PendingSynchronization
{
    public required UsageSnapshot Snapshot { get; init; }

    public required DateTimeOffset QueuedAtUtc { get; init; }

    public int AttemptCount { get; init; }

    public DateTimeOffset? NextAttemptAtUtc { get; init; }
}

public sealed record LocalState
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public UsageSnapshot? LatestSnapshot { get; init; }

    public List<UsageSnapshot> History { get; init; } = [];

    public NotificationState NotificationState { get; init; } = new();

    public PendingSynchronization? PendingSynchronization { get; init; }

    public DateTimeOffset? LastSuccessfulReadAtUtc { get; init; }

    public DateTimeOffset? LastSuccessfulCloudSyncAtUtc { get; init; }

    public DateTimeOffset? LastUpdateCheckAtUtc { get; init; }

    public UsageFreshness CurrentStatus { get; init; } = UsageFreshness.Stale;

    public string? LastErrorCode { get; init; }

    public string? LastSanitizedErrorMessage { get; init; }

    public LocalState WithSuccessfulSnapshot(UsageSnapshot snapshot, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        UsageSnapshotValidator.ThrowIfInvalid(snapshot);

        var history = new List<UsageSnapshot>(History);
        var meaningfulChange = LatestSnapshot is null
            || !WindowEquivalent(LatestSnapshot.ShortTermWindow, snapshot.ShortTermWindow)
            || !WindowEquivalent(LatestSnapshot.WeeklyWindow, snapshot.WeeklyWindow)
            || nowUtc - LatestSnapshot.CapturedAtUtc >= TimeSpan.FromHours(1);
        if (meaningfulChange)
        {
            history.Add(snapshot);
        }

        history = history
            .Where(item => nowUtc - item.CapturedAtUtc <= TimeSpan.FromDays(30))
            .OrderBy(item => item.CapturedAtUtc)
            .TakeLast(500)
            .ToList();

        return this with
        {
            LatestSnapshot = snapshot,
            History = history,
            LastSuccessfulReadAtUtc = nowUtc,
            CurrentStatus = snapshot.Freshness,
            LastErrorCode = null,
            LastSanitizedErrorMessage = null,
            PendingSynchronization = new PendingSynchronization
            {
                Snapshot = snapshot,
                QueuedAtUtc = nowUtc
            }
        };
    }

    public LocalState WithReadFailure(
        UsageFreshness status,
        string? errorCode,
        string? sanitizedMessage)
    {
        return this with
        {
            CurrentStatus = status,
            LastErrorCode = errorCode,
            LastSanitizedErrorMessage = sanitizedMessage
        };
    }

    private static bool WindowEquivalent(UsageWindow? left, UsageWindow? right)
    {
        if (left is null || right is null)
        {
            return left is null && right is null;
        }

        return left.RemainingPercent == right.RemainingPercent
            && left.UsedPercent == right.UsedPercent
            && left.ResetsAtUtc == right.ResetsAtUtc
            && left.IsConfirmed == right.IsConfirmed
            && left.IsEstimated == right.IsEstimated;
    }
}
