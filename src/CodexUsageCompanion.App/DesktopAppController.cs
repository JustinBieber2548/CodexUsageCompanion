using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using CodexUsageCompanion.Core.Abstractions;
using CodexUsageCompanion.Core.Models;
using CodexUsageCompanion.Core.Notifications;
using CodexUsageCompanion.Core.Readers;
using CodexUsageCompanion.Core.Settings;
using CodexUsageCompanion.Core.Versioning;
using CodexUsageCompanion.Infrastructure.Logging;
using CodexUsageCompanion.Infrastructure.Security;
using CodexUsageCompanion.Infrastructure.Storage;
using CodexUsageCompanion.Infrastructure.Supabase;
using CodexUsageCompanion.Infrastructure.Updates;
using CodexUsageCompanion.Infrastructure.Windows;
using CodexUsageCompanion.Reader.Codex;

namespace CodexUsageCompanion.App;

public sealed record AppNotification(string Title, string Message);

public sealed class DesktopAppController : IDisposable
{
    private readonly ApplicationPaths _paths;
    private readonly JsonSettingsStore _settingsStore;
    private readonly JsonLocalStateStore _stateStore;
    private readonly SanitizedFileLogger _logger;
    private readonly SupabaseAuthClient _authClient;
    private readonly SupabaseSyncClient _syncClient;
    private readonly HttpClient _httpClient;
    private readonly NotificationEngine _notificationEngine = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly CancellationTokenSource _shutdown = new();
    private System.Threading.Timer? _timer;
    private ICodexUsageReader _reader;
    private bool _disposed;

    private DesktopAppController(
        ApplicationPaths paths,
        JsonSettingsStore settingsStore,
        JsonLocalStateStore stateStore,
        SanitizedFileLogger logger,
        SupabaseAuthClient authClient,
        SupabaseSyncClient syncClient,
        HttpClient httpClient,
        AppSettings settings,
        LocalState state)
    {
        _paths = paths;
        _settingsStore = settingsStore;
        _stateStore = stateStore;
        _logger = logger;
        _authClient = authClient;
        _syncClient = syncClient;
        _httpClient = httpClient;
        Settings = settings;
        State = state;
        _reader = CreateReader(settings);
    }

    public event EventHandler? StateChanged;

    public event EventHandler<AppNotification>? NotificationRaised;

    public AppSettings Settings { get; private set; }

    public LocalState State { get; private set; }

    public ApplicationPaths Paths => _paths;

    public static string Version =>
        Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

    public static async Task<DesktopAppController> CreateAsync()
    {
        var paths = new ApplicationPaths();
        paths.EnsureDirectories();
        var settingsStore = new JsonSettingsStore(paths);
        var stateStore = new JsonLocalStateStore(paths);
        var logger = new SanitizedFileLogger(paths);
        var credentials = new WindowsCredentialStore();
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var authClient = new SupabaseAuthClient(httpClient, credentials);
        var syncClient = new SupabaseSyncClient(httpClient, authClient);
        var settings = await settingsStore.LoadAsync();
        var state = await stateStore.LoadAsync();
        return new DesktopAppController(
            paths,
            settingsStore,
            stateStore,
            logger,
            authClient,
            syncClient,
            httpClient,
            settings,
            state);
    }

    public async Task StartAsync()
    {
        await _logger.WriteAsync(
            LogLevel.Information,
            "application_started",
            $"Codex Usage Companion {Version} started.");
        _timer = new System.Threading.Timer(
            _ => _ = RefreshAsync(false),
            null,
            TimeSpan.Zero,
            CurrentRefreshInterval());

        if (UpdatePolicy.ShouldAutomaticallyCheck(
                Settings.AutoCheckForUpdates,
                State.LastUpdateCheckAtUtc,
                DateTimeOffset.UtcNow))
        {
            _ = CheckForUpdatesAsync(false);
        }
    }

