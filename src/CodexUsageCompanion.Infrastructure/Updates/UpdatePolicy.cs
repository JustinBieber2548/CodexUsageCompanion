namespace CodexUsageCompanion.Infrastructure.Updates;

public static class UpdatePolicy
{
    public static bool ShouldAutomaticallyCheck(
        bool enabled,
        DateTimeOffset? lastCheckAtUtc,
        DateTimeOffset nowUtc)
    {
        return enabled
            && (lastCheckAtUtc is null || nowUtc - lastCheckAtUtc >= TimeSpan.FromDays(1));
    }
}
