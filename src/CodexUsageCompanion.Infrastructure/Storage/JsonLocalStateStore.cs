using System.Text.Json;

namespace CodexUsageCompanion.Infrastructure.Storage;

public sealed class JsonLocalStateStore : ILocalStateStore
{
    private readonly ApplicationPaths _paths;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public JsonLocalStateStore(ApplicationPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _jsonOptions = JsonDefaults.Create();
    }

    public async Task<LocalState> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _paths.EnsureDirectories();
            if (!File.Exists(_paths.StateFile))
            {
                return new LocalState();
            }

            await using var stream = new FileStream(
                _paths.StateFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            var state = await JsonSerializer.DeserializeAsync<LocalState>(
                stream,
                _jsonOptions,
                cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("Local state is empty.");
            if (state.SchemaVersion is <= 0 or > LocalState.CurrentSchemaVersion)
            {
                throw new InvalidDataException("The local state schema is unsupported.");
            }

            if (state.LatestSnapshot is not null)
            {
                Core.Models.UsageSnapshotValidator.ThrowIfInvalid(state.LatestSnapshot);
            }

            return state;
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Local state contains invalid JSON.", exception);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(LocalState state, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(state);
        if (state.SchemaVersion != LocalState.CurrentSchemaVersion)
        {
            throw new InvalidDataException("The local state schema is unsupported.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _paths.EnsureDirectories();
            var temporaryPath = _paths.StateFile + $".tmp-{Guid.NewGuid():N}";
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
                        state,
                        _jsonOptions,
                        cancellationToken).ConfigureAwait(false);
                    await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                File.Move(temporaryPath, _paths.StateFile, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
