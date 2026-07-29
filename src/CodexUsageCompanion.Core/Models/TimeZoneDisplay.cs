namespace CodexUsageCompanion.Core.Models;

public static class TimeZoneDisplay
{
    public static DateTimeOffset ConvertFromUtc(DateTimeOffset utc, string timeZoneId)
    {
        if (string.IsNullOrWhiteSpace(timeZoneId))
        {
            throw new ArgumentException("A time-zone identifier is required.", nameof(timeZoneId));
        }

        var zone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        return TimeZoneInfo.ConvertTime(utc, zone);
    }
}
