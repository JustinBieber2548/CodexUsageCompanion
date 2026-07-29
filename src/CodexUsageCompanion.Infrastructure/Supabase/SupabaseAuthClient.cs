using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using CodexUsageCompanion.Infrastructure.Security;
using CodexUsageCompanion.Infrastructure.Storage;

namespace CodexUsageCompanion.Infrastructure.Supabase;

public sealed class SupabaseAuthClient
{
    public const string CredentialTarget = "CodexUsageCompanion/SupabaseSession";
    private readonly HttpClient _httpClient;
    private readonly ICredentialStore _credentialStore;
    private readonly JsonSerializerOptions _jsonOptions = JsonDefaults.Create();

    public SupabaseAuthClient(HttpClient httpClient, ICredentialStore credentialStore)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
    }

    public async Task<SupabaseOperationResult> SignInAsync(
        string supabaseUrl,
        string publishableKey,
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (!TryValidateConfiguration(supabaseUrl, publishableKey, out var baseUri))
        {
            return new SupabaseOperationResult(
                SupabaseOperationStatus.NotConfigured,
                "supabase_not_configured",
                "Supabase requires an HTTPS project URL and publishable key.");
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(baseUri, "/auth/v1/token?grant_type=password"));
        request.Headers.TryAddWithoutValidation("apikey", publishableKey);
        request.Content = JsonContent.Create(new
        {
            email,
            password
        });

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.BadRequest
                or HttpStatusCode.Unauthorized
                or HttpStatusCode.Forbidden)
            {
                return new SupabaseOperationResult(
                    SupabaseOperationStatus.AuthenticationRequired,
                    "supabase_sign_in_rejected",
                    "Supabase rejected the email or password.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return TemporaryFailure((int)response.StatusCode);
            }

            var session = await ParseSessionAsync(response, cancellationToken).ConfigureAwait(false);
            await SaveSessionAsync(session, cancellationToken).ConfigureAwait(false);
            return SupabaseOperationResult.Success();
        }
        catch (HttpRequestException)
        {
            return new SupabaseOperationResult(
                SupabaseOperationStatus.TemporaryFailure,
                "supabase_network_error",
                "Supabase could not be reached.");
        }
        catch (JsonException)
        {
            return InvalidResponse();
        }
    }

    public async Task<SupabaseSession?> LoadSessionAsync(
        CancellationToken cancellationToken = default)
    {
        var json = await _credentialStore.ReadAsync(CredentialTarget, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<SupabaseSession>(json, _jsonOptions);
        }
        catch (JsonException)
        {
            await _credentialStore.DeleteAsync(CredentialTarget, cancellationToken).ConfigureAwait(false);
            return null;
        }
    }

    public async Task<(SupabaseOperationResult Result, SupabaseSession? Session)> EnsureFreshSessionAsync(
        string supabaseUrl,
        string publishableKey,
        CancellationToken cancellationToken = default)
    {
        var current = await LoadSessionAsync(cancellationToken).ConfigureAwait(false);
        if (current is null)
        {
            return (
                new SupabaseOperationResult(
                    SupabaseOperationStatus.AuthenticationRequired,
                    "supabase_sign_in_required",
                    "Sign in to Supabase from Settings."),
                null);
        }

        if (current.ExpiresAtUtc > DateTimeOffset.UtcNow.AddMinutes(2))
        {
            return (SupabaseOperationResult.Success(), current);
        }

        if (!TryValidateConfiguration(supabaseUrl, publishableKey, out var baseUri))
        {
            return (
                new SupabaseOperationResult(
                    SupabaseOperationStatus.NotConfigured,
                    "supabase_not_configured",
                    "Supabase requires an HTTPS project URL and publishable key."),
                null);
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            new Uri(baseUri, "/auth/v1/token?grant_type=refresh_token"));
        request.Headers.TryAddWithoutValidation("apikey", publishableKey);
        request.Content = JsonContent.Create(new
        {
            refresh_token = current.RefreshToken
        });

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode is HttpStatusCode.BadRequest
                or HttpStatusCode.Unauthorized
                or HttpStatusCode.Forbidden)
            {
                await _credentialStore.DeleteAsync(CredentialTarget, cancellationToken).ConfigureAwait(false);
                return (
                    new SupabaseOperationResult(
                        SupabaseOperationStatus.AuthenticationRequired,
                        "supabase_session_expired",
                        "The Supabase session expired. Sign in again."),
                    null);
            }

            if (!response.IsSuccessStatusCode)
            {
                return (TemporaryFailure((int)response.StatusCode), null);
            }

            var refreshed = await ParseSessionAsync(response, cancellationToken).ConfigureAwait(false);
            await SaveSessionAsync(refreshed, cancellationToken).ConfigureAwait(false);
            return (SupabaseOperationResult.Success(), refreshed);
        }
        catch (HttpRequestException)
        {
            return (
                new SupabaseOperationResult(
                    SupabaseOperationStatus.TemporaryFailure,
                    "supabase_network_error",
                    "Supabase could not be reached."),
                null);
        }
        catch (JsonException)
        {
            return (InvalidResponse(), null);
        }
    }

    public Task SignOutAsync(CancellationToken cancellationToken = default)
    {
        return _credentialStore.DeleteAsync(CredentialTarget, cancellationToken);
    }

    private async Task SaveSessionAsync(
        SupabaseSession session,
        CancellationToken cancellationToken)
    {
        var serialized = JsonSerializer.Serialize(session, _jsonOptions);
        await _credentialStore.SaveAsync(CredentialTarget, serialized, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<SupabaseSession> ParseSessionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false),
            cancellationToken: cancellationToken).ConfigureAwait(false);
        var root = document.RootElement;
        var accessToken = root.TryGetProperty("access_token", out var accessElement)
            && accessElement.ValueKind == JsonValueKind.String
            ? accessElement.GetString()
            : null;
        var refreshToken = root.TryGetProperty("refresh_token", out var refreshElement)
            && refreshElement.ValueKind == JsonValueKind.String
            ? refreshElement.GetString()
            : null;
        var expiresIn = root.TryGetProperty("expires_in", out var expiresElement)
            && expiresElement.TryGetInt32(out var seconds)
            ? seconds
            : 3600;
        var userId = root.TryGetProperty("user", out var user)
            && user.TryGetProperty("id", out var id)
            ? id.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(accessToken)
            || string.IsNullOrWhiteSpace(refreshToken)
            || string.IsNullOrWhiteSpace(userId))
        {
            throw new JsonException("Supabase session fields were missing.");
        }

        return new SupabaseSession
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            ExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, expiresIn)),
            UserId = userId
        };
    }

    private static bool TryValidateConfiguration(
        string supabaseUrl,
        string publishableKey,
        out Uri baseUri)
    {
        return Uri.TryCreate(supabaseUrl, UriKind.Absolute, out baseUri!)
            && baseUri.Scheme == Uri.UriSchemeHttps
            && !string.IsNullOrWhiteSpace(publishableKey);
    }

    private static SupabaseOperationResult TemporaryFailure(int statusCode)
    {
        return new SupabaseOperationResult(
            SupabaseOperationStatus.TemporaryFailure,
            $"supabase_http_{statusCode}",
            "Supabase is temporarily unavailable.");
    }

    private static SupabaseOperationResult InvalidResponse()
    {
        return new SupabaseOperationResult(
            SupabaseOperationStatus.InvalidResponse,
            "supabase_invalid_response",
            "Supabase returned an invalid response.");
    }
}
