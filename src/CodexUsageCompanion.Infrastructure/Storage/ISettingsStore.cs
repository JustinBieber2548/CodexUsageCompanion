using CodexUsageCompanion.Core.Settings;

namespace CodexUsageCompanion.Infrastructure.Storage;

public interface ISettingsStore
{
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}

public interface ILocalStateStore
{
    Task<LocalState> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(LocalState state, CancellationToken cancellationToken = default);
}
