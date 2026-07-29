using CodexUsageCompanion.Core.Models;

namespace CodexUsageCompanion.Core.Notifications;

public sealed class NotificationEngine
{
    private const int MaximumRememberedEventKeys = 500;

    public NotificationEvaluation Evaluate(
        UsageSnapshot? snapshot,
        NotificationState previousState,
        NotificationPreferences preferences,
        DateTimeOffset nowUtc,
        DateTimeOffset? lastSuccessfulReadAtUtc,
        UsageFreshness currentStatus)
    {
        ArgumentNullException.ThrowIfNull(previousState);
        ArgumentNullException.ThrowIfNull(preferences);

        var events = new List<NotificationEvent>();
        var deliveredKeys = new HashSet<string>(
            previousState.DeliveredEventKeys,
            StringComparer.Ordinal);

        var shortState = previousState.ShortTerm;
        var weeklyState = previousState.Weekly;

        if (snapshot is not null)
        {
            shortState = EvaluateWindow(
                UsageWindowKind.ShortTerm,
                snapshot.ShortTermWindow,
                shortState,
                preferences.ShortTermThresholds,
                preferences,
                nowUtc,
                deliveredKeys,
                events);
            weeklyState = EvaluateWindow(
                UsageWindowKind.Weekly,
                snapshot.WeeklyWindow,
                weeklyState,
                preferences.WeeklyThresholds,
                preferences,
                nowUtc,
                deliveredKeys,
                events);
        }

        var monitoringStartedAtUtc = previousState.MonitoringStartedAtUtc ?? nowUtc;
        var offline = previousState.OfflineAlertActive;
        var lastObservedOnlineAtUtc = lastSuccessfulReadAtUtc ?? monitoringStartedAtUtc;
        var isOffline = nowUtc - lastObservedOnlineAtUtc >= preferences.OfflineAfter;
        if (isOffline && !offline)
        {
            AddEvent(
                new NotificationEvent(
                    $"monitoring:offline:{nowUtc:yyyyMMddHHmm}",
                    NotificationEventKind.MonitoringOffline,
                    "Codex monitoring is offline",
                    $"No successful local reading for at least {preferences.OfflineAfter.TotalMinutes:0} minutes.",
                    nowUtc),
                deliveredKeys,
                events);
            offline = true;
        }
        else if (!isOffline && offline)
        {
            AddEvent(
                new NotificationEvent(
                    $"monitoring:restored:{lastSuccessfulReadAtUtc:O}",
                    NotificationEventKind.MonitoringRestored,
                    "Codex monitoring restored",
                    "Fresh local usage readings have resumed.",
                    nowUtc),
                deliveredKeys,
                events);
            offline = false;
        }

        var authenticationAlertActive = previousState.AuthenticationAlertActive;
        if (currentStatus == UsageFreshness.AuthenticationRequired && !authenticationAlertActive)
        {
            AddEvent(
                new NotificationEvent(
                    "authentication:required",
                    NotificationEventKind.AuthenticationRequired,
                    "Codex authentication needs attention",
                    "Open Codex and sign in, then refresh Codex Usage Companion.",
                    nowUtc),
                deliveredKeys,
                events);
            authenticationAlertActive = true;
        }
        else if (currentStatus != UsageFreshness.AuthenticationRequired)
        {
            authenticationAlertActive = false;
            deliveredKeys.Remove("authentication:required");
        }

        TrimDeliveredKeys(deliveredKeys);
        return new NotificationEvaluation(
            new NotificationState
            {
                MonitoringStartedAtUtc = monitoringStartedAtUtc,
                ShortTerm = shortState,
                Weekly = weeklyState,
                OfflineAlertActive = offline,
                AuthenticationAlertActive = authenticationAlertActive,
                DeliveredEventKeys = deliveredKeys
            },
            events);
    }

