using System.Windows;

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
