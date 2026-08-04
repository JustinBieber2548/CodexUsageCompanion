using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodexUsageCompanion.Core.Models;
using CodexUsageCompanion.Core.Settings;
using CodexUsageCompanion.Core.Versioning;
using CodexUsageCompanion.Infrastructure.Logging;
using CodexUsageCompanion.Infrastructure.Security;
using CodexUsageCompanion.Infrastructure.Storage;
using CodexUsageCompanion.Infrastructure.Supabase;
using CodexUsageCompanion.Infrastructure.Updates;

namespace CodexUsageCompanion.Infrastructure.Tests;

public sealed class InfrastructureBehaviorTests
{
    [Fact]
    public void Default_data_root_is_per_user_application_data()
    {
        var paths = new ApplicationPaths();
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        Assert.StartsWith(
            Path.GetFullPath(localAppData),
            Path.GetFullPath(paths.RootDirectory),
            StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            AppContext.BaseDirectory,
            paths.RootDirectory,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Settings_and_state_round_trip_outside_install_directory()
    {
        using var directory = new TemporaryDirectory();
        var paths = new ApplicationPaths(directory.Path);
        var settingsStore = new JsonSettingsStore(paths);
        var stateStore = new JsonLocalStateStore(paths);
        var settings = new AppSettings { DeviceLabel = "Test workstation" };
        var state = new LocalState().WithSuccessfulSnapshot(Snapshot(62), DateTimeOffset.UtcNow);

        await settingsStore.SaveAsync(settings);
        await stateStore.SaveAsync(state);

        Assert.Equal("Test workstation", (await settingsStore.LoadAsync()).DeviceLabel);
        Assert.Equal(62m, (await stateStore.LoadAsync()).LatestSnapshot?.ShortTermWindow?.RemainingPercent);
        Assert.True(File.Exists(paths.SettingsFile));
        Assert.True(File.Exists(paths.StateFile));
    }

    [Fact]
    public void Local_state_retains_history_but_queues_only_the_newest_snapshot()
    {
        var start = DateTimeOffset.Parse("2026-07-29T12:00:00Z");
        var first = new LocalState().WithSuccessfulSnapshot(
            Snapshot(60) with { CapturedAtUtc = start },
            start);
        var second = first.WithSuccessfulSnapshot(
            Snapshot(59) with { CapturedAtUtc = start.AddMinutes(1) },
            start.AddMinutes(1));

        Assert.Equal(2, second.History.Count);
        Assert.Equal(59m, second.PendingSynchronization?.Snapshot.ShortTermWindow?.RemainingPercent);
    }

    [Fact]
    public async Task Upgrade_backup_copies_settings_and_history_without_credentials()
    {
        using var directory = new TemporaryDirectory();
        var paths = new ApplicationPaths(directory.Path);
        await new JsonSettingsStore(paths).SaveAsync(new AppSettings());
        await new JsonLocalStateStore(paths).SaveAsync(
            new LocalState().WithSuccessfulSnapshot(Snapshot(55), DateTimeOffset.UtcNow));

        var result = await new UpgradeBackupService(paths)
            .BackupAndValidateAsync("1.0.0", "1.1.0");

        Assert.Contains("settings.json", result.Files);
        Assert.Contains("state.json", result.Files);
        Assert.Contains("manifest.json", result.Files);
        Assert.DoesNotContain(
            result.Files,
            item => item.Contains("credential", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Logger_redactor_removes_common_credentials()
    {
        var text =
            "Authorization: Bearer abc123 password=hunter2 access_token=token eyJabcdefgh.abcdefgh.abcdefgh";
        var redacted = SensitiveDataRedactor.Redact(text);
        Assert.DoesNotContain("abc123", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("access_token=token", redacted, StringComparison.Ordinal);
        Assert.DoesNotContain("eyJabcdefgh", redacted, StringComparison.Ordinal);
    }

    [Fact]
    public void Daily_update_policy_never_checks_more_than_once_per_day()
    {
        var now = DateTimeOffset.Parse("2026-07-29T12:00:00Z");
        Assert.False(UpdatePolicy.ShouldAutomaticallyCheck(true, now.AddHours(-23), now));
        Assert.True(UpdatePolicy.ShouldAutomaticallyCheck(true, now.AddHours(-24), now));
        Assert.False(UpdatePolicy.ShouldAutomaticallyCheck(false, null, now));
    }

    [Fact]
    public void Approved_update_uses_progress_only_install_and_reopens_application()
    {
        var startInfo = UpdateInstallerLauncher.CreateAutomaticUpdateStartInfo(
            @"C:\Updates\CodexUsageCompanion-1.1.0-win-x64-setup.exe");

        Assert.True(startInfo.UseShellExecute);
        Assert.Contains("/SILENT", startInfo.Arguments, StringComparison.Ordinal);
        Assert.Contains("/SP-", startInfo.Arguments, StringComparison.Ordinal);
        Assert.Contains("/NORESTART", startInfo.Arguments, StringComparison.Ordinal);
        Assert.Contains("/CLOSEAPPLICATIONS", startInfo.Arguments, StringComparison.Ordinal);
        Assert.Contains("/STARTAPP=1", startInfo.Arguments, StringComparison.Ordinal);
        Assert.DoesNotContain("/VERYSILENT", startInfo.Arguments, StringComparison.Ordinal);
        Assert.DoesNotContain("/SUPPRESSMSGBOXES", startInfo.Arguments, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Github_release_check_uses_semver_and_handles_unavailability()
    {
        var releaseJson =
            """
            {
              "tag_name": "v1.1.0",
              "name": "Version 1.1.0",
              "body": "Notes",
              "html_url": "https://github.com/example/project/releases/tag/v1.1.0",
              "draft": false,
              "prerelease": false,
              "assets": [{
                "name": "CodexUsageCompanion-1.1.0-x64.exe",
                "browser_download_url": "https://github.com/example/project/releases/download/v1.1.0/app.exe",
                "size": 100,
                "digest": "sha256:aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa"
              }]
            }
            """;
        using var successClient = new HttpClient(new StubHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(releaseJson, Encoding.UTF8, "application/json")
            }));
        var available = await new GitHubReleaseClient(successClient)
            .CheckAsync("example/project", SemanticVersion.Parse("1.0.0"));
        Assert.Equal(UpdateCheckStatus.UpdateAvailable, available.Status);
        Assert.Equal("1.1.0", available.Release?.Version.ToString());

        using var unavailableClient = new HttpClient(new StubHandler(
            _ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)));
        var unavailable = await new GitHubReleaseClient(unavailableClient)
            .CheckAsync("example/project", SemanticVersion.Parse("1.0.0"));
        Assert.Equal(UpdateCheckStatus.Unavailable, unavailable.Status);
    }

    [Fact]
    public async Task Update_download_requires_and_verifies_sha256()
    {
        using var directory = new TemporaryDirectory();
        var bytes = Encoding.UTF8.GetBytes("verified installer placeholder");
        var digest = $"sha256:{Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant()}";
        using var client = new HttpClient(new StubHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes)
            }));
        var release = Release(digest, bytes.Length);
        var result = await new UpdateDownloader(client, new ApplicationPaths(directory.Path))
            .DownloadAsync(release, release.Assets[0]);
        Assert.True(result.IsSuccess);
        Assert.Equal(bytes, await File.ReadAllBytesAsync(result.FilePath!));

        var missingDigest = Release(null, bytes.Length);
        var rejected = await new UpdateDownloader(client, new ApplicationPaths(directory.Path))
            .DownloadAsync(missingDigest, missingDigest.Assets[0]);
        Assert.False(rejected.IsSuccess);
    }

    [Fact]
    public async Task Supabase_sign_in_persists_session_but_never_password()
    {
        var credentials = new InMemoryCredentialStore();
        string? requestBody = null;
        using var client = new HttpClient(new AsyncStubHandler(async request =>
        {
            requestBody = await request.Content!.ReadAsStringAsync();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"access_token":"access-value","refresh_token":"refresh-value","expires_in":3600,"user":{"id":"00000000-0000-0000-0000-000000000001"}}""",
                    Encoding.UTF8,
                    "application/json")
            };
        }));
        var auth = new SupabaseAuthClient(client, credentials);
        var result = await auth.SignInAsync(
            "https://project.supabase.co",
            "sb_publishable_test",
            "person@example.com",
            "temporary-password");
        var stored = await credentials.ReadAsync(SupabaseAuthClient.CredentialTarget);

        Assert.True(result.IsSuccess);
        Assert.Contains("temporary-password", requestBody, StringComparison.Ordinal);
        Assert.DoesNotContain("temporary-password", stored, StringComparison.Ordinal);
        Assert.Contains("refresh-value", stored, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Supabase_sync_uploads_only_normalized_fields_and_reports_temporary_failure()
    {
        var credentials = new InMemoryCredentialStore();
        await credentials.SaveAsync(
            SupabaseAuthClient.CredentialTarget,
            JsonSerializer.Serialize(
                new SupabaseSession
                {
                    AccessToken = "user-access-token",
                    RefreshToken = "user-refresh-token",
                    ExpiresAtUtc = DateTimeOffset.UtcNow.AddHours(1),
                    UserId = "00000000-0000-0000-0000-000000000001"
                },
                JsonDefaults.Create()));
        string? uploadedBody = null;
        string? bearer = null;
        var responseStatus = HttpStatusCode.OK;
        using var client = new HttpClient(new AsyncStubHandler(async request =>
        {
            uploadedBody = await request.Content!.ReadAsStringAsync();
            bearer = request.Headers.Authorization?.Parameter;
            return new HttpResponseMessage(responseStatus);
        }));
        var auth = new SupabaseAuthClient(client, credentials);
        var sync = new SupabaseSyncClient(client, auth);
        var settings = new AppSettings
        {
            SupabaseUrl = "https://project.supabase.co",
            SupabasePublishableKey = "sb_publishable_test"
        };

        var success = await sync.UploadAsync(settings, Snapshot(44));
        Assert.True(success.IsSuccess);
        Assert.Equal("user-access-token", bearer);
        Assert.Contains("\"shortTerm\"", uploadedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("user-access-token", uploadedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("user-refresh-token", uploadedBody, StringComparison.Ordinal);
        Assert.DoesNotContain("password", uploadedBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("prompt", uploadedBody, StringComparison.OrdinalIgnoreCase);

        responseStatus = HttpStatusCode.ServiceUnavailable;
        var temporary = await sync.UploadAsync(settings, Snapshot(43));
        Assert.Equal(SupabaseOperationStatus.TemporaryFailure, temporary.Status);
    }

    [Fact]
    public async Task Supabase_auth_rejects_invalid_server_response()
    {
        using var client = new HttpClient(new StubHandler(
            _ => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            }));
        var result = await new SupabaseAuthClient(client, new InMemoryCredentialStore())
            .SignInAsync(
                "https://project.supabase.co",
                "sb_publishable_test",
                "person@example.com",
                "temporary-password");
        Assert.Equal(SupabaseOperationStatus.InvalidResponse, result.Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(20)]
    public void Retry_schedule_is_bounded(int attempt)
    {
        var delay = RetrySchedule.Calculate(attempt, 0.5);
        Assert.InRange(delay, TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(30));
    }

    private static GitHubReleaseInfo Release(string? digest, int size)
    {
        return new GitHubReleaseInfo(
            SemanticVersion.Parse("1.1.0"),
            "Version 1.1.0",
            "Notes",
            new Uri("https://github.com/example/project/releases/tag/v1.1.0"),
            DateTimeOffset.UtcNow,
            [
                new GitHubReleaseAsset(
                    "CodexUsageCompanion-1.1.0-x64.exe",
                    new Uri("https://github.com/example/project/releases/download/v1.1.0/app.exe"),
                    size,
                    digest)
            ]);
    }

    private static UsageSnapshot Snapshot(decimal remaining)
    {
        return new UsageSnapshot
        {
            CapturedAtUtc = DateTimeOffset.UtcNow,
            Source = "test",
            Freshness = UsageFreshness.Fresh,
            IsAuthenticated = true,
            ShortTermWindow = new UsageWindow
            {
                Name = "5-hour window",
                RemainingPercent = remaining,
                UsedPercent = 100 - remaining,
                ResetsAtUtc = DateTimeOffset.UtcNow.AddHours(2),
                IsConfirmed = true
            }
        };
    }

    private sealed class StubHandler(
        Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(responseFactory(request));
        }
    }

    private sealed class AsyncStubHandler(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return responseFactory(request);
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"codex-usage-infrastructure-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, true);
        }
    }
}
