using System.Buffers;
using System.Security.Cryptography;
using CodexUsageCompanion.Infrastructure.Storage;

namespace CodexUsageCompanion.Infrastructure.Updates;

public sealed class UpdateDownloader
{
    private const long MaximumDownloadBytes = 500L * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly ApplicationPaths _paths;

    public UpdateDownloader(HttpClient httpClient, ApplicationPaths paths)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    }

    public static GitHubReleaseAsset? SelectWindowsInstaller(GitHubReleaseInfo release)
    {
        return release.Assets
            .Where(asset => asset.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                || asset.Name.EndsWith(".msix", StringComparison.OrdinalIgnoreCase))
            .OrderBy(asset => asset.Name.Contains("x64", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .FirstOrDefault();
    }

    public async Task<UpdateDownloadResult> DownloadAsync(
        GitHubReleaseInfo release,
        GitHubReleaseAsset asset,
        CancellationToken cancellationToken = default)
    {
        if (asset.Size > MaximumDownloadBytes)
        {
            return new UpdateDownloadResult(false, SanitizedMessage: "The update asset is too large.");
        }

        if (!TryParseDigest(asset.Digest, out var expectedDigest))
        {
            return new UpdateDownloadResult(
                false,
                SanitizedMessage: "The release asset does not include a SHA-256 digest.");
        }

        var safeName = Path.GetFileName(asset.Name);
        if (!string.Equals(safeName, asset.Name, StringComparison.Ordinal)
            || (!safeName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                && !safeName.EndsWith(".msix", StringComparison.OrdinalIgnoreCase)))
        {
            return new UpdateDownloadResult(false, SanitizedMessage: "The update asset name is invalid.");
        }

        var versionDirectory = Path.Combine(_paths.UpdatesDirectory, release.Version.ToString());
        Directory.CreateDirectory(versionDirectory);
        var destinationPath = Path.Combine(versionDirectory, safeName);
        var temporaryPath = destinationPath + $".download-{Guid.NewGuid():N}";

        try
        {
            using var response = await _httpClient.GetAsync(
                asset.DownloadUrl,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateDownloadResult(
                    false,
                    SanitizedMessage: "GitHub could not download the update asset.");
            }

            if (response.Content.Headers.ContentLength is > MaximumDownloadBytes)
            {
                return new UpdateDownloadResult(false, SanitizedMessage: "The update download is too large.");
            }

            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);
            await using var output = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.WriteThrough);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = ArrayPool<byte>.Shared.Rent(81920);
            long total = 0;
            try
            {
                while (true)
                {
                    var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken)
                        .ConfigureAwait(false);
                    if (read == 0)
                    {
                        break;
                    }

                    total += read;
                    if (total > MaximumDownloadBytes)
                    {
                        return new UpdateDownloadResult(
                            false,
                            SanitizedMessage: "The update download exceeded the size limit.");
                    }

                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer, true);
            }

            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
            await output.DisposeAsync().ConfigureAwait(false);
            var actualDigest = hash.GetHashAndReset();
            if (!CryptographicOperations.FixedTimeEquals(actualDigest, expectedDigest))
            {
                return new UpdateDownloadResult(
                    false,
                    SanitizedMessage: "The update SHA-256 digest did not match the GitHub release.");
            }

            File.Move(temporaryPath, destinationPath, true);
            return new UpdateDownloadResult(true, destinationPath);
        }
        catch (HttpRequestException)
        {
            return new UpdateDownloadResult(false, SanitizedMessage: "The update download failed.");
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static bool TryParseDigest(string? digest, out byte[] bytes)
    {
        bytes = [];
        const string prefix = "sha256:";
        if (string.IsNullOrWhiteSpace(digest)
            || !digest.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            bytes = Convert.FromHexString(digest[prefix.Length..]);
            return bytes.Length == 32;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
