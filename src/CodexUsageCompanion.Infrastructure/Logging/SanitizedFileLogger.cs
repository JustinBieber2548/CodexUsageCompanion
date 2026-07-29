using System.Text.Json;
using CodexUsageCompanion.Infrastructure.Storage;

namespace CodexUsageCompanion.Infrastructure.Logging;

public enum LogLevel
{
    Debug,
    Information,
    Warning,
    Error
}

public sealed class SanitizedFileLogger
{
    private const long MaximumLogBytes = 2 * 1024 * 1024;
    private readonly ApplicationPaths _paths;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SanitizedFileLogger(ApplicationPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public async Task WriteAsync(
        LogLevel level,
        string eventName,
        string message,
        IReadOnlyDictionary<string, object?>? properties = null,
        CancellationToken cancellationToken = default)
    {
        _paths.EnsureDirectories();
        var entry = new
        {
            timestampUtc = DateTimeOffset.UtcNow,
            level = level.ToString(),
            eventName = SensitiveDataRedactor.Redact(eventName),
            message = SensitiveDataRedactor.Redact(message),
            properties = properties?.ToDictionary(
                pair => pair.Key,
                pair => (object?)SensitiveDataRedactor.Redact(pair.Value?.ToString()))
        };
        var json = JsonSerializer.Serialize(entry, JsonDefaults.Create()).ReplaceLineEndings(string.Empty);
        var logPath = Path.Combine(_paths.LogsDirectory, "application.log");

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            RotateIfNeeded(logPath);
            await File.AppendAllTextAsync(logPath, json + Environment.NewLine, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static void RotateIfNeeded(string logPath)
    {
        if (!File.Exists(logPath) || new FileInfo(logPath).Length < MaximumLogBytes)
        {
            return;
        }

        for (var index = 3; index >= 1; index--)
        {
            var current = $"{logPath}.{index}";
            var next = $"{logPath}.{index + 1}";
            if (File.Exists(current))
            {
                File.Move(current, next, true);
            }
        }

        File.Move(logPath, $"{logPath}.1", true);
    }
}
