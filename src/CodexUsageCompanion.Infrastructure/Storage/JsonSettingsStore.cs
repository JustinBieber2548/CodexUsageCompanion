using System.Text.Json;
using CodexUsageCompanion.Core.Settings;

namespace CodexUsageCompanion.Infrastructure.Storage;

public sealed class JsonSettingsStore : ISettingsStore
{
    private readonly ApplicationPaths _paths;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonSettingsStore(ApplicationPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _jsonOptions = JsonDefaults.Create();
    }

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _paths.EnsureDirectories();
            if (!File.Exists(_paths.SettingsFile))
            {
                var defaults = new AppSettings();
                await SaveInternalAsync(defaults, cancellationToken).ConfigureAwait(false);
                return defaults;
            }

            await using var stream = new FileStream(
                _paths.SettingsFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(
                stream,
                _jsonOptions,
                cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("Settings are empty.");
            AppSettingsValidator.ThrowIfInvalid(settings);
            return settings;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Settings contain invalid JSON.", exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(
        AppSettings settings,
        CancellationToken cancellationToken = default)
    {
        AppSettingsValidator.ThrowIfInvalid(settings);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _paths.EnsureDirectories();
            await SaveInternalAsync(settings, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task SaveInternalAsync(
        AppSettings settings,
        CancellationToken cancellationToken)
    {
        var temporaryPath = _paths.SettingsFile + $".tmp-{Guid.NewGuid():N}";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    settings,
                    _jsonOptions,
                    cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporaryPath, _paths.SettingsFile, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }
}