    private static WindowNotificationState EvaluateWindow(
        UsageWindowKind kind,
        UsageWindow? window,
        WindowNotificationState previous,
        IReadOnlyList<decimal> thresholds,
        NotificationPreferences preferences,
        DateTimeOffset nowUtc,
        HashSet<string> deliveredKeys,
        ICollection<NotificationEvent> events)
    {
        if (window is null)
        {
            return previous;
        }

        var fired = new HashSet<decimal>(previous.FiredThresholds);
        var remaining = window.RemainingPercent;
        var resetAt = window.ResetsAtUtc;
        var confirmedReset = window.IsConfirmed
            && remaining is not null
            && previous.LastRemainingPercent is not null
            && ((resetAt is not null
                    && previous.LastResetAtUtc is not null
                    && resetAt > previous.LastResetAtUtc
                    && remaining > previous.LastRemainingPercent)
                || remaining >= previous.LastRemainingPercent + 20);

        if (confirmedReset)
        {
            fired.Clear();
            if (preferences.ResetDetectedEnabled)
            {
                AddEvent(
                    new NotificationEvent(
                        $"reset:confirmed:{kind}:{resetAt?.ToUnixTimeSeconds().ToString() ?? nowUtc.ToUnixTimeSeconds().ToString()}",
                        NotificationEventKind.ResetConfirmed,
                        $"Codex {WindowLabel(kind)} reset confirmed",
                        $"{FormatPercent(remaining)} remaining is now confirmed.",
                        nowUtc,
                        kind),
                    deliveredKeys,
                    events);
            }
        }
        else if (window.IsConfirmed && remaining is not null)
        {
            foreach (var threshold in thresholds)
            {
                if (remaining > threshold + preferences.RearmMargin)
                {
                    fired.Remove(threshold);
                }
            }
        }

        if (window.IsConfirmed && remaining is not null)
        {
            foreach (var threshold in thresholds.OrderByDescending(value => value))
            {
                if (remaining <= threshold && fired.Add(threshold))
                {
                    AddEvent(
                        new NotificationEvent(
                            $"threshold:{kind}:{ResetKey(resetAt)}:{threshold:0.####}",
                            NotificationEventKind.ThresholdReached,
                            $"Codex {WindowLabel(kind)} usage warning",
                            $"{FormatPercent(remaining)} remaining; the {threshold:0.####}% threshold was reached.",
                            nowUtc,
                            kind,
                            threshold),
                        deliveredKeys,
                        events);
                }
            }
        }

        if (resetAt is not null)
        {
            var untilReset = resetAt.Value - nowUtc;
            if (kind == UsageWindowKind.ShortTerm
                && preferences.ShortTermResetWarningEnabled
                && untilReset > TimeSpan.Zero
                && untilReset <= TimeSpan.FromMinutes(10))
            {
                AddEvent(
                    new NotificationEvent(
                        $"reset:approaching:{kind}:{resetAt.Value.ToUnixTimeSeconds()}",
                        NotificationEventKind.ResetApproaching,
                        "Codex short-term reset is approaching",
                        $"The known reset time is in {Math.Max(1, Math.Ceiling(untilReset.TotalMinutes)):0} minute(s).",
                        nowUtc,
                        kind),
                    deliveredKeys,
                    events);
            }
            else if (untilReset <= TimeSpan.Zero && preferences.ResetDetectedEnabled)
            {
                AddEvent(
                    new NotificationEvent(
                        $"reset:arrived:{kind}:{resetAt.Value.ToUnixTimeSeconds()}",
                        NotificationEventKind.ResetTimeArrived,
                        $"Codex {WindowLabel(kind)} reset time arrived",
                        "Waiting for the next device reading to confirm the refreshed allowance.",
                        nowUtc,
                        kind),
                    deliveredKeys,
                    events);
            }
        }

        if (preferences.UsageChangeEnabled
            && remaining is not null
            && previous.LastRemainingPercent is not null
            && remaining != previous.LastRemainingPercent)
        {
            AddEvent(
                new NotificationEvent(
                    $"usage-change:{kind}:{nowUtc.ToUnixTimeSeconds()}:{remaining:0.####}",
                    NotificationEventKind.UsageChanged,
                    $"Codex {WindowLabel(kind)} usage changed",
                    $"{FormatPercent(remaining)} remaining.",
                    nowUtc,
                    kind),
                deliveredKeys,
                events);
        }

        return new WindowNotificationState
        {
            LastResetAtUtc = resetAt ?? previous.LastResetAtUtc,
            LastRemainingPercent = remaining ?? previous.LastRemainingPercent,
            FiredThresholds = fired
        };
    }

    private static void AddEvent(
        NotificationEvent notification,
        ISet<string> deliveredKeys,
        ICollection<NotificationEvent> events)
    {
        if (deliveredKeys.Add(notification.EventKey))
        {
            events.Add(notification);
        }
    }

    private static string ResetKey(DateTimeOffset? resetAt)
    {
        return resetAt?.ToUnixTimeSeconds().ToString() ?? "unknown";
    }

    private static string WindowLabel(UsageWindowKind kind)
    {
        return kind == UsageWindowKind.ShortTerm ? "short-term window" : "weekly window";
    }

    private static string FormatPercent(decimal? value)
    {
        return value is null ? "Usage unavailable" : $"{Math.Round(value.Value, 1):0.#}%";
    }

    private static void TrimDeliveredKeys(HashSet<string> keys)
    {
        if (keys.Count <= MaximumRememberedEventKeys)
        {
            return;
        }

        foreach (var key in keys.OrderBy(value => value, StringComparer.Ordinal)
                     .Take(keys.Count - MaximumRememberedEventKeys)
                     .ToArray())
        {
            keys.Remove(key);
        }
    }
}
