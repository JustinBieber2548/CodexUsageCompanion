using System.Diagnostics;

namespace CodexUsageCompanion.Infrastructure.Updates;

public static class UpdateInstallerLauncher
{
    public static ProcessStartInfo CreateAutomaticUpdateStartInfo(string installerPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(installerPath);

        return new ProcessStartInfo(Path.GetFullPath(installerPath))
        {
            UseShellExecute = true,
            // The user has already approved installation in the application. Inno's
            // silent mode keeps a progress window visible while removing the wizard.
            Arguments = "/SILENT /SP- /NORESTART /CLOSEAPPLICATIONS /STARTAPP=1"
        };
    }

    public static Process? StartAutomaticUpdate(string installerPath)
    {
        return Process.Start(CreateAutomaticUpdateStartInfo(installerPath));
    }
}
