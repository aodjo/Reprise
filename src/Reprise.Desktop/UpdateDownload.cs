using System.Security.Cryptography;

namespace Reprise.Desktop;

/// <summary>
/// Streams an update package to disk and verifies its published size and SHA-256.
/// </summary>
public static class UpdateDownload
{
    /// <summary>
    /// Downloads into a new private file; callers validate the URL and package metadata.
    /// </summary>
    /// <param name="client">HTTP transport supplied by the platform.</param>
    /// <param name="update">Expected URL, byte count, and digest.</param>
    /// <param name="path">New destination file.</param>
    /// <param name="progress">Receives download percentages.</param>
    /// <param name="cancellationToken">Cancels network and disk operations.</param>
    /// <returns>A task completing only after byte count and digest both match.</returns>
    public static async Task DownloadAsync(HttpClient client, ApplicationUpdate update, string path,
        IProgress<UpdateProgress> progress, CancellationToken cancellationToken)
    {
        using var response = await client.GetAsync(update.DownloadUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is { } length && length != update.Size)
        {
            throw new InvalidDataException("다운로드 파일 크기가 업데이트 정보와 다릅니다. 다시 확인해 주세요.");
        }

        await using var input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await CopyVerifiedAsync(input, update, path, progress, cancellationToken);
    }

    /// <summary>Copies and verifies a prepared package, or downloads it when the cache is absent or damaged.</summary>
    public static async Task DownloadOrUsePreparedAsync(HttpClient client, ApplicationUpdate update, string cacheRoot, string path,
        IProgress<UpdateProgress> progress, CancellationToken cancellationToken)
    {
        var prepared = PreparedPath(update, cacheRoot);
        if (File.Exists(prepared))
        {
            try
            {
                await using var input = new FileStream(prepared, FileMode.Open, FileAccess.Read, FileShare.Read,
                    65536, FileOptions.Asynchronous);
                await CopyVerifiedAsync(input, update, path, progress, cancellationToken);
                return;
            }
            catch (InvalidDataException)
            {
                File.Delete(path);
                File.Delete(prepared);
            }
        }
        await DownloadAsync(client, update, path, progress, cancellationToken);
    }

    /// <summary>Downloads and verifies bytes without starting an installer; concurrent preparations publish only complete files.</summary>
    public static async Task<string> PrepareAsync(HttpClient client, ApplicationUpdate update, string cacheRoot,
        IProgress<UpdateProgress> progress, CancellationToken cancellationToken)
    {
        var target = PreparedPath(update, cacheRoot);
        if (File.Exists(target))
        {
            await using var input = new FileStream(target, FileMode.Open, FileAccess.Read, FileShare.Read,
                65536, FileOptions.Asynchronous);
            if (input.Length == update.Size && Convert.ToHexString(await SHA256.HashDataAsync(input, cancellationToken))
                .Equals(update.Sha256, StringComparison.OrdinalIgnoreCase)) return target;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await DownloadAsync(client, update, temporary, progress, cancellationToken);
            File.Move(temporary, target, true);
            return target;
        }
        finally { File.Delete(temporary); }
    }

    /// <summary>Uses a validated content hash as the cache key, preventing feed data from escaping the application's cache.</summary>
    private static string PreparedPath(ApplicationUpdate update, string cacheRoot)
    {
        if (update.Sha256.Length != 64 || !update.Sha256.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid package digest.");
        var extension = Path.GetExtension(update.DownloadUri.AbsolutePath).ToLowerInvariant();
        if (extension is not (".deb" or ".exe")) extension = ".bin";
        return Path.Combine(cacheRoot, "prepared", update.Sha256.ToLowerInvariant(), "package" + extension);
    }

    /// <summary>Verifies length and SHA-256 while streaming either network data or cached data into a new destination.</summary>
    private static async Task CopyVerifiedAsync(Stream input, ApplicationUpdate update, string path,
        IProgress<UpdateProgress> progress, CancellationToken cancellationToken)
    {
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
            65536, FileOptions.Asynchronous);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buffer = new byte[65536];
        long total = 0;
        var percent = -1;
        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken)) != 0)
        {
            total += read;
            if (total > update.Size)
            {
                throw new InvalidDataException("다운로드 파일 크기가 업데이트 정보와 다릅니다. 다시 확인해 주세요.");
            }

            hash.AppendData(buffer, 0, read);
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
            var nextPercent = (int)(100 * total / update.Size);
            if (nextPercent != percent)
            {
                progress.Report(new UpdateProgress("업데이트를 다운로드하고 있습니다…", (double)total / update.Size));
                percent = nextPercent;
            }
        }

        if (total != update.Size ||
            !Convert.ToHexString(hash.GetHashAndReset()).Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("다운로드한 파일을 검증하지 못했습니다. 업데이트를 다시 확인해 주세요.");
        }
    }
}
