using System.Text.Json.Serialization;

namespace CodexUsageCompanion.Core.Models;

public sealed record UsageSnapshot
{
    [JsonPropertyName("provider")]
    public string Provider { get; init; } = "codex";

    [JsonPropertyName("shortTerm")]
    public UsageWindow? ShortTermWindow { get; init; }

    [JsonPropertyName("weekly")]
    public UsageWindow? WeeklyWindow { get; init; }

    [JsonPropertyName("capturedAtUtc")]
    public required DateTimeOffset CapturedAtUtc { get; init; }

    [JsonPropertyName("source")]
    public required string Source { get; init; }

    [JsonPropertyName("freshness")]
    public UsageFreshness Freshness { get; init; } = UsageFreshness.Fresh;

    [JsonPropertyName("isAuthenticated")]
    public bool IsAuthenticated { get; init; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; init; }

    [JsonPropertyName("sanitizedErrorMessage")]
    public string? SanitizedErrorMessage { get; init; }
}
