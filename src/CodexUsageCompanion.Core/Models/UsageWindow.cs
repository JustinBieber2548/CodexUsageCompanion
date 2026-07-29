using System.Text.Json.Serialization;

namespace CodexUsageCompanion.Core.Models;

public sealed record UsageWindow
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("remainingPercent")]
    public decimal? RemainingPercent { get; init; }

    [JsonPropertyName("usedPercent")]
    public decimal? UsedPercent { get; init; }

    [JsonPropertyName("resetAtUtc")]
    public DateTimeOffset? ResetsAtUtc { get; init; }

    [JsonPropertyName("windowDuration")]
    public TimeSpan? WindowDuration { get; init; }

    [JsonPropertyName("confirmed")]
    public bool IsConfirmed { get; init; }

    [JsonPropertyName("estimated")]
    public bool IsEstimated { get; init; }
}
