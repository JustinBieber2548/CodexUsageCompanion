using System.Text.Json;
using CodexUsageCompanion.Core.Settings;

namespace CodexUsageCompanion.Infrastructure.Storage;

public sealed record UpgradeBackupResult(string BackupDirectory, IReadOnlyList<string> Files);

public sealed class UpgradeBackupService
{
    private readonly ApplicationPaths _paths;

    public UpgradeBackupService(ApplicationPaths paths)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public async Task<UpgradeBackupResult> BackupAndValidateAsync(
        string fromVersion,
        string toVersion,
        CancellationToken cancellationToken = default)
    {
        _paths.EnsureDirectories();
        var settings = await LoadAndValidateSettingsAsync(cancellationToken).ConfigureAwait(false);
        AppSettingsValidator.ThrowIfInvalid(settings);

        var backupName =
            $"{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{SafeSegment(fromVersion)}-to-{SafeSegment(toVersion)}";
        var backupDirectory = Path.Combine(_paths.BackupsDirectory, backupName);
        Directory.CreateDirectory(backupDirectory);

        var copiedFiles = new List<string>();
        foreach (var source in new[] { _paths.SettingsFile, _paths.StateFile })
        {
            if (!File.Exists(source))
            {
                continue;
            }

            var destination = Path.Combine(backupDirectory, Path.GetFileName(source));
            File.Copy(source, destination, false);
            copiedFiles.Add(Path.GetFileName(source));
        }

        var manifest = new
        {
            createdAtUtc = DateTimeOffset.UtcNow,
            fromVersion,
            toVersion,
            files = copiedFiles,
            note = "Credentials remain in Windows Credential Manager and are not copied."
        };
        var manifestPath = Path.Combine(backupDirectory, "manifest.json");
        await File.WriteAllTextAsync(
            manifestPath,
            JsonSerializer.Serialize(manifest, JsonDefaults.Create()),
            cancellationToken).ConfigureAwait(false);
        copiedFiles.Add("manifest.json");
        return new UpgradeBackupResult(backupDirectory, copiedFiles);
    }

    private async Task<AppSettings> LoadAndValidateSettingsAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_paths.SettingsFile))
        {
            return new AppSettings();
        }

        await using var stream = File.OpenRead(_paths.SettingsFile);
        try
        {
            return await JsonSerializer.DeserializeAsync<AppSettings>(
                stream,
                JsonDefaults.Create(),
                cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidDataException("Settings are empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("Settings are invalid; the upgrade was not started.", exception);
        }
    }

    private static string SafeSegment(string value)
    {
        var filtered = new string(value.Where(character =>
                char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_')
            .ToArray());
        return string.IsNullOrWhiteSpace(filtered) ? "unknown" : filtered;
    }
}
