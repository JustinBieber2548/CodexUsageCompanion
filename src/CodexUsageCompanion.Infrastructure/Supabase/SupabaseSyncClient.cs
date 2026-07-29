using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using CodexUsageCompanion.Core.Models;
using CodexUsageCompanion.Core.Settings;

namespace CodexUsageCompanion.Infrastructure.Supabase;

public sealed class SupabaseSyncClient
{
    private readonly HttpClient _httpClient;
    private readonly SupabaseAuthClient _authClient;

    public SupabaseSyncClient(HttpClient httpClient, SupabaseAuthClient authClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _authClient = authClient ?? throw new ArgumentNullException(nameof(authClient));
    }

    public async Task<SupabaseOperationResult> UploadAsync(
        AppSettings settings,
        UsageSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(snapshot);
        UsageSnapshotValidator.ThrowIfInvalid(snapshot);

        return await UploadPayloadAsync(
            settings,
            CreatePayload(settings, snapshot),
            cancellationToken).ConfigureAwait(false);
    }

    public Task<SupabaseOperationResult> UploadStatusAsync(
        AppSettings settings,
        UsageFreshness status,
        string? errorCode,
        string? sanitizedMessage,
        CancellationToken cancellationToken = default)
    {
        var payload = new
        {
            deviceId = settings.DeviceId,
            deviceName = settings.DeviceLabel,
            appVersion = typeof(SupabaseSyncClient).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
            sourceType = "local-reader",
            sourceFreshness = status.ToString().ToLowerInvariant(),
            capturedAtUtc = DateTimeOffset.UtcNow,
            shortTerm = (object?)null,
            weekly = (object?)null,
            errorCode,
            sanitizedErrorMessage = sanitizedMessage
        };
        return UploadPayloadAsync(settings, payload, cancellationToken);
    }

    private async Task<SupabaseOperationResult> UploadPayloadAsync(
        AppSettings settings,
        object payload,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.SupabaseUrl)
            || string.IsNullOrWhiteSpace(settings.SupabasePublishableKey))
        {
            return new SupabaseOperationResult(
                SupabaseOperationStatus.NotConfigured,
                "supabase_not_configured",
                "Supabase synchronization is not configured.");
        }

        var (authResult, session) = await _authClient.EnsureFreshSessionAsync(
            settings.SupabaseUrl,
            settings.SupabasePublishableKey,
            cancellationToken).ConfigureAwait(false);
        if (!authResult.IsSuccess || session is null)
        {
            return authResult;
        }

        var endpoint = new Uri(
            new Uri(settings.SupabaseUrl.TrimEnd('/') + "/", UriKind.Absolute),
            "functions/v1/ingest-usage");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);
        request.Headers.TryAddWithoutValidation("apikey", settings.SupabasePublishableKey);
        request.Content = JsonContent.Create(payload);

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return new SupabaseOperationResult(
                    SupabaseOperationStatus.AuthenticationRequired,
                    "supabase_ingest_authentication_required",
                    "Supabase authentication must be renewed.");
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return new SupabaseOperationResult(
                    SupabaseOperationStatus.TemporaryFailure,
                    "supabase_ingest_rate_limited",
                    "Supabase asked the app to retry later.");
            }

            if ((int)response.StatusCode is >= 500)
            {
                return new SupabaseOperationResult(
                    SupabaseOperationStatus.TemporaryFailure,
                    $"supabase_ingest_http_{(int)response.StatusCode}",
                    "Supabase synchronization is temporarily unavailable.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return new SupabaseOperationResult(
                    SupabaseOperationStatus.Rejected,
                    $"supabase_ingest_http_{(int)response.StatusCode}",
                    "Supabase rejected the normalized usage update.");
            }

            return SupabaseOperationResult.Success();
        }
        catch (HttpRequestException)
        {
            return new SupabaseOperationResult(
                SupabaseOperationStatus.TemporaryFailure,
                "supabase_ingest_network_error",
                "Supabase could not be reached.");
        }
    }

    private static object CreatePayload(AppSettings settings, UsageSnapshot snapshot)
    {
        return new
        {
            deviceId = settings.DeviceId,
            deviceName = settings.DeviceLabel,
            appVersion = typeof(SupabaseSyncClient).Assembly.GetName().Version?.ToString(3) ?? "1.0.0",
            sourceType = snapshot.Source,
            sourceFreshness = snapshot.Freshness.ToString().ToLowerInvariant(),
            capturedAtUtc = snapshot.CapturedAtUtc,
            shortTerm = WindowPayload(snapshot.ShortTermWindow),
            weekly = WindowPayload(snapshot.WeeklyWindow),
            errorCode = (string?)null,
            sanitizedErrorMessage = (string?)null
        };
    }

    private static object? WindowPayload(UsageWindow? window)
    {
        return window is null
            ? null
            : new
            {
                name = window.Name,
                remainingPercent = window.RemainingPercent,
                usedPercent = window.UsedPercent,
                resetAtUtc = window.ResetsAtUtc,
                windowSeconds = window.WindowDuration is null
                    ? (long?)null
                    : (long)window.WindowDuration.Value.TotalSeconds,
                confirmed = window.IsConfirmed,
                estimated = window.IsEstimated
            };
    }
}
