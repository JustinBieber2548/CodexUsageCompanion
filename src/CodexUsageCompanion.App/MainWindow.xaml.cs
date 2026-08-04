using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CodexUsageCompanion.Core.Models;
using Microsoft.Win32;

namespace CodexUsageCompanion.App;

public partial class MainWindow : Window
{
    private readonly DesktopAppController _controller;
    private readonly DispatcherTimer _countdownTimer;

    public MainWindow(DesktopAppController controller)
    {
        _controller = controller;
        InitializeComponent();
        _controller.StateChanged += OnStateChanged;
        _countdownTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, (_, _) => Render(), Dispatcher);
        _countdownTimer.Start();
        Render();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_controller.IsExiting
            && !System.Windows.Application.Current.Dispatcher.HasShutdownStarted)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        _controller.StateChanged -= OnStateChanged;
        _countdownTimer.Stop();
        base.OnClosing(e);
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
        {
            Render();
        }
        else
        {
            Dispatcher.Invoke(Render);
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await _controller.RefreshAsync(true);
    }

    private async void Update_Click(object sender, RoutedEventArgs e)
    {
        UpdateButton.IsEnabled = false;
        try
        {
            await _controller.CheckForUpdatesAsync(true);
        }
        finally
        {
            UpdateButton.IsEnabled = true;
        }
    }

    private void Settings_Click(object sender, RoutedEventArgs e)
    {
        new SettingsWindow(_controller) { Owner = this }.ShowDialog();
    }

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Export safe diagnostics",
            Filter = "JSON file (*.json)|*.json",
            FileName = $"codex-usage-diagnostics-{DateTime.Now:yyyyMMdd-HHmmss}.json",
            AddExtension = true,
            DefaultExt = ".json"
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        await _controller.ExportDiagnosticsAsync(dialog.FileName);
        System.Windows.MessageBox.Show(
            this,
            "The diagnostics file was created without credentials or raw provider responses.",
            "Diagnostics exported",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Clipboard.SetText(_controller.GetDiagnosticSummary());
        System.Windows.MessageBox.Show(
            this,
            "A sanitized diagnostic summary was copied to the clipboard.",
            "Summary copied",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void Render()
    {
        var state = _controller.State;
        var snapshot = state.LatestSnapshot;
        var shortWindow = snapshot?.ShortTermWindow;
        var weeklyWindow = snapshot?.WeeklyWindow;

        ShortWindowNameText.Text = shortWindow?.Name ?? "Short-term window";
        WeeklyWindowNameText.Text = weeklyWindow?.Name ?? "Weekly window";
        RenderWindow(shortWindow, ShortPercentText, ShortProgress, ShortResetText);
        RenderWindow(weeklyWindow, WeeklyPercentText, WeeklyProgress, WeeklyResetText);

        StatusBadgeText.Text = FriendlyStatus(state.CurrentStatus);
        StatusText.Text = state.LastSanitizedErrorMessage
            ?? "Monitoring runs locally and keeps your history across upgrades.";
        StatusBadge.Background = new SolidColorBrush(StatusColor(state.CurrentStatus));
        LastReadText.Text = FormatAge(state.LastSuccessfulReadAtUtc);
        LastSyncText.Text = string.IsNullOrWhiteSpace(_controller.Settings.SupabaseUrl)
            ? "Not configured"
            : FormatAge(state.LastSuccessfulCloudSyncAtUtc);
        DeviceText.Text = _controller.Settings.DeviceLabel;
        SourceText.Text = snapshot?.Source ?? _controller.Settings.ReaderMode.ToString();
        VersionText.Text = DesktopAppController.Version;
        DataPathText.Text = _controller.Paths.RootDirectory;
        DataPathText.ToolTip = _controller.Paths.RootDirectory;
    }

    private void RenderWindow(
        UsageWindow? window,
        System.Windows.Controls.TextBlock percentText,
        System.Windows.Controls.ProgressBar progress,
        System.Windows.Controls.TextBlock resetText)
    {
        var remaining = window?.RemainingPercent;
        percentText.Text = remaining is null ? "—" : $"{remaining:0.#}%";
        progress.Value = (double)(remaining ?? 0);
        resetText.Text = window?.ResetsAtUtc is null
            ? "Reset time unavailable"
            : $"Resets {_controller.FormatReset(window.ResetsAtUtc)} · {FormatCountdown(window.ResetsAtUtc.Value)}";
    }

    private static string FormatAge(DateTimeOffset? value)
    {
        if (value is null)
        {
            return "Never";
        }

        var age = DateTimeOffset.UtcNow - value.Value;
        return age.TotalSeconds < 60
            ? "Just now"
            : age.TotalMinutes < 60
                ? $"{Math.Floor(age.TotalMinutes):0} min ago"
                : $"{Math.Floor(age.TotalHours):0} hr ago";
    }

    private static string FormatCountdown(DateTimeOffset value)
    {
        var remaining = value - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            return "awaiting confirmation";
        }

        if (remaining.TotalDays >= 1)
        {
            return $"{Math.Floor(remaining.TotalDays):0}d {remaining.Hours}h remaining";
        }

        return $"{Math.Floor(remaining.TotalHours):0}h {remaining.Minutes}m {remaining.Seconds}s remaining";
    }

    private static string FriendlyStatus(UsageFreshness status)
    {
        return status switch
        {
            UsageFreshness.Fresh => "Fresh",
            UsageFreshness.Stale => "Stale",
            UsageFreshness.Offline => "Offline",
            UsageFreshness.AuthenticationRequired => "Sign-in needed",
            UsageFreshness.UnsupportedSource => "Source unavailable",
            _ => "Reader problem"
        };
    }

    private static System.Windows.Media.Color StatusColor(UsageFreshness status)
    {
        return status switch
        {
            UsageFreshness.Fresh => System.Windows.Media.Color.FromRgb(5, 150, 105),
            UsageFreshness.AuthenticationRequired => System.Windows.Media.Color.FromRgb(220, 38, 38),
            UsageFreshness.Offline => System.Windows.Media.Color.FromRgb(107, 114, 128),
            _ => System.Windows.Media.Color.FromRgb(217, 119, 6)
        };
    }
}
