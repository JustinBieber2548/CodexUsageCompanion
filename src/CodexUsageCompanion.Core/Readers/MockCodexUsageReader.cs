using CodexUsageCompanion.Core.Abstractions;
using CodexUsageCompanion.Core.Models;
using CodexUsageCompanion.Core.Settings;

namespace CodexUsageCompanion.Core.Readers;

public sealed class MockCodexUsageReader : ICodexUsageReader
{
    private readonly IClock _clock;
    private int _readCount;

    public MockCodexUsageReader(IClock clock, MockScenario scenario = MockScenario.Normal)
    {
        _clock = clock;
        Scenario = scenario;
    }

    public string SourceName => "mock";

    public MockScenario Scenario { get; set; }

    public Task<UsageReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var read = Interlocked.Increment(ref _readCount);
        var now = _clock.UtcNow;

        return Task.FromResult(Scenario switch
        {
            MockScenario.AuthenticationFailure => UsageReadResult.Failure(
                UsageFreshness.AuthenticationRequired,
                "mock_authentication_required",
                "Mock authentication requires attention."),
            MockScenario.NetworkFailure => UsageReadResult.Failure(
                UsageFreshness.Offline,
                "mock_network_unavailable",
                "Mock network is unavailable."),
            MockScenario.Unsupported => UsageReadResult.Failure(
                UsageFreshness.UnsupportedSource,
                "mock_unsupported",
                "The mock source is unsupported."),
            _ => UsageReadResult.Success(CreateSnapshot(Scenario, read, now))
        });
    }

    private static UsageSnapshot CreateSnapshot(
        MockScenario scenario,
        int read,
        DateTimeOffset now)
    {
        var shortRemaining = Math.Max(0, 62 - (read - 1));
        var weeklyRemaining = Math.Max(0, 83 - ((read - 1) / 3m));
        var capturedAt = now;
        var freshness = UsageFreshness.Fresh;
        var shortReset = now.AddHours(1).AddMinutes(42);
        var weeklyReset = now.AddDays(4);

        if (scenario == MockScenario.LowUsage)
        {
            shortRemaining = 9;
            weeklyRemaining = 4;
        }
        else if (scenario == MockScenario.Stale)
        {
            capturedAt = now.AddMinutes(-30);
            freshness = UsageFreshness.Stale;
        }
        else if (scenario == MockScenario.Reset)
        {
            if (read == 1)
            {
                shortRemaining = 4;
                weeklyRemaining = 5;
                shortReset = now.AddSeconds(-1);
                weeklyReset = now.AddSeconds(-1);
            }
            else
            {
                shortRemaining = 100;
                weeklyRemaining = 100;
                shortReset = now.AddHours(5);
                weeklyReset = now.AddDays(7);
            }
        }

        return new UsageSnapshot
        {
            ShortTermWindow = new UsageWindow
            {
                Name = "5-hour window",
                RemainingPercent = shortRemaining,
                UsedPercent = 100 - shortRemaining,
                ResetsAtUtc = shortReset,
                WindowDuration = TimeSpan.FromHours(5),
                IsConfirmed = true
            },
            WeeklyWindow = new UsageWindow
            {
                Name = "Weekly window",
                RemainingPercent = weeklyRemaining,
                UsedPercent = 100 - weeklyRemaining,
                ResetsAtUtc = weeklyReset,
                WindowDuration = TimeSpan.FromDays(7),
                IsConfirmed = true
            },
            CapturedAtUtc = capturedAt,
            Source = "mock",
            Freshness = freshness,
            IsAuthenticated = true
        };
    }
}
