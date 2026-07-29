using System.Net;
using System.Text;
using System.Text.Json;
using CodexUsageCompanion.Core.Abstractions;
using CodexUsageCompanion.Core.Models;

namespace CodexUsageCompanion.Reader.Codex.Tests;

public sealed class CodexReaderTests
{
    [Fact]
    public void OAuth_payload_is_normalized_to_remaining_percent()
    {
        var payload = Encoding.UTF8.GetBytes(
            """
            {
              "rate_limit": {
                "primary_window": {
                  "used_percent": 37.5,
                  "reset_at": 1785355200,
                  "limit_window_seconds": 18000
                },
                "secondary_window": {
                  "used_percent": 10,
                  "reset_at": 1785787200,
                  "limit_window_seconds": 604800
                }
              }
            }
            """);
        var snapshot = CodexOAuthUsageReader.ParseUsage(
            payload,
            DateTimeOffset.Parse("2026-07-29T12:00:00Z"));

        Assert.Equal(62.5m, snapshot.ShortTermWindow?.RemainingPercent);
        Assert.Equal(90m, snapshot.WeeklyWindow?.RemainingPercent);
        Assert.Equal("codexbar-compatible-oauth", snapshot.Source);
        Assert.True(snapshot.ShortTermWindow?.IsConfirmed);
    }

    [Fact]
    public void OAuth_payload_rejects_out_of_range_percent()
    {
        var payload = Encoding.UTF8.GetBytes(
            """{"rate_limit":{"primary_window":{"used_percent":120}}}""");
        Assert.Throws<InvalidDataException>(() =>
            CodexOAuthUsageReader.ParseUsage(payload, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task OAuth_reader_returns_authentication_required_when_auth_is_missing()
    {
        using var directory = new TemporaryDirectory();
        using var reader = new CodexOAuthUsageReader(
            new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)),
            new FixedClock(),
            new CodexOAuthUsageReaderOptions { CodexHomePath = directory.Path });

        var result = await reader.ReadAsync(CancellationToken.None);
        Assert.False(result.IsSuccess);
        Assert.Equal(UsageFreshness.AuthenticationRequired, result.Status);
        Assert.Equal("codex_auth_not_found", result.ErrorCode);
    }

    [Fact]
    public async Task OAuth_reader_uses_bearer_in_memory_and_normalizes_response()
    {
        using var directory = new TemporaryDirectory();
        await File.WriteAllTextAsync(
            System.IO.Path.Combine(directory.Path, "auth.json"),
            """{"tokens":{"access_token":"test-access","account_id":"account-1"}}""");
        var sawExpectedHeaders = false;
        using var reader = new CodexOAuthUsageReader(
            new StubHandler(request =>
            {
                sawExpectedHeaders = request.Headers.Authorization?.Parameter == "test-access"
                    && request.Headers.Contains("ChatGPT-Account-Id");
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"rate_limit":{"primary_window":{"used_percent":25,"limit_window_seconds":18000}}}""",
                        Encoding.UTF8,
                        "application/json")
                };
            }),
            new FixedClock(),
            new CodexOAuthUsageReaderOptions { CodexHomePath = directory.Path });

        var result = await reader.ReadAsync(CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.True(sawExpectedHeaders);
        Assert.Equal(75m, result.Snapshot?.ShortTermWindow?.RemainingPercent);
    }

    [Fact]
    public void Cli_rpc_payload_is_normalized()
    {
        using var document = JsonDocument.Parse(
            """
            {
              "id": 2,
              "result": {
                "rateLimits": {
                  "primary": {"usedPercent": 12, "windowDurationMins": 300},
                  "secondary": {"usedPercent": 45, "windowDurationMins": 10080}
                }
              }
            }
            """);
        var snapshot = CodexCliRpcUsageReader.ParseResponse(
            document.RootElement,
            DateTimeOffset.Parse("2026-07-29T12:00:00Z"));
        Assert.Equal(88m, snapshot.ShortTermWindow?.RemainingPercent);
        Assert.Equal(55m, snapshot.WeeklyWindow?.RemainingPercent);
        Assert.Equal("codex-cli-rpc", snapshot.Source);
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-07-29T12:00:00Z");
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

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"codex-usage-reader-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, true);
        }
    }
}
