using CodexUsageCompanion.Core.Models;
using CodexUsageCompanion.Core.Notifications;
using CodexUsageCompanion.Core.Settings;
using CodexUsageCompanion.Core.Versioning;

namespace CodexUsageCompanion.Core.Tests;

public sealed class CoreBehaviorTests
{
    [Fact]
    public void Validator_accepts_missing_optional_window_and_rejects_invalid_percent()
    {
        var valid = Snapshot(50, null);
        Assert.Empty(UsageSnapshotValidator.Validate(valid with { WeeklyWindow = null }));

        var invalid = valid with
        {
            ShortTermWindow = valid.ShortTermWindow! with { RemainingPercent = 101 }
        };
        Assert.Contains(
            UsageSnapshotValidator.Validate(invalid),
            error => error.Contains("between 0 and 100", StringComparison.Ordinal));
    }

    [Fact]
    public void Validator_requires_timestamp_and_consistent_confidence()
    {
        var invalid = Snapshot(50, 50) with
        {
            CapturedAtUtc = default,
            ShortTermWindow = Snapshot(50, 50).ShortTermWindow! with
            {
                IsConfirmed = true,
                IsEstimated = true
            }
        };

        var errors = UsageSnapshotValidator.Validate(invalid);
        Assert.Contains(errors, error => error.Contains("CapturedAtUtc", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("both confirmed and estimated", StringComparison.Ordinal));
    }

    [Fact]
    public void Freshness_uses_capture_timestamp()
    {
        var now = DateTimeOffset.Parse("2026-07-29T12:00:00Z");
        var snapshot = Snapshot(50, 50) with { CapturedAtUtc = now.AddMinutes(-11) };
        Assert.Equal(
            UsageFreshness.Stale,
            FreshnessEvaluator.Evaluate(snapshot, now, TimeSpan.FromMinutes(10)));
    }

    [Fact]
    public void Time_zone_conversion_preserves_the_instant()
    {
        var utc = DateTimeOffset.Parse("2026-07-29T12:00:00Z");
        var converted = TimeZoneDisplay.ConvertFromUtc(utc, "UTC");
        Assert.Equal(utc, converted);
    }

    [Fact]
    public void Semantic_versions_follow_semver_precedence()
    {
        Assert.True(SemanticVersion.Parse("1.1.0").CompareTo(SemanticVersion.Parse("1.0.9")) > 0);
        Assert.True(SemanticVersion.Parse("1.0.0").CompareTo(SemanticVersion.Parse("1.0.0-rc.1")) > 0);
        Assert.Equal(
            0,
            SemanticVersion.Parse("v1.0.0+build.5").CompareTo(SemanticVersion.Parse("1.0.0+other")));
        Assert.False(SemanticVersion.TryParse("01.0.0", out _));
        Assert.False(SemanticVersion.TryParse("1.0.0-01", out _));
    }

    [Fact]
    public void Threshold_notifications_fire_once_across_restart()
    {
        var now = DateTimeOffset.Parse("2026-07-29T12:00:00Z");
        var engine = new NotificationEngine();
        var preferences = new NotificationPreferences();
        var first = engine.Evaluate(
            Snapshot(4, 4, now.AddHours(2)),
            new NotificationState(),
            preferences,
            now,
            now,
            UsageFreshness.Fresh);
        Assert.Contains(first.Events, item => item.Threshold == 10);
        Assert.Contains(first.Events, item => item.Threshold == 5);

        var afterRestart = engine.Evaluate(
            Snapshot(4, 4, now.AddHours(2)),
            first.State,
            preferences,
            now.AddMinutes(1),
            now.AddMinutes(1),
            UsageFreshness.Fresh);
        Assert.DoesNotContain(
            afterRestart.Events,
            item => item.Kind == NotificationEventKind.ThresholdReached);
    }

    [Fact]
    public void Confirmed_reset_rearms_thresholds()
    {
        var now = DateTimeOffset.Parse("2026-07-29T12:00:00Z");
        var reset = now.AddMinutes(-1);
        var engine = new NotificationEngine();
        var preferences = new NotificationPreferences();
        var low = engine.Evaluate(
            Snapshot(4, 30, reset),
            new NotificationState(),
            preferences,
            now,
            now,
            UsageFreshness.Fresh);
        var refreshed = engine.Evaluate(
            Snapshot(100, 30, now.AddHours(5)),
            low.State,
            preferences,
            now.AddMinutes(1),
            now.AddMinutes(1),
            UsageFreshness.Fresh);
        Assert.Contains(refreshed.Events, item => item.Kind == NotificationEventKind.ResetConfirmed);

        var lowAgain = engine.Evaluate(
            Snapshot(9, 30, now.AddHours(5)),
            refreshed.State,
            preferences,
            now.AddMinutes(2),
            now.AddMinutes(2),
            UsageFreshness.Fresh);
        Assert.Contains(lowAgain.Events, item => item.Threshold == 10);
    }

    [Fact]
    public void Passing_reset_timestamp_never_claims_full_allowance()
    {
        var now = DateTimeOffset.Parse("2026-07-29T12:00:00Z");
        var evaluation = new NotificationEngine().Evaluate(
            Snapshot(4, 40, now.AddSeconds(-1)),
            new NotificationState(),
            new NotificationPreferences(),
            now,
            now,
            UsageFreshness.Fresh);

        var arrival = Assert.Single(
            evaluation.Events.Where(item => item.Kind == NotificationEventKind.ResetTimeArrived));
        Assert.Contains("Waiting", arrival.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("100", arrival.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Offline_and_restored_notifications_are_transition_based()
    {
        var start = DateTimeOffset.Parse("2026-07-29T12:00:00Z");
        var engine = new NotificationEngine();
        var preferences = new NotificationPreferences { OfflineAfter = TimeSpan.FromMinutes(15) };
        var initial = engine.Evaluate(
            null,
            new NotificationState(),
            preferences,
            start,
            null,
            UsageFreshness.Offline);
        Assert.Empty(initial.Events);

        var offline = engine.Evaluate(
            null,
            initial.State,
            preferences,
            start.AddMinutes(15),
            null,
            UsageFreshness.Offline);
        Assert.Single(offline.Events, item => item.Kind == NotificationEventKind.MonitoringOffline);

        var restored = engine.Evaluate(
            Snapshot(50, 50),
            offline.State,
            preferences,
            start.AddMinutes(16),
            start.AddMinutes(16),
            UsageFreshness.Fresh);
        Assert.Single(restored.Events, item => item.Kind == NotificationEventKind.MonitoringRestored);
    }

    [Fact]
    public void Settings_validate_refresh_and_time_zone()
    {
        var invalid = new AppSettings
        {
            ActiveRefreshMinutes = 0,
            TimeZoneId = "This/Zone/Does/Not/Exist"
        };
        var errors = AppSettingsValidator.Validate(invalid);
        Assert.Contains(errors, error => error.Contains("ActiveRefreshMinutes", StringComparison.Ordinal));
        Assert.Contains(errors, error => error.Contains("TimeZoneId", StringComparison.Ordinal));
    }

    private static UsageSnapshot Snapshot(
        decimal? shortRemaining,
        decimal? weeklyRemaining,
        DateTimeOffset? shortReset = null)
    {
        var now = DateTimeOffset.Parse("2026-07-29T12:00:00Z");
        return new UsageSnapshot
        {
            CapturedAtUtc = now,
            Source = "test",
            Freshness = UsageFreshness.Fresh,
            IsAuthenticated = true,
            ShortTermWindow = shortRemaining is null
                ? null
                : new UsageWindow
                {
                    Name = "5-hour window",
                    RemainingPercent = shortRemaining,
                    UsedPercent = 100 - shortRemaining,
                    ResetsAtUtc = shortReset ?? now.AddHours(2),
                    IsConfirmed = true
                },
            WeeklyWindow = weeklyRemaining is null
                ? null
                : new UsageWindow
                {
                    Name = "Weekly window",
                    RemainingPercent = weeklyRemaining,
                    UsedPercent = 100 - weeklyRemaining,
                    ResetsAtUtc = now.AddDays(3),
                    IsConfirmed = true
                }
        };
    }
}
