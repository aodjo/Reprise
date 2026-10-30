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
