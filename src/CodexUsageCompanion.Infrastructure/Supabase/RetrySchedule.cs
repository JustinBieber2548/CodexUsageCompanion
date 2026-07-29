namespace CodexUsageCompanion.Infrastructure.Supabase;

public static class RetrySchedule
{
    public static TimeSpan Calculate(int attempt, double jitterSample)
    {
        var boundedAttempt = Math.Clamp(attempt, 0, 10);
        var baseSeconds = Math.Min(30 * Math.Pow(2, boundedAttempt), 30 * 60);
        var boundedJitter = Math.Clamp(jitterSample, 0, 1);
        return TimeSpan.FromSeconds(baseSeconds * (0.8 + (0.4 * boundedJitter)));
    }
}
