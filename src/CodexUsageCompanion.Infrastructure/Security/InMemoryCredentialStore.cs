using System.Collections.Concurrent;

namespace CodexUsageCompanion.Infrastructure.Security;

public sealed class InMemoryCredentialStore : ICredentialStore
{
    private readonly ConcurrentDictionary<string, string> _credentials =
        new(StringComparer.Ordinal);

    public Task SaveAsync(
        string targetName,
        string secret,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _credentials[targetName] = secret;
        return Task.CompletedTask;
    }

    public Task<string?> ReadAsync(
        string targetName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _credentials.TryGetValue(targetName, out var value);
        return Task.FromResult(value);
    }

    public Task DeleteAsync(
        string targetName,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _credentials.TryRemove(targetName, out _);
        return Task.CompletedTask;
    }
}
