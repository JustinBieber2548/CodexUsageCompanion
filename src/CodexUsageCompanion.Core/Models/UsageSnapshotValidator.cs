namespace CodexUsageCompanion.Core.Models;

public static class UsageSnapshotValidator
{
    public static IReadOnlyList<string> Validate(UsageSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var errors = new List<string>();
        if (!string.Equals(snapshot.Provider, "codex", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Provider must be Codex.");
        }

        if (snapshot.CapturedAtUtc == default)
        {
            errors.Add("CapturedAtUtc is required.");
        }

        if (string.IsNullOrWhiteSpace(snapshot.Source) || snapshot.Source.Length > 100)
        {
            errors.Add("Source is required and must not exceed 100 characters.");
        }

        ValidateWindow(snapshot.ShortTermWindow, "Short-term", errors);
        ValidateWindow(snapshot.WeeklyWindow, "Weekly", errors);
        return errors;
    }

    public static void ThrowIfInvalid(UsageSnapshot snapshot)
    {
        var errors = Validate(snapshot);
        if (errors.Count > 0)
        {
            throw new ArgumentException(string.Join(" ", errors), nameof(snapshot));
        }
    }

    private static void ValidateWindow(
        UsageWindow? window,
        string label,
        ICollection<string> errors)
    {
        if (window is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(window.Name) || window.Name.Length > 100)
        {
            errors.Add($"{label} window name is required and must not exceed 100 characters.");
        }

        ValidatePercent(window.RemainingPercent, $"{label} remaining percentage", errors);
        ValidatePercent(window.UsedPercent, $"{label} used percentage", errors);

        if (window.WindowDuration is { } duration && duration <= TimeSpan.Zero)
        {
            errors.Add($"{label} window duration must be positive.");
        }

        if (window.IsConfirmed && window.IsEstimated)
        {
            errors.Add($"{label} window cannot be both confirmed and estimated.");
        }
    }

    private static void ValidatePercent(
        decimal? value,
        string label,
        ICollection<string> errors)
    {
        if (value is < 0 or > 100)
        {
            errors.Add($"{label} must be between 0 and 100.");
        }
    }
}