    public async Task RefreshAsync(bool userInitiated)
    {
        if (_shutdown.IsCancellationRequested || !await _refreshGate.WaitAsync(0))
        {
            return;
        }

        try
        {
            var now = DateTimeOffset.UtcNow;
            var result = await _reader.ReadAsync(_shutdown.Token);
            if (result.IsSuccess && result.Snapshot is not null)
            {
                var snapshot = result.Snapshot;
                var freshness = FreshnessEvaluator.Evaluate(
                    snapshot,
                    now,
                    TimeSpan.FromMinutes(Settings.StaleAfterMinutes));
                snapshot = snapshot with { Freshness = freshness };
                State = State.WithSuccessfulSnapshot(snapshot, now);
                EvaluateNotifications(snapshot, now);
                await _stateStore.SaveAsync(State, _shutdown.Token);
                await SynchronizePendingAsync();
                await _logger.WriteAsync(
                    LogLevel.Information,
                    "usage_read_succeeded",
                    $"A {snapshot.Freshness} usage snapshot was read from {snapshot.Source}.",
                    cancellationToken: _shutdown.Token);
            }
            else
            {
                State = State.WithReadFailure(
                    result.Status,
                    result.ErrorCode,
                    result.SanitizedErrorMessage);
                EvaluateNotifications(null, now);
                await _stateStore.SaveAsync(State, _shutdown.Token);
                await UploadFailureStatusAsync(result);
                await _logger.WriteAsync(
                    LogLevel.Warning,
                    "usage_read_failed",
                    result.SanitizedErrorMessage ?? "The local usage reader failed.",
                    new Dictionary<string, object?> { ["errorCode"] = result.ErrorCode },
                    _shutdown.Token);

                if (userInitiated)
                {
                    RaiseNotification(
                        "Refresh did not complete",
                        result.SanitizedErrorMessage ?? "Usage is temporarily unavailable.");
                }
            }
        }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            await _logger.WriteAsync(
                LogLevel.Error,
                "refresh_unhandled_error",
                exception.Message);
        }
        finally
        {
            _refreshGate.Release();
            _timer?.Change(CurrentRefreshInterval(), CurrentRefreshInterval());
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public async Task SaveSettingsAsync(AppSettings settings)
    {
        AppSettingsValidator.ThrowIfInvalid(settings);
        var readerChanged = settings.ReaderMode != Settings.ReaderMode
            || settings.MockScenario != Settings.MockScenario
            || settings.ExperimentalOAuthConsent != Settings.ExperimentalOAuthConsent;
        Settings = settings;
        await _settingsStore.SaveAsync(settings, _shutdown.Token);

        if (OperatingSystem.IsWindows())
        {
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("The application path is unavailable.");
            new StartupManager().SetEnabled(settings.StartWithWindows, executable);
        }

        if (readerChanged)
        {
            if (_reader is IDisposable disposable)
            {
                disposable.Dispose();
            }

            _reader = CreateReader(settings);
        }

        _timer?.Change(TimeSpan.Zero, CurrentRefreshInterval());
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public Task<SupabaseOperationResult> SignInToSupabaseAsync(
        string email,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(Settings.SupabaseUrl)
            || string.IsNullOrWhiteSpace(Settings.SupabasePublishableKey))
        {
            return Task.FromResult(new SupabaseOperationResult(
                SupabaseOperationStatus.NotConfigured,
                "supabase_not_configured",
                "Save a Supabase URL and publishable key first."));
        }

        return _authClient.SignInAsync(
            Settings.SupabaseUrl,
            Settings.SupabasePublishableKey,
            email,
            password,
            cancellationToken);
    }

    public Task SignOutFromSupabaseAsync()
    {
        return _authClient.SignOutAsync(_shutdown.Token);
    }

    public async Task<UpdateCheckResult> CheckForUpdatesAsync(bool userInitiated)
    {
        var client = new GitHubReleaseClient(_httpClient);
        var result = await client.CheckAsync(
            Settings.GitHubRepository,
            SemanticVersion.Parse(Version),
            _shutdown.Token);
        State = State with { LastUpdateCheckAtUtc = DateTimeOffset.UtcNow };
        await _stateStore.SaveAsync(State, _shutdown.Token);
        StateChanged?.Invoke(this, EventArgs.Empty);

        if (result.Status == UpdateCheckStatus.UpdateAvailable && result.Release is not null)
        {
            if (userInitiated)
            {
                await OfferUpdateAsync(result.Release);
            }
            else
            {
                RaiseNotification(
                    $"Version {result.Release.Version} is available",
                    "Open Check for updates when you are ready to review and install it.");
            }
        }
        else if (userInitiated)
        {
            var message = result.Status == UpdateCheckStatus.UpToDate
                ? $"Version {Version} is current."
                : result.SanitizedMessage ?? "The update check could not be completed.";
            System.Windows.MessageBox.Show(
                message,
                "Check for updates",
                System.Windows.MessageBoxButton.OK,
                result.Status == UpdateCheckStatus.UpToDate
                    ? System.Windows.MessageBoxImage.Information
                    : System.Windows.MessageBoxImage.Warning);
        }

        return result;
    }

    public string FormatReset(DateTimeOffset? utc)
    {
        if (utc is null)
        {
            return "Unknown";
        }

        try
        {
            return TimeZoneDisplay.ConvertFromUtc(utc.Value, Settings.TimeZoneId)
                .ToString("ddd, MMM d h:mm tt");
        }
        catch (TimeZoneNotFoundException)
        {
            return utc.Value.LocalDateTime.ToString("ddd, MMM d h:mm tt");
        }
    }

    public async Task ExportDiagnosticsAsync(string destinationFile)
    {
        var payload = new
        {
            generatedAtUtc = DateTimeOffset.UtcNow,
            appVersion = Version,
            status = State.CurrentStatus.ToString(),
            lastSuccessfulReadAtUtc = State.LastSuccessfulReadAtUtc,
            lastCloudSyncAtUtc = State.LastSuccessfulCloudSyncAtUtc,
            source = State.LatestSnapshot?.Source,
            deviceLabel = Settings.DeviceLabel,
            localDataDirectory = _paths.RootDirectory,
            note = "Tokens, passwords, cookies, and raw provider responses are intentionally excluded."
        };
        var json = System.Text.Json.JsonSerializer.Serialize(
            payload,
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(destinationFile, json, _shutdown.Token);
    }

    public void Exit()
    {
        System.Windows.Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _shutdown.Cancel();
        _timer?.Dispose();
        if (_reader is IDisposable disposable)
        {
            disposable.Dispose();
        }

        _httpClient.Dispose();
        _shutdown.Dispose();
        _refreshGate.Dispose();
    }

    private void EvaluateNotifications(UsageSnapshot? snapshot, DateTimeOffset now)
    {
        var evaluation = _notificationEngine.Evaluate(
            snapshot,
            State.NotificationState,
            NotificationPreferences.FromSettings(Settings),
            now,
            State.LastSuccessfulReadAtUtc,
            State.CurrentStatus);
        State = State with { NotificationState = evaluation.State };
        if (!Settings.WindowsNotificationsEnabled)
        {
            return;
        }

        foreach (var notification in evaluation.Events)
        {
            RaiseNotification(notification.Title, notification.Message);
        }
    }

    private async Task SynchronizePendingAsync()
    {
        var pending = State.PendingSynchronization;
        if (pending is null
            || (pending.NextAttemptAtUtc is not null
                && pending.NextAttemptAtUtc > DateTimeOffset.UtcNow))
        {
            return;
        }

        var result = await _syncClient.UploadAsync(Settings, pending.Snapshot, _shutdown.Token);
        if (result.IsSuccess)
        {
            State = State with
            {
                PendingSynchronization = null,
                LastSuccessfulCloudSyncAtUtc = DateTimeOffset.UtcNow
            };
        }
        else if (result.Status == SupabaseOperationStatus.TemporaryFailure)
        {
            var attempts = pending.AttemptCount + 1;
            State = State with
            {
                PendingSynchronization = pending with
                {
                    AttemptCount = attempts,
                    NextAttemptAtUtc = DateTimeOffset.UtcNow.Add(
                        RetrySchedule.Calculate(attempts, Random.Shared.NextDouble()))
                }
            };
        }

        await _stateStore.SaveAsync(State, _shutdown.Token);
    }

    private async Task UploadFailureStatusAsync(UsageReadResult result)
    {
        if (string.IsNullOrWhiteSpace(Settings.SupabaseUrl))
        {
            return;
        }

        _ = await _syncClient.UploadStatusAsync(
            Settings,
            result.Status,
            result.ErrorCode,
            result.SanitizedErrorMessage,
            _shutdown.Token);
    }

    private async Task OfferUpdateAsync(GitHubReleaseInfo release)
    {
        var notes = release.ReleaseNotes.Length > 1800
            ? release.ReleaseNotes[..1800] + "\n\n…"
            : release.ReleaseNotes;
        var accepted = System.Windows.MessageBox.Show(
            $"Version {release.Version} is available.\n\n{notes}\n\nDownload this update?",
            "Codex Usage Companion update",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Information);
        if (accepted != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        var asset = UpdateDownloader.SelectWindowsInstaller(release);
        if (asset is null)
        {
            System.Windows.MessageBox.Show(
                "This release does not contain a supported Windows installer.",
                "Update unavailable",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        var download = await new UpdateDownloader(_httpClient, _paths)
            .DownloadAsync(release, asset, _shutdown.Token);
        if (!download.IsSuccess || download.FilePath is null)
        {
            System.Windows.MessageBox.Show(
                download.SanitizedMessage ?? "The update could not be downloaded.",
                "Update unavailable",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return;
        }

        var backup = await new UpgradeBackupService(_paths).BackupAndValidateAsync(
            Version,
            release.Version.ToString(),
            _shutdown.Token);
        var install = System.Windows.MessageBox.Show(
            $"The update passed its SHA-256 check and your local data was backed up.\n\nBackup: {backup.BackupDirectory}\n\nStart the installer now?",
            "Ready to install",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        if (install != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        Process.Start(new ProcessStartInfo(download.FilePath) { UseShellExecute = true });
        Exit();
    }

    private static ICodexUsageReader CreateReader(AppSettings settings)
    {
        return settings.ReaderMode switch
        {
            ReaderMode.ExperimentalOAuth when settings.ExperimentalOAuthConsent =>
                new CodexOAuthUsageReader(new SystemClock()),
            ReaderMode.ExperimentalOAuth => new ConsentRequiredReader(),
            ReaderMode.CliRpc => new CodexCliRpcUsageReader(new SystemClock()),
            _ => new MockCodexUsageReader(new SystemClock(), settings.MockScenario)
        };
    }

    private TimeSpan CurrentRefreshInterval()
    {
        var minutes = IsCodexActive()
            ? Settings.ActiveRefreshMinutes
            : Settings.IdleRefreshMinutes;
        return TimeSpan.FromMinutes(minutes);
    }

    private static bool IsCodexActive()
    {
        try
        {
            return Process.GetProcesses().Any(process =>
            {
                using (process)
                {
                    return process.ProcessName.Contains("codex", StringComparison.OrdinalIgnoreCase)
                        || process.ProcessName.Contains("chatgpt", StringComparison.OrdinalIgnoreCase);
                }
            });
        }
        catch
        {
            return false;
        }
    }

    private void RaiseNotification(string title, string message)
    {
        NotificationRaised?.Invoke(this, new AppNotification(title, message));
    }

    private sealed class ConsentRequiredReader : ICodexUsageReader
    {
        public string SourceName => "experimental-oauth";

        public Task<UsageReadResult> ReadAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(UsageReadResult.Failure(
                UsageFreshness.AuthenticationRequired,
                "experimental_reader_consent_required",
                "Enable the experimental local Codex reader consent in Settings."));
        }
    }
}
