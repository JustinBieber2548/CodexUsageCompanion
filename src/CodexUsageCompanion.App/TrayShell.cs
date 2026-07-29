using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Forms;
using CodexUsageCompanion.Core.Models;

namespace CodexUsageCompanion.App;

public sealed class TrayShell : IDisposable
{
    private readonly DesktopAppController _controller;
    private readonly Window _window;
    private readonly NotifyIcon _icon;
    private readonly ToolStripMenuItem _notifications;
    private readonly ToolStripMenuItem _startup;

    public TrayShell(DesktopAppController controller, Window window)
    {
        _controller = controller;
        _window = window;
        _notifications = new ToolStripMenuItem("Notifications")
        {
            Checked = controller.Settings.WindowsNotificationsEnabled,
            CheckOnClick = true
        };
        _notifications.Click += async (_, _) =>
            await controller.SaveSettingsAsync(
                controller.Settings with
                {
                    WindowsNotificationsEnabled = _notifications.Checked
                });

        _startup = new ToolStripMenuItem("Start with Windows")
        {
            Checked = controller.Settings.StartWithWindows,
            CheckOnClick = true
        };
        _startup.Click += async (_, _) =>
            await controller.SaveSettingsAsync(
                controller.Settings with { StartWithWindows = _startup.Checked });

        var menu = new ContextMenuStrip();
        menu.Items.Add("Codex Usage", null, (_, _) => ShowWindow());
        menu.Items.Add("Refresh now", null, async (_, _) => await controller.RefreshAsync(true));
        menu.Items.Add("Open dashboard / status", null, (_, _) => ShowWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_notifications);
        menu.Items.Add(_startup);
        menu.Items.Add("Settings", null, (_, _) => OpenSettings());
        menu.Items.Add("View logs", null, (_, _) => OpenLogs());
        menu.Items.Add("Check for updates", null, async (_, _) => await controller.CheckForUpdatesAsync(true));
        menu.Items.Add("About", null, (_, _) => ShowAbout());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => controller.Exit());

        _icon = new NotifyIcon
        {
            ContextMenuStrip = menu,
            Icon = CreateIcon(Color.FromArgb(103, 80, 164)),
            Text = "Codex Usage Companion",
            Visible = true
        };
        _icon.DoubleClick += (_, _) => ShowWindow();
        controller.StateChanged += OnStateChanged;
        UpdateIcon();
    }

    public void ShowNotification(AppNotification notification)
    {
        if (NativeToastNotifier.TryShow(notification))
        {
            return;
        }

        _icon.BalloonTipTitle = notification.Title;
        _icon.BalloonTipText = notification.Message;
        _icon.BalloonTipIcon = ToolTipIcon.Info;
        _icon.ShowBalloonTip(5000);
    }

    public void Dispose()
    {
        _controller.StateChanged -= OnStateChanged;
        _icon.Visible = false;
        _icon.Dispose();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (System.Windows.Application.Current.Dispatcher.CheckAccess())
        {
            UpdateIcon();
        }
        else
        {
            System.Windows.Application.Current.Dispatcher.Invoke(UpdateIcon);
        }
    }

    private void UpdateIcon()
    {
        _notifications.Checked = _controller.Settings.WindowsNotificationsEnabled;
        _startup.Checked = _controller.Settings.StartWithWindows;
        var state = _controller.State;
        var shortRemaining = state.LatestSnapshot?.ShortTermWindow?.RemainingPercent;
        var weeklyRemaining = state.LatestSnapshot?.WeeklyWindow?.RemainingPercent;
        var text = CreateTooltip();
        _icon.Text = text.Length > 63 ? text[..63] : text;
        var color = state.CurrentStatus switch
        {
            UsageFreshness.Fresh when (shortRemaining ?? -1) > 50 =>
                Color.FromArgb(5, 150, 105),
            UsageFreshness.Fresh when (shortRemaining ?? -1) >= 20 =>
                Color.FromArgb(234, 179, 8),
            UsageFreshness.Fresh when shortRemaining is not null =>
                Color.FromArgb(220, 38, 38),
            _ => Color.FromArgb(107, 114, 128)
        };
        var oldIcon = _icon.Icon;
        _icon.Icon = CreateIcon(color);
        oldIcon?.Dispose();
    }

    private string CreateTooltip()
    {
        var snapshot = _controller.State.LatestSnapshot;
        if (snapshot?.ShortTermWindow?.RemainingPercent is not { } shortRemaining)
        {
            return $"Codex · Usage unavailable · {_controller.State.CurrentStatus}";
        }

        var weekly = snapshot.WeeklyWindow?.RemainingPercent is { } weeklyRemaining
            ? $"{weeklyRemaining:0.#}%"
            : "n/a";
        var reset = snapshot.ShortTermWindow.ResetsAtUtc is { } resetAt
            ? CompactDuration(resetAt - DateTimeOffset.UtcNow)
            : "n/a";
        var updated = _controller.State.LastSuccessfulReadAtUtc is { } readAt
            ? CompactDuration(DateTimeOffset.UtcNow - readAt) + " ago"
            : "never";
        return $"Codex · Short {shortRemaining:0.#}% · Week {weekly} · Reset {reset} · {updated}";
    }

    private static string CompactDuration(TimeSpan value)
    {
        if (value <= TimeSpan.Zero)
        {
            return "due";
        }

        if (value.TotalHours >= 1)
        {
            return $"{Math.Floor(value.TotalHours):0}h{value.Minutes}m";
        }

        return value.TotalMinutes >= 1
            ? $"{Math.Floor(value.TotalMinutes):0}m"
            : $"{Math.Max(0, Math.Floor(value.TotalSeconds)):0}s";
    }

    private void ShowWindow()
    {
        _window.Show();
        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
    }

    private void OpenSettings()
    {
        var settings = new SettingsWindow(_controller) { Owner = _window };
        settings.ShowDialog();
    }

    private void OpenLogs()
    {
        Directory.CreateDirectory(_controller.Paths.LogsDirectory);
        Process.Start(new ProcessStartInfo(_controller.Paths.LogsDirectory) { UseShellExecute = true });
    }

    private static void ShowAbout()
    {
        System.Windows.MessageBox.Show(
            $"Codex Usage Companion {DesktopAppController.Version}\n\nA local-first Windows tray monitor. Raw Codex responses, passwords, tokens, and cookies are never uploaded or written to logs.\n\nThis independent project is not affiliated with OpenAI or CodexBar.",
            "About Codex Usage Companion",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private static Icon CreateIcon(Color color)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var brush = new SolidBrush(color);
        graphics.FillEllipse(brush, 1, 1, 30, 30);
        using var font = new Font("Segoe UI", 17, System.Drawing.FontStyle.Bold, GraphicsUnit.Pixel);
        using var textBrush = new SolidBrush(Color.White);
        var size = graphics.MeasureString("C", font);
        graphics.DrawString("C", font, textBrush, (32 - size.Width) / 2, (32 - size.Height) / 2 - 1);
        var handle = bitmap.GetHicon();
        try
        {
            using var source = Icon.FromHandle(handle);
            return (Icon)source.Clone();
        }
        finally
        {
            _ = DestroyIcon(handle);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(
        System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
