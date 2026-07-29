using System.Text.Json.Serialization;

namespace CodexUsageCompanion.Infrastructure.Supabase;

public sealed record SupabaseSession
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("refresh_token")]
    public required string RefreshToken { get; init; }

    [JsonPropertyName("expires_at")]
    public required DateTimeOffset ExpiresAtUtc { get; init; }

    [JsonPropertyName("user_id")]
    public required string UserId { get; init; }
}

public enum SupabaseOperationStatus
{
    Success,
    NotConfigured,
    AuthenticationRequired,
    TemporaryFailure,
    InvalidResponse,
    Rejected
}

public sealed record SupabaseOperationResult(
    SupabaseOperationStatus Status,
    string? ErrorCode = null,
    string? SanitizedMessage = null)
{
    public bool IsSuccess => Status == SupabaseOperationStatus.Success;

    public static SupabaseOperationResult Success() => new(SupabaseOperationStatus.Success);
}
