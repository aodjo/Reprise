using System.Net.Http.Headers;

namespace Reprise.Desktop;

/// <summary>
/// Fetches the bytes behind an artwork URI.
/// </summary>
/// <remarks>
/// An interface so the view model can be tested without touching the file
/// system or the network.
/// </remarks>
public interface IArtworkLoader
{
    /// <summary>
    /// Loads the encoded image a player points at.
    /// </summary>
    /// <param name="uri">Absolute artwork location.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>
    /// The encoded image bytes, or null when the artwork cannot be fetched.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled.
    /// </exception>
    Task<byte[]?> LoadAsync(Uri uri, CancellationToken cancellationToken);
}

/// <summary>
/// Loads artwork from the schemes MPRIS players actually publish.
/// </summary>
/// <remarks>
/// Players hand out <c>file:</c> paths into their own caches, <c>http(s):</c>
/// links to a CDN, and occasionally inline <c>data:</c> URIs. Anything else
/// is treated as no artwork. Failures resolve to null rather than throw
/// because a missing cover is cosmetic and the poll that triggered the load
/// must not surface it as an error.
/// </remarks>
public sealed class ArtworkLoader : IArtworkLoader
{
    /// <summary>
    /// Largest cover accepted, to keep a hostile or broken URL from filling
    /// memory.
    /// </summary>
    private const long MaximumBytes = 16 * 1024 * 1024;

    private static readonly HttpClient Http = CreateClient();

    /// <summary>
    /// Loads the encoded image a player points at.
    /// </summary>
    /// <param name="uri">Absolute artwork location.</param>
    /// <param name="cancellationToken">Cancels the load.</param>
    /// <returns>
    /// The encoded image bytes, or null when the scheme is unsupported, the
    /// source is missing, or the transfer fails.
    /// </returns>
    /// <exception cref="OperationCanceledException">
    /// Thrown when <paramref name="cancellationToken"/> is cancelled.
    /// </exception>
    /// <example>
    /// <code>
    /// var bytes = await loader.LoadAsync(session.ArtworkUri, token);
    /// </code>
    /// </example>
    public async Task<byte[]?> LoadAsync(Uri uri, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(uri);

        try
        {
            return uri.Scheme switch
            {
                "file" => await ReadFileAsync(uri, cancellationToken),
                "http" or "https" => await ReadHttpAsync(uri, cancellationToken),
                "data" => DecodeDataUri(uri),
                _ => null,
            };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or HttpRequestException
            or FormatException
            or TaskCanceledException
            or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Builds the shared HTTP client with a bounded timeout.
    /// </summary>
    /// <returns>A client that identifies itself and gives up after ten seconds.</returns>
    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("Reprise", "2.0"));
        return client;
    }

    /// <summary>
    /// Reads a local file, refusing anything past <see cref="MaximumBytes"/>.
    /// </summary>
    /// <param name="uri">A <c>file:</c> URI.</param>
    /// <param name="cancellationToken">Cancels the read.</param>
    /// <returns>The file contents, or null when absent or too large.</returns>
    private static async Task<byte[]?> ReadFileAsync(Uri uri, CancellationToken cancellationToken)
    {
        var path = uri.LocalPath;
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > MaximumBytes)
        {
            return null;
        }

        return await File.ReadAllBytesAsync(path, cancellationToken);
    }

    /// <summary>
    /// Downloads a remote image, refusing anything past <see cref="MaximumBytes"/>.
    /// </summary>
    /// <param name="uri">An <c>http:</c> or <c>https:</c> URI.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>The response body, or null on a non-success status or oversize body.</returns>
    private static async Task<byte[]?> ReadHttpAsync(Uri uri, CancellationToken cancellationToken)
    {
        using var response = await Http.GetAsync(
            uri,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode
            || response.Content.Headers.ContentLength > MaximumBytes)
        {
            return null;
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        return bytes.Length > MaximumBytes ? null : bytes;
    }

    /// <summary>
    /// Decodes an inline <c>data:</c> URI.
    /// </summary>
    /// <remarks>
    /// Only the base64 form is accepted; percent-encoded image data is not
    /// something any known player emits.
    /// </remarks>
    /// <param name="uri">A <c>data:</c> URI.</param>
    /// <returns>The decoded payload, or null when it is not base64.</returns>
    private static byte[]? DecodeDataUri(Uri uri)
    {
        var body = uri.OriginalString;
        var separator = body.IndexOf(',', StringComparison.Ordinal);
        if (separator < 0)
        {
            return null;
        }

        var header = body[..separator];
        if (!header.EndsWith(";base64", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Convert.FromBase64String(body[(separator + 1)..]);
    }
}
