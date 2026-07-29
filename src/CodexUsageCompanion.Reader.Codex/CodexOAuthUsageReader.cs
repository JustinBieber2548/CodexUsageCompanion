using System.Buffers;
using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using CodexUsageCompanion.Core.Abstractions;
using CodexUsageCompanion.Core.Models;

namespace CodexUsageCompanion.Reader.Codex;

public sealed class CodexOAuthUsageReader : ICodexUsageReader, IDisposable
{
    private static readonly Uri UsageEndpoint =
        new("https://chatgpt.com/backend-api/wham/usage", UriKind.Absolute);

    private readonly HttpClient _httpClient;
    private readonly bool _disposeHttpClient;
    private readonly IClock _clock;
    private readonly CodexOAuthUsageReaderOptions _options;

    public CodexOAuthUsageReader(
        IClock clock,
        CodexOAuthUsageReaderOptions? options = null)
        : this(CreateSecureHandler(), clock, options, true)
    {
    }

    public CodexOAuthUsageReader(
        HttpMessageHandler handler,
        IClock clock,
        CodexOAuthUsageReaderOptions? options = null)
        : this(handler, clock, options, true)
    {
    }

    private CodexOAuthUsageReader(
        HttpMessageHandler handler,
        IClock clock,
        CodexOAuthUsageReaderOptions? options,
        bool disposeHandler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? new CodexOAuthUsageReaderOptions();
        _httpClient = new HttpClient(handler, disposeHandler)
        {
            Timeout = _options.Timeout
        };
        _disposeHttpClient = true;
    }

    public string SourceName => "codexbar-compatible-oauth";

    public async Task<UsageReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        LocalCodexCredentials credentials;
        try
        {
            credentials = await LoadCredentialsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            return Failure(
                UsageFreshness.AuthenticationRequired,
                "codex_auth_not_found",
                "Codex authentication was not found. Open Codex and sign in.");
        }
        catch (UnauthorizedAccessException)
        {
            return Failure(
                UsageFreshness.AuthenticationRequired,
                "codex_auth_unreadable",
                "Codex authentication cannot be read by this Windows account.");
        }
        catch (InvalidDataException exception)
        {
            return Failure(
                UsageFreshness.UnsupportedSource,
                "codex_auth_invalid",
                exception.Message);
        }
        catch (IOException)
        {
            return Failure(
                UsageFreshness.ReaderError,
                "codex_auth_io_error",
                "Codex authentication could not be read safely.");
        }
        catch (JsonException)
        {
            return Failure(
                UsageFreshness.UnsupportedSource,
                "codex_auth_json_invalid",
                "Codex authentication has an unsupported format.");
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, UsageEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        request.Headers.UserAgent.ParseAdd("CodexUsageCompanion/1.0");
        if (!string.IsNullOrWhiteSpace(credentials.AccountId))
        {
            request.Headers.TryAddWithoutValidation("ChatGPT-Account-Id", credentials.AccountId);
        }

        try
        {
            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                return Failure(
                    UsageFreshness.AuthenticationRequired,
                    "codex_authentication_required",
                    "Codex authentication expired or was rejected. Sign in through Codex.");
            }

            if ((int)response.StatusCode is >= 300 and < 400)
            {
                return Failure(
                    UsageFreshness.ReaderError,
                    "codex_redirect_rejected",
                    "The Codex usage service returned an unexpected redirect.");
            }

            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return Failure(
                    UsageFreshness.Offline,
                    "codex_rate_limited",
                    "The Codex usage service asked the app to slow down.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return Failure(
                    UsageFreshness.Offline,
                    $"codex_http_{(int)response.StatusCode}",
                    "The Codex usage service is temporarily unavailable.");
            }

