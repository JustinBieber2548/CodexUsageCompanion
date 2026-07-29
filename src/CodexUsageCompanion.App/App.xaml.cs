using System.Windows;
using CodexUsageCompanion.Infrastructure.Storage;

namespace CodexUsageCompanion.App;

public partial class App : System.Windows.Application
{
    private DesktopAppController? _controller;
    private TrayShell? _tray;
    private MainWindow? _mainWindow;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var prepareIndex = Array.FindIndex(
                e.Args,
                argument => string.Equals(
                    argument,
                    "--prepare-upgrade",
                    StringComparison.OrdinalIgnoreCase));
            if (prepareIndex >= 0)
            {
                var targetVersion = prepareIndex + 1 < e.Args.Length
                    ? e.Args[prepareIndex + 1]
                    : "unknown";
                await new UpgradeBackupService(new ApplicationPaths())
                    .BackupAndValidateAsync(DesktopAppController.Version, targetVersion);
                Shutdown(0);
                return;
            }

            _controller = await DesktopAppController.CreateAsync();
            _mainWindow = new MainWindow(_controller);
            _tray = new TrayShell(_controller, _mainWindow);
            _controller.NotificationRaised += OnNotificationRaised;
            await _controller.StartAsync();

            if (!e.Args.Contains("--background", StringComparer.OrdinalIgnoreCase))
            {
                _mainWindow.Show();
                _mainWindow.Activate();
            }
        }
        catch (Exception exception)
        {
            if (e.Args.Contains("--prepare-upgrade", StringComparer.OrdinalIgnoreCase))
            {
                Shutdown(2);
                return;
            }

            System.Windows.MessageBox.Show(
                $"Codex Usage Companion could not start.\n\n{exception.Message}",
                "Startup problem",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_controller is not null)
        {
            _controller.NotificationRaised -= OnNotificationRaised;
        }

        _tray?.Dispose();
        _controller?.Dispose();
        base.OnExit(e);
    }

    private void OnNotificationRaised(object? sender, AppNotification notification)
    {
        Dispatcher.Invoke(() => _tray?.ShowNotification(notification));
    }
}
