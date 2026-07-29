using CodexUsageCompanion.Core.Models;

namespace CodexUsageCompanion.Core.Abstractions;

public interface ICodexUsageReader
{
    string SourceName { get; }

    Task<UsageReadResult> ReadAsync(CancellationToken cancellationToken);
}
