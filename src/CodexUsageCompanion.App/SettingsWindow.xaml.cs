using System.IO;
using System.Windows;
using CodexUsageCompanion.Core.Settings;

namespace CodexUsageCompanion.App;

public partial class SettingsWindow : Window
{
    private readonly DesktopAppController _controller;

    public SettingsWindow(DesktopAppController controller)
    {
        _controller = controller;
        InitializeComponent();
        ReaderModeBox.ItemsSource = Enum.GetValues<ReaderMode>();
        MockScenarioBox.ItemsSource = Enum.GetValues<MockScenario>();
        LoadSettings();
    }

    private void LoadSettings()
    {
        var settings = _controller.Settings;
        ReaderModeBox.SelectedItem = settings.ReaderMode;
        MockScenarioBox.SelectedItem = settings.MockScenario;
        DeviceLabelBox.Text = settings.DeviceLabel;
        TimeZoneBox.Text = settings.TimeZoneId;
        ActiveMinutesBox.Text = settings.ActiveRefreshMinutes.ToString();
        IdleMinutesBox.Text = settings.IdleRefreshMinutes.ToString();
        StartupBox.IsChecked = settings.StartWithWindows;
        OAuthConsentBox.IsChecked = settings.ExperimentalOAuthConsent;
        WindowsNotificationsBox.IsChecked = settings.WindowsNotificationsEnabled;
        ResetWarningBox.IsChecked = settings.ShortTermResetWarningEnabled;
        ResetDetectedBox.IsChecked = settings.ResetDetectedEnabled;
        EveryChangeBox.IsChecked = settings.EveryUsageChangeEnabled;
        SupabaseUrlBox.Text = settings.SupabaseUrl ?? string.Empty;
        SupabaseKeyBox.Text = settings.SupabasePublishableKey ?? string.Empty;
        EmailBox.Text = settings.SupabaseEmail ?? string.Empty;
        RepositoryBox.Text = settings.GitHubRepository;
        AutoUpdateBox.IsChecked = settings.AutoCheckForUpdates;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        if (await TrySaveAsync())
        {
            DialogResult = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private async void SignIn_Click(object sender, RoutedEventArgs e)
    {
        if (!await TrySaveAsync())
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(EmailBox.Text) || PasswordBox.SecurePassword.Length == 0)
        {
            CloudStatusText.Text = "Enter your email and password. The password is used only for this sign-in request.";
            return;
        }

        CloudStatusText.Text = "Signing in…";
        var result = await _controller.SignInToSupabaseAsync(EmailBox.Text.Trim(), PasswordBox.Password);
        PasswordBox.Clear();
        CloudStatusText.Text = result.IsSuccess
            ? "Signed in. The rotating session is encrypted by Windows Credential Manager."
            : result.SanitizedMessage ?? "Sign-in could not be completed.";
    }

    private async void SignOut_Click(object sender, RoutedEventArgs e)
    {
        await _controller.SignOutFromSupabaseAsync();
        PasswordBox.Clear();
        CloudStatusText.Text = "Signed out and removed the saved cloud session.";
    }

    private async Task<bool> TrySaveAsync()
    {
        if (!int.TryParse(ActiveMinutesBox.Text, out var activeMinutes)
            || !int.TryParse(IdleMinutesBox.Text, out var idleMinutes))
        {
            System.Windows.MessageBox.Show(
                this,
                "Refresh intervals must be whole numbers of minutes.",
                "Check settings",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }

        var settings = _controller.Settings with
        {
            ReaderMode = ReaderModeBox.SelectedItem is ReaderMode reader
                ? reader
                : ReaderMode.Mock,
            MockScenario = MockScenarioBox.SelectedItem is MockScenario scenario
                ? scenario
                : MockScenario.Normal,
            DeviceLabel = DeviceLabelBox.Text.Trim(),
            TimeZoneId = TimeZoneBox.Text.Trim(),
            ActiveRefreshMinutes = activeMinutes,
            IdleRefreshMinutes = idleMinutes,
            StartWithWindows = StartupBox.IsChecked == true,
            ExperimentalOAuthConsent = OAuthConsentBox.IsChecked == true,
            WindowsNotificationsEnabled = WindowsNotificationsBox.IsChecked == true,
            ShortTermResetWarningEnabled = ResetWarningBox.IsChecked == true,
            ResetDetectedEnabled = ResetDetectedBox.IsChecked == true,
            EveryUsageChangeEnabled = EveryChangeBox.IsChecked == true,
            SupabaseUrl = EmptyToNull(SupabaseUrlBox.Text),
            SupabasePublishableKey = EmptyToNull(SupabaseKeyBox.Text),
            SupabaseEmail = EmptyToNull(EmailBox.Text),
            GitHubRepository = RepositoryBox.Text.Trim(),
            AutoCheckForUpdates = AutoUpdateBox.IsChecked == true
        };

        try
        {
            await _controller.SaveSettingsAsync(settings);
            return true;
        }
        catch (Exception exception) when (exception is InvalidDataException
                                              or ArgumentException
                                              or TimeZoneNotFoundException
                                              or InvalidTimeZoneException)
        {
            System.Windows.MessageBox.Show(
                this,
                exception.Message,
                "Check settings",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return false;
        }
    }

    private static string? EmptyToNull(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