            var payload = await ReadBoundedPayloadAsync(response, cancellationToken).ConfigureAwait(false);
            var snapshot = ParseUsage(payload, _clock.UtcNow);
            return UsageReadResult.Success(snapshot);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Failure(
                UsageFreshness.Offline,
                "codex_request_timeout",
                "The Codex usage request timed out.");
        }
        catch (HttpRequestException)
        {
            return Failure(
                UsageFreshness.Offline,
                "codex_network_error",
                "The Codex usage service could not be reached.");
        }
        catch (InvalidDataException exception)
        {
            return Failure(
                UsageFreshness.ReaderError,
                "codex_response_invalid",
                exception.Message);
        }
        catch (JsonException)
        {
            return Failure(
                UsageFreshness.ReaderError,
                "codex_response_json_invalid",
                "The Codex usage response has an unsupported format.");
        }
    }

    public void Dispose()
    {
        if (_disposeHttpClient)
        {
            _httpClient.Dispose();
        }
    }

    internal static UsageSnapshot ParseUsage(ReadOnlyMemory<byte> payload, DateTimeOffset capturedAtUtc)
    {
        using var document = JsonDocument.Parse(payload);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !TryGetProperty(document.RootElement, "rate_limit", out var rateLimit)
            || rateLimit.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidDataException("Codex did not return supported usage windows.");
        }

        UsageWindow? shortTerm = null;
        UsageWindow? weekly = null;
        if (TryGetProperty(rateLimit, "primary_window", out var primary)
            && primary.ValueKind == JsonValueKind.Object)
        {
            shortTerm = ParseWindow(primary, false);
        }

        if (TryGetProperty(rateLimit, "secondary_window", out var secondary)
            && secondary.ValueKind == JsonValueKind.Object)
        {
            weekly = ParseWindow(secondary, true);
        }

        if (shortTerm is null && weekly is null)
        {
            throw new InvalidDataException("Codex did not return supported usage windows.");
        }

        var snapshot = new UsageSnapshot
        {
            ShortTermWindow = shortTerm,
            WeeklyWindow = weekly,
            CapturedAtUtc = capturedAtUtc,
            Source = "codexbar-compatible-oauth",
            Freshness = UsageFreshness.Fresh,
            IsAuthenticated = true
        };
        UsageSnapshotValidator.ThrowIfInvalid(snapshot);
        return snapshot;
    }

    private async Task<LocalCodexCredentials> LoadCredentialsAsync(
        CancellationToken cancellationToken)
    {
        var authPath = ResolveAuthPath();
        var info = new FileInfo(authPath);
        if (!info.Exists)
        {
            throw new FileNotFoundException("Codex authentication was not found.", authPath);
        }

        if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
        {
            throw new InvalidDataException("Codex authentication cannot be read through a file-system link.");
        }

        if (info.Length <= 0 || info.Length > _options.MaximumAuthFileBytes)
        {
            throw new InvalidDataException("Codex authentication has an unexpected size.");
        }

        await using var stream = new FileStream(
            authPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            4096,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var document = await JsonDocument.ParseAsync(
            stream,
            new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16
            },
            cancellationToken).ConfigureAwait(false);

        if (!TryGetProperty(document.RootElement, "tokens", out var tokens)
            || tokens.ValueKind != JsonValueKind.Object
            || !TryReadString(tokens, "access_token", "accessToken", out var accessToken))
        {
            throw new InvalidDataException("Codex OAuth tokens are unavailable. Sign in through Codex.");
        }

        _ = TryReadString(tokens, "account_id", "accountId", out var accountId);
        return new LocalCodexCredentials(accessToken, accountId);
    }

    private string ResolveAuthPath()
    {
        var configuredHome = _options.CodexHomePath;
        var codexHome = string.IsNullOrWhiteSpace(configuredHome)
            ? Environment.GetEnvironmentVariable("CODEX_HOME")
            : configuredHome;

        if (string.IsNullOrWhiteSpace(codexHome))
        {
            codexHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".codex");
        }

        if (!Path.IsPathFullyQualified(codexHome))
        {
            throw new InvalidDataException("The configured Codex home must be an absolute local path.");
        }

        var fullHome = Path.GetFullPath(codexHome);
        if (new Uri(fullHome).IsUnc)
        {
            throw new InvalidDataException("A network Codex home is not supported.");
        }

        var root = Path.GetPathRoot(fullHome);
        if (!string.IsNullOrWhiteSpace(root)
            && new DriveInfo(root).DriveType == DriveType.Network)
        {
            throw new InvalidDataException("A network Codex home is not supported.");
        }

        for (var directory = new DirectoryInfo(fullHome);
             directory is not null;
             directory = directory.Parent)
        {
            if (directory.Exists
                && (directory.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidDataException(
                    "Codex authentication cannot be read through a linked directory.");
            }
        }

        return Path.Combine(fullHome, "auth.json");
    }

    private async Task<byte[]> ReadBoundedPayloadAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        if (response.Content.Headers.ContentLength is > 0
            && response.Content.Headers.ContentLength > _options.MaximumResponseBytes)
        {
            throw new InvalidDataException("The Codex usage response was too large.");
        }

        await using var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var destination = new MemoryStream();
        var buffer = ArrayPool<byte>.Shared.Rent(8192);
        try
        {
            while (true)
            {
                var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                    .ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                if (destination.Length + read > _options.MaximumResponseBytes)
                {
                    throw new InvalidDataException("The Codex usage response was too large.");
                }

                await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            }

            return destination.ToArray();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, true);
        }
    }

    private static UsageWindow ParseWindow(JsonElement element, bool weekly)
    {
        if (!TryReadDecimal(element, "used_percent", "usedPercent", out var used)
            || used is < 0 or > 100)
        {
            throw new InvalidDataException("Codex returned an invalid usage percentage.");
        }

        _ = TryReadLong(element, "reset_at", "resetsAt", out var resetAt);
        _ = TryReadLong(element, "limit_window_seconds", "limitWindowSeconds", out var durationSeconds);
        var duration = durationSeconds > 0 ? TimeSpan.FromSeconds(durationSeconds) : (TimeSpan?)null;

        return new UsageWindow
        {
            Name = weekly
                ? "Weekly window"
                : duration == TimeSpan.FromHours(5) ? "5-hour window" : "Short-term window",
            RemainingPercent = 100 - used,
            UsedPercent = used,
            ResetsAtUtc = resetAt > 0 ? DateTimeOffset.FromUnixTimeSeconds(resetAt) : null,
            WindowDuration = duration,
            IsConfirmed = true
        };
    }

    private static bool TryGetProperty(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        if (element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static bool TryReadString(
        JsonElement element,
        string snakeCase,
        string camelCase,
        out string value)
    {
        if ((TryGetProperty(element, snakeCase, out var property)
                || TryGetProperty(element, camelCase, out property))
            && property.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(property.GetString()))
        {
            value = property.GetString()!;
            return true;
        }

        value = string.Empty;
        return false;
    }

    private static bool TryReadDecimal(
        JsonElement element,
        string snakeCase,
        string camelCase,
        out decimal value)
    {
        if (!(TryGetProperty(element, snakeCase, out var property)
            || TryGetProperty(element, camelCase, out property)))
        {
            value = default;
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out value))
        {
            return true;
        }

        value = default;
        return property.ValueKind == JsonValueKind.String
            && decimal.TryParse(
                property.GetString(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out value);
    }

    private static bool TryReadLong(
        JsonElement element,
        string snakeCase,
        string camelCase,
        out long value)
    {
        if (!(TryGetProperty(element, snakeCase, out var property)
            || TryGetProperty(element, camelCase, out property)))
        {
            value = default;
            return false;
        }

        if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out value))
        {
            return true;
        }

        value = default;
        return property.ValueKind == JsonValueKind.String
            && long.TryParse(property.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static UsageReadResult Failure(
        UsageFreshness status,
        string errorCode,
        string message)
    {
        return UsageReadResult.Failure(status, errorCode, message);
    }

    private static HttpMessageHandler CreateSecureHandler()
    {
        return new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
    }

    private sealed record LocalCodexCredentials(string AccessToken, string? AccountId);
}
