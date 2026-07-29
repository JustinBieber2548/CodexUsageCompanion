namespace CodexUsageCompanion.Core.Settings;

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string DeviceId { get; init; } = Guid.NewGuid().ToString("D");

    public string DeviceLabel { get; init; } = Environment.MachineName;

    public string TimeZoneId { get; init; } = "Asia/Bangkok";

    public ReaderMode ReaderMode { get; init; } = ReaderMode.Mock;

    public MockScenario MockScenario { get; init; } = MockScenario.Normal;

    public bool ExperimentalOAuthConsent { get; init; }

    public int ActiveRefreshMinutes { get; init; } = 2;

    public int IdleRefreshMinutes { get; init; } = 5;

    public int StaleAfterMinutes { get; init; } = 10;

    public int OfflineAfterMinutes { get; init; } = 15;

    public bool WindowsNotificationsEnabled { get; init; } = true;

    public bool ShortTermResetWarningEnabled { get; init; } = true;

    public bool ResetDetectedEnabled { get; init; } = true;

    public bool EveryUsageChangeEnabled { get; init; }

    public bool ActivityNotificationsEnabled { get; init; }

    public bool DailySummaryEnabled { get; init; }

    public bool StartWithWindows { get; init; }

    public bool AutoCheckForUpdates { get; init; } = true;

    public string GitHubRepository { get; init; } = "OWNER/CodexUsageCompanion";

    public string? SupabaseUrl { get; init; }

    public string? SupabasePublishableKey { get; init; }

    public string? SupabaseEmail { get; init; }
}
