using CodexUsageCompanion.Core.Models;

namespace CodexUsageCompanion.Reader.Codex;

internal enum CodexUsageWindowKind
{
    ShortTerm,
    Weekly
}

internal static class CodexUsageWindowClassifier
{
    private static readonly TimeSpan WeeklyMinimumDuration = TimeSpan.FromDays(6);
    private static readonly TimeSpan ShortTermMaximumDuration = TimeSpan.FromDays(1);

    public static void Assign(
        UsageWindow? candidate,
        CodexUsageWindowKind fallbackKind,
        ref UsageWindow? shortTerm,
        ref UsageWindow? weekly)
    {
        if (candidate is null)
        {
            return;
        }

        var kind = Classify(candidate.WindowDuration, fallbackKind);
        var normalized = candidate with
        {
            Name = kind == CodexUsageWindowKind.Weekly
                ? "Weekly window"
                : candidate.WindowDuration == TimeSpan.FromHours(5)
                    ? "5-hour window"
                    : "Short-term window"
        };

        if (kind == CodexUsageWindowKind.Weekly)
        {
            weekly ??= normalized;
        }
        else
        {
            shortTerm ??= normalized;
        }
    }

    private static CodexUsageWindowKind Classify(
        TimeSpan? duration,
        CodexUsageWindowKind fallbackKind)
    {
        if (duration >= WeeklyMinimumDuration)
        {
            return CodexUsageWindowKind.Weekly;
        }

        if (duration <= ShortTermMaximumDuration)
        {
            return CodexUsageWindowKind.ShortTerm;
        }

        return fallbackKind;
    }
}
