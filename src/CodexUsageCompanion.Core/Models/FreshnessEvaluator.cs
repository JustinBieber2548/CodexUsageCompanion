namespace CodexUsageCompanion.Core.Models;

public static class FreshnessEvaluator
{
    public static UsageFreshness Evaluate(
        UsageSnapshot snapshot,
        DateTimeOffset nowUtc,
        TimeSpan staleAfter)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Freshness != UsageFreshness.Fresh)
        {
            return snapshot.Freshness;
        }

        return nowUtc - snapshot.CapturedAtUtc > staleAfter
            ? UsageFreshness.Stale
            : UsageFreshness.Fresh;
    }
}
