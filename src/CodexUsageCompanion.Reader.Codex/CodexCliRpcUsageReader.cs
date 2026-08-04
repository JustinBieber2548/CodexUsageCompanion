using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CodexUsageCompanion.Core.Abstractions;
using CodexUsageCompanion.Core.Models;

namespace CodexUsageCompanion.Reader.Codex;

public sealed class CodexCliRpcUsageReader : ICodexUsageReader
{
    private readonly IClock _clock;
    private readonly CodexCliRpcUsageReaderOptions _options;

    public CodexCliRpcUsageReader(
        IClock clock,
        CodexCliRpcUsageReaderOptions? options = null)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _options = options ?? new CodexCliRpcUsageReaderOptions();
    }

    public string SourceName => "codex-cli-rpc";

    public async Task<UsageReadResult> ReadAsync(CancellationToken cancellationToken)
    {
        Process? process = null;
        try
        {
            process = StartProcess();
            _ = DrainStandardErrorAsync(process, cancellationToken);

            await SendAsync(
                process,
                new
                {
                    id = 1,
                    method = "initialize",
                    @params = new
                    {
                        clientInfo = new
                        {
                            name = "codex-usage-companion",
                            version = "1.0.0"
                        }
                    }
                },
                cancellationToken).ConfigureAwait(false);
            _ = await ReadResponseAsync(
                process,
                1,
                _options.InitializeTimeout,
                cancellationToken).ConfigureAwait(false);

            await SendAsync(
                process,
                new
                {
                    method = "initialized"
                },
                cancellationToken).ConfigureAwait(false);
            await SendAsync(
                process,
                new
                {
                    id = 2,
                    method = "account/rateLimits/read"
                },
                cancellationToken).ConfigureAwait(false);
            using var response = await ReadResponseAsync(
                process,
                2,
                _options.RequestTimeout,
                cancellationToken).ConfigureAwait(false);
            return UsageReadResult.Success(ParseResponse(response.RootElement, _clock.UtcNow));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return UsageReadResult.Failure(
                UsageFreshness.Offline,
                "codex_cli_timeout",
                "The Codex CLI rate-limit request timed out.");
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return UsageReadResult.Failure(
                UsageFreshness.UnsupportedSource,
                "codex_cli_unavailable",
                "A launchable Codex CLI was not found for this Windows account.");
        }
        catch (UnauthorizedAccessException)
        {
            return UsageReadResult.Failure(
                UsageFreshness.UnsupportedSource,
                "codex_cli_access_denied",
                "Windows blocked access to the detected Codex CLI.");
        }
        catch (InvalidDataException exception)
        {
            return UsageReadResult.Failure(
                UsageFreshness.ReaderError,
                "codex_cli_invalid_response",
                exception.Message);
        }
        catch (IOException)
        {
            return UsageReadResult.Failure(
                UsageFreshness.Offline,
                "codex_cli_io_error",
                "Communication with the Codex CLI failed.");
        }
        catch (JsonException)
        {
            return UsageReadResult.Failure(
                UsageFreshness.ReaderError,
                "codex_cli_json_invalid",
                "The Codex CLI returned an unsupported response.");
        }
        finally
        {
            if (process is not null)
            {
                try
                {
                    if (!process.HasExited)
                    {
                        process.Kill(true);
                    }
                }
                catch (InvalidOperationException)
                {
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
    }

    internal static UsageSnapshot ParseResponse(JsonElement response, DateTimeOffset capturedAtUtc)
    {
        if (TryGetProperty(response, "error", out _))
        {
            throw new InvalidDataException("The Codex CLI rejected the rate-limit request.");
        }

        if (!TryGetProperty(response, "result", out var result)
            || !TryGetProperty(result, "rateLimits", out var rateLimits))
        {
            throw new InvalidDataException("The Codex CLI did not return rate limits.");
        }

        UsageWindow? shortTerm = null;
        UsageWindow? weekly = null;
        if (TryGetProperty(rateLimits, "primary", out var primary))
        {
            CodexUsageWindowClassifier.Assign(
                ParseWindow(primary),
                CodexUsageWindowKind.ShortTerm,
                ref shortTerm,
                ref weekly);
        }

        if (TryGetProperty(rateLimits, "secondary", out var secondary))
        {
            CodexUsageWindowClassifier.Assign(
                ParseWindow(secondary),
                CodexUsageWindowKind.Weekly,
                ref shortTerm,
                ref weekly);
        }
        if (shortTerm is null && weekly is null)
        {
            throw new InvalidDataException("The Codex CLI did not return supported usage windows.");
        }

        var snapshot = new UsageSnapshot
        {
            ShortTermWindow = shortTerm,
            WeeklyWindow = weekly,
            CapturedAtUtc = capturedAtUtc,
            Source = "codex-cli-rpc",
            Freshness = UsageFreshness.Fresh,
            IsAuthenticated = true
        };
        UsageSnapshotValidator.ThrowIfInvalid(snapshot);
        return snapshot;
    }

    private Process StartProcess()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _options.ExecutablePath,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        startInfo.ArgumentList.Add("-s");
        startInfo.ArgumentList.Add("read-only");
        startInfo.ArgumentList.Add("-a");
        startInfo.ArgumentList.Add("untrusted");
        startInfo.ArgumentList.Add("app-server");

        var process = new Process
        {
            StartInfo = startInfo
        };
        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidDataException("The Codex CLI did not start.");
        }

        return process;
    }

    private static async Task SendAsync(
        Process process,
        object message,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(message);
        await process.StandardInput.WriteLineAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<JsonDocument> ReadResponseAsync(
        Process process,
        int expectedId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        while (true)
        {
            var line = await process.StandardOutput.ReadLineAsync(timeoutSource.Token).ConfigureAwait(false);
            if (line is null)
            {
                throw new InvalidDataException("The Codex CLI closed before replying.");
            }

            if (Encoding.UTF8.GetByteCount(line) > _options.MaximumLineBytes)
            {
                throw new InvalidDataException("The Codex CLI response was too large.");
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(line);
            }
            catch (JsonException)
            {
                continue;
            }

            if (TryGetProperty(document.RootElement, "id", out var id)
                && id.ValueKind == JsonValueKind.Number
                && id.TryGetInt32(out var parsedId)
                && parsedId == expectedId)
            {
                return document;
            }

            document.Dispose();
        }
    }

    private async Task DrainStandardErrorAsync(
        Process process,
        CancellationToken cancellationToken)
    {
        var total = 0;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    return;
                }

                total += Encoding.UTF8.GetByteCount(line);
                if (total > _options.MaximumLineBytes)
                {
                    return;
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static UsageWindow? ParseWindow(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object
            || !TryReadDecimal(element, "usedPercent", "used_percent", out var used))
        {
            return null;
        }

        if (used is < 0 or > 100)
        {
            throw new InvalidDataException("The Codex CLI returned an invalid usage percentage.");
        }

        _ = TryReadLong(element, "resetsAt", "resets_at", out var resetAt);
        _ = TryReadLong(element, "windowDurationMins", "window_duration_mins", out var durationMinutes);
        var duration = durationMinutes > 0 ? TimeSpan.FromMinutes(durationMinutes) : (TimeSpan?)null;
        return new UsageWindow
        {
            Name = "Usage window",
            RemainingPercent = 100 - used,
            UsedPercent = used,
            ResetsAtUtc = resetAt > 0 ? DateTimeOffset.FromUnixTimeSeconds(resetAt) : null,
            WindowDuration = duration,
            IsConfirmed = true
        };
    }

    private static bool TryGetProperty(
        JsonElement element,
        string name,
        out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out value))
        {
            return true;
        }

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static bool TryReadDecimal(
        JsonElement element,
        string first,
        string second,
        out decimal value)
    {
        if (!(TryGetProperty(element, first, out var property)
            || TryGetProperty(element, second, out property)))
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
        string first,
        string second,
        out long value)
    {
        if (!(TryGetProperty(element, first, out var property)
            || TryGetProperty(element, second, out property)))
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
}
