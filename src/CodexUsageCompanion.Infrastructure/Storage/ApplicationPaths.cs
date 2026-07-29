namespace CodexUsageCompanion.Infrastructure.Storage;

public sealed class ApplicationPaths
{
    public ApplicationPaths(string? rootOverride = null)
    {
        RootDirectory = string.IsNullOrWhiteSpace(rootOverride)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexUsageCompanion")
            : Path.GetFullPath(rootOverride);
    }

    public string RootDirectory { get; }

    public string SettingsFile => Path.Combine(RootDirectory, "settings.json");

    public string StateFile => Path.Combine(RootDirectory, "state.json");

    public string LogsDirectory => Path.Combine(RootDirectory, "logs");

    public string BackupsDirectory => Path.Combine(RootDirectory, "backups");

    public string UpdatesDirectory => Path.Combine(RootDirectory, "updates");

    public string DiagnosticsDirectory => Path.Combine(RootDirectory, "diagnostics");

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(RootDirectory);
        Directory.CreateDirectory(LogsDirectory);
        Directory.CreateDirectory(BackupsDirectory);
        Directory.CreateDirectory(UpdatesDirectory);
        Directory.CreateDirectory(DiagnosticsDirectory);
    }
}
