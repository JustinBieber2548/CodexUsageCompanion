using CodexUsageCompanion.Core.Models;

namespace CodexUsageCompanion.Core.Notifications;

public enum NotificationEventKind
{
    ThresholdReached,
    ResetApproaching,
    ResetTimeArrived,
    ResetConfirmed,
    MonitoringOffline,
    MonitoringRestored,
    AuthenticationRequired,
    UsageChanged
}

public enum UsageWindowKind
{
    ShortTerm,
    Weekly
}

public sealed record NotificationEvent(
    string EventKey,
    NotificationEventKind Kind,
    string Title,
    string Message,
    DateTimeOffset CreatedAtUtc,
    UsageWindowKind? WindowKind = null,
    decimal? Threshold = null);

public sealed record WindowNotificationState
{
    public DateTimeOffset? LastResetAtUtc { get; init; }

    public decimal? LastRemainingPercent { get; init; }

    public HashSet<decimal> FiredThresholds { get; init; } = [];
}

public sealed record NotificationState
{
    public DateTimeOffset? MonitoringStartedAtUtc { get; init; }

    public WindowNotificationState ShortTerm { get; init; } = new();

    public WindowNotificationState Weekly { get; init; } = new();

    public bool OfflineAlertActive { get; init; }

    public bool AuthenticationAlertActive { get; init; }

    public HashSet<string> DeliveredEventKeys { get; init; } = new(StringComparer.Ordinal);
}

public sealed record NotificationEvaluation(
    NotificationState State,
    IReadOnlyList<NotificationEvent> Events);

public sealed record NotificationPreferences
{
    public IReadOnlyList<decimal> ShortTermThresholds { get; init; } = [10, 5, 0];

    public IReadOnlyList<decimal> WeeklyThresholds { get; init; } = [10, 5];

    public bool ShortTermResetWarningEnabled { get; init; } = true;

    public bool ResetDetectedEnabled { get; init; } = true;

    public bool UsageChangeEnabled { get; init; }

    public TimeSpan OfflineAfter { get; init; } = TimeSpan.FromMinutes(15);

    public decimal RearmMargin { get; init; } = 5;

    public static NotificationPreferences FromSettings(Settings.AppSettings settings)
    {
        return new NotificationPreferences
        {
            ShortTermResetWarningEnabled = settings.ShortTermResetWarningEnabled,
            ResetDetectedEnabled = settings.ResetDetectedEnabled,
            UsageChangeEnabled = settings.EveryUsageChangeEnabled,
            OfflineAfter = TimeSpan.FromMinutes(settings.OfflineAfterMinutes)
        };
    }
}
