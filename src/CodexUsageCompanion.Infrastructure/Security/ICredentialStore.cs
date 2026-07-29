namespace CodexUsageCompanion.Infrastructure.Security;

public interface ICredentialStore
{
    Task SaveAsync(string targetName, string secret, CancellationToken cancellationToken = default);

    Task<string?> ReadAsync(string targetName, CancellationToken cancellationToken = default);

    Task DeleteAsync(string targetName, CancellationToken cancellationToken = default);
}
