using Microsoft.Win32;
using System.Runtime.Versioning;

namespace CodexUsageCompanion.Infrastructure.Windows;

[SupportedOSPlatform("windows")]
public sealed class StartupManager
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CodexUsageCompanion";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false);
        return key?.GetValue(ValueName) is string value && !string.IsNullOrWhiteSpace(value);
    }

    public void SetEnabled(bool enabled, string executablePath)
    {
        if (!Path.IsPathFullyQualified(executablePath))
        {
            throw new ArgumentException("The executable path must be absolute.", nameof(executablePath));
        }

        using var key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true);
        if (enabled)
        {
            key.SetValue(ValueName, $"\"{executablePath}\" --background", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(ValueName, false);
        }
    }
}
