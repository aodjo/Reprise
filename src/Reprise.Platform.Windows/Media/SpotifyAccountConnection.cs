using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Reprise.Core;
using Reprise.Desktop;

namespace Reprise.Platform.Windows.Media;

/// <summary>Uses Spotify's official PKCE and playback APIs for local and remote Connect device volume.</summary>
internal sealed class SpotifyAccountConnection : IPlayerAccountConnection
{
    /// <summary>Official authorization endpoint; no application secret is used by a desktop public client.</summary>
    private const string AuthorizeEndpoint = "https://accounts.spotify.com/authorize";
    /// <summary>Exact redirect path registered for the public client, with a dynamically assigned loopback port.</summary>
    internal const string CallbackPath = "/reprise/spotify/callback";
    /// <summary>Scopes needed to observe and change the user's current playback device.</summary>
    private const string Scopes = "user-read-playback-state user-modify-playback-state";
    private readonly string _clientId;
    private readonly HttpClient _http;
    private readonly ISpotifyTokenStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _authorization = new(1, 1);
    private SpotifyToken? _token;
    private SpotifyDeviceVolume? _device;
    private DateTimeOffset _nextRead;
    private DateTimeOffset _retryAfter;
    private long _generation;
    private readonly object _cacheGate = new();
    private Task<SpotifyDeviceVolume?>? _pendingRead;
    private DateTimeOffset _requested;
    private long _observedAtTicks;

    /// <summary>One connection shared by settings and the Windows media backend in the current user process.</summary>
    internal static SpotifyAccountConnection Shared { get; } = new(
        Environment.GetEnvironmentVariable("REPRISE_SPOTIFY_CLIENT_ID") ??
        typeof(SpotifyAccountConnection).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(item => item.Key == "SpotifyClientId")?.Value ?? string.Empty,
        new HttpClient(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(15) }, new SpotifyTokenStore());

    /// <summary>Accepts an isolated transport and protected store for deterministic protocol tests.</summary>
    internal SpotifyAccountConnection(string clientId, HttpClient http, ISpotifyTokenStore store)
    {
        _clientId = clientId.Trim();
        _http = http;
        _store = store;
        _token = store.Read();
        if (_token?.ClientId != _clientId || _token is not { AccessToken.Length: > 0, RefreshToken.Length: > 0 }) _token = null;
    }

    /// <inheritdoc />
    public bool IsConfigured => _clientId.Length == 32 && _clientId.All(Uri.IsHexDigit);
    /// <inheritdoc />
    public bool IsConnected => IsConfigured && _token is not null;
    /// <inheritdoc />
    public string Status { get; private set; } = string.Empty;
    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <summary>Encodes PKCE verifier and state bytes without padding, as required by RFC 7636.</summary>
    internal static string Encode(ReadOnlySpan<byte> bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    /// <summary>Computes the SHA-256 PKCE challenge from a high-entropy verifier.</summary>
    internal static string Challenge(string verifier) => Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
    /// <summary>Accepts only the exact callback path and unpredictable state; denial never replaces existing credentials.</summary>
    internal static string CallbackCode(Uri uri, string expectedState)
    {
        if (!uri.IsLoopback || uri.AbsolutePath != CallbackPath) throw new InvalidOperationException("Spotify 연결 응답 주소가 올바르지 않습니다.");
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Split('=', 2)).ToArray();
        /// <summary>Requires exactly one occurrence of each sensitive callback parameter.</summary>
        string? Read(string key) => query.Count(item => Uri.UnescapeDataString(item[0]) == key) == 1
            ? Uri.UnescapeDataString(query.Single(item => Uri.UnescapeDataString(item[0]) == key).ElementAtOrDefault(1) ?? string.Empty) : null;
        var actual = Encoding.UTF8.GetBytes(Read("state") ?? string.Empty);
        if (!CryptographicOperations.FixedTimeEquals(actual, Encoding.UTF8.GetBytes(expectedState)))
            throw new InvalidOperationException("Spotify 연결 확인 값이 일치하지 않습니다. 다시 연결해 주세요.");
        if (Read("error") is not null) throw new InvalidOperationException("Spotify 계정 연결이 취소되었습니다.");
        return Read("code") is { Length: > 0 } code ? code : throw new InvalidOperationException("Spotify 연결 코드가 없습니다.");
    }

    /// <inheritdoc />
    public async Task ConnectAsync(Func<Uri, Task> openBrowser, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) throw new InvalidOperationException("이 빌드에 Spotify 계정 연결 설정이 포함되지 않았습니다.");
        if (!await _authorization.WaitAsync(0, cancellationToken)) return;
        var generation = Interlocked.Read(ref _generation);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            var verifier = Encode(RandomNumberGenerator.GetBytes(64));
            var state = Encode(RandomNumberGenerator.GetBytes(32));
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var redirect = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}{CallbackPath}");
            var authorization = new Uri(AuthorizeEndpoint + "?" + Query(new Dictionary<string, string>
            {
                ["response_type"] = "code", ["client_id"] = _clientId, ["redirect_uri"] = redirect.AbsoluteUri,
                ["scope"] = Scopes, ["state"] = state, ["code_challenge_method"] = "S256", ["code_challenge"] = Challenge(verifier),
            }));
            SetStatus("브라우저에서 Spotify 계정 연결을 마쳐 주세요.");
            await openBrowser(authorization);
            var code = await ReceiveCodeAsync(listener, redirect, state, timeout.Token);
            await _gate.WaitAsync(timeout.Token);
            try
            {
                var token = await ExchangeAsync(new Dictionary<string, string>
                {
                    ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = redirect.AbsoluteUri,
                    ["client_id"] = _clientId, ["code_verifier"] = verifier,
                }, null, timeout.Token);
                if (generation != Interlocked.Read(ref _generation)) throw new OperationCanceledException("Spotify 연결이 취소되었습니다.");
                _store.Write(token);
                _token = token;
                _device = null;
                _nextRead = default;
                SetStatus("Spotify 계정이 연결되었습니다.");
            }
            finally { _gate.Release(); }
        }
        finally { _authorization.Release(); }
    }

    /// <summary>Reads one bounded HTTP request at a time on loopback; unrelated requests cannot consume the callback.</summary>
    private static async Task<string> ReceiveCodeAsync(TcpListener listener, Uri redirect, string state, CancellationToken cancellationToken)
    {
        while (true)
        {
            using var client = await listener.AcceptTcpClientAsync(cancellationToken);
            using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            requestTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            await using var stream = client.GetStream();
            var bytes = new List<byte>();
            var buffer = new byte[1024];
            while (bytes.Count < 8192)
            {
                var count = await stream.ReadAsync(buffer, requestTimeout.Token);
                if (count == 0) break;
                bytes.AddRange(buffer.AsSpan(0, count).ToArray());
                if (Encoding.ASCII.GetString(bytes.ToArray()).Contains("\r\n\r\n", StringComparison.Ordinal)) break;
            }
            var line = Encoding.ASCII.GetString(bytes.ToArray()).Split("\r\n")[0].Split(' ');
            string? code = null;
            Exception? failure = null;
            if (bytes.Count < 8192 && line is ["GET", var target, "HTTP/1.1"] && target.StartsWith(CallbackPath + "?", StringComparison.Ordinal))
            {
                try { code = CallbackCode(new Uri(redirect, target), state); }
                catch (InvalidOperationException error) { failure = error; }
            }
            const string body = "<!doctype html><meta charset=utf-8><title>Reprise</title><p>Reprise 창에서 Spotify 연결 결과를 확인해 주세요.</p>";
            var payload = Encoding.UTF8.GetBytes(body);
            var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {(code is not null ? "200 OK" : "400 Bad Request")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {payload.Length}\r\nCache-Control: no-store\r\nContent-Security-Policy: default-src 'none'\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(header, cancellationToken);
            await stream.WriteAsync(payload, cancellationToken);
            if (code is not null) return code;
            if (failure is not null) throw failure;
        }
    }

    /// <inheritdoc />
    public void Disconnect()
    {
        Interlocked.Increment(ref _generation);
        _gate.Wait();
        try { _store.Write(null); _token = null; _device = null; _nextRead = default; SetStatus("Spotify 계정 연결을 해제했습니다."); }
        finally { _gate.Release(); }
    }

    /// <summary>Refreshes cloud state off the playback polling path so a slow network cannot stall lyrics or local controls.</summary>
    internal SpotifyDeviceVolume? ReadCachedVolume(MediaSessionSnapshot session)
    {
        if (!IsConnected) return null;
        lock (_cacheGate)
        {
            var now = DateTimeOffset.UtcNow;
            if (_pendingRead is { IsCompleted: true })
            {
                _ = _pendingRead.Exception;
                _pendingRead = null;
            }
            if (_pendingRead is null && now - _requested >= TimeSpan.FromSeconds(2))
            {
                _requested = now;
                _pendingRead = Task.Run(() => ReadVolumeAsync(session, CancellationToken.None));
            }
            var device = _device;
            return now.Ticks - Interlocked.Read(ref _observedAtTicks) <= TimeSpan.FromSeconds(5).Ticks && device?.Matches(session) == true ? device : null;
        }
    }

    /// <summary>Returns a matched device's actual volume, caching reads to avoid polling the Web API at lyric-frame frequency.</summary>
    internal async Task<SpotifyDeviceVolume?> ReadVolumeAsync(MediaSessionSnapshot session, CancellationToken cancellationToken)
    {
        if (!IsConnected) return null;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (DateTimeOffset.UtcNow >= _nextRead && DateTimeOffset.UtcNow >= _retryAfter)
            {
                _nextRead = DateTimeOffset.UtcNow.AddSeconds(2);
                _device = await ReadDeviceAsync(cancellationToken);
                Interlocked.Exchange(ref _observedAtTicks, DateTimeOffset.UtcNow.Ticks);
            }
            return _device is { } device && device.Matches(session) ? device : null;
        }
        catch (Exception error) when (error is HttpRequestException or JsonException or InvalidOperationException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        { _device = null; SetStatus(error.Message); return null; }
        finally { _gate.Release(); }
    }

    /// <summary>Targets the device and track previously displayed, rejecting device switches rather than adjusting another output.</summary>
    internal async Task<bool> SetVolumeAsync(SpotifyDeviceVolume displayed, double value, CancellationToken cancellationToken)
    {
        if (!double.IsFinite(value) || value is < 0 or > 1 || !IsConnected) return false;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = await ReadDeviceAsync(cancellationToken);
            if (current is null || current.DeviceId != displayed.DeviceId || current.TrackId != displayed.TrackId) return false;
            var percent = (int)Math.Round(value * 100);
            using var response = await SendAsync(HttpMethod.Put, "me/player/volume?" + Query(new Dictionary<string, string>
                { ["volume_percent"] = percent.ToString(System.Globalization.CultureInfo.InvariantCulture), ["device_id"] = current.DeviceId }), cancellationToken);
            await CheckResponseAsync(response, cancellationToken);
            _nextRead = default;
            _device = current with { Volume = percent / 100d };
            Interlocked.Exchange(ref _observedAtTicks, DateTimeOffset.UtcNow.Ticks);
            return true;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Reads current playback only; never transfers playback or selects a different device.</summary>
    private async Task<SpotifyDeviceVolume?> ReadDeviceAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, "me/player", cancellationToken);
        if (response.StatusCode == HttpStatusCode.NoContent) return null;
        await CheckResponseAsync(response, cancellationToken);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = json.RootElement;
        if (!root.TryGetProperty("device", out var device) || device.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("item", out var track) || track.ValueKind != JsonValueKind.Object ||
            !track.TryGetProperty("id", out var trackId) || trackId.ValueKind != JsonValueKind.String ||
            !track.TryGetProperty("name", out var name) || name.ValueKind != JsonValueKind.String ||
            !track.TryGetProperty("artists", out var artists) || artists.ValueKind != JsonValueKind.Array ||
            device.TryGetProperty("supports_volume", out var supports) && supports.ValueKind == JsonValueKind.False ||
            device.TryGetProperty("is_restricted", out var restricted) && restricted.ValueKind == JsonValueKind.True ||
            !device.TryGetProperty("id", out var id) || id.GetString() is not { Length: > 0 } deviceId ||
            !device.TryGetProperty("volume_percent", out var level) || level.ValueKind != JsonValueKind.Number ||
            !level.TryGetInt32(out var percent) || percent is < 0 or > 100) return null;
        return new SpotifyDeviceVolume(deviceId, trackId.GetString() ?? string.Empty, name.GetString() ?? string.Empty,
            string.Join(", ", artists.EnumerateArray().Select(artist => artist.TryGetProperty("name", out var artistName) ? artistName.GetString() : null)),
            percent / 100d,
            track.TryGetProperty("album", out var album) && album.TryGetProperty("name", out var albumName) ? albumName.GetString() ?? string.Empty : string.Empty,
            track.TryGetProperty("duration_ms", out var duration) && duration.TryGetInt64(out var milliseconds) && milliseconds >= 0
                ? TimeSpan.FromMilliseconds(milliseconds) : null);
    }

    /// <summary>Refreshes expired access tokens and retries one unauthorized response without logging token contents.</summary>
    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow < _retryAfter) throw new InvalidOperationException("Spotify 요청이 많습니다. 잠시 후 다시 시도해 주세요.");
        for (var attempt = 0; attempt < 2; attempt++)
        {
            if (_token is null) throw new InvalidOperationException("Spotify 계정을 다시 연결해 주세요.");
            if (_token.ExpiresAt <= DateTimeOffset.UtcNow.AddSeconds(30))
            {
                var refreshed = await ExchangeAsync(new Dictionary<string, string>
                { ["grant_type"] = "refresh_token", ["refresh_token"] = _token.RefreshToken, ["client_id"] = _clientId }, _token.RefreshToken, cancellationToken);
                _store.Write(refreshed);
                _token = refreshed;
            }
            using var request = new HttpRequestMessage(method, "https://api.spotify.com/v1/" + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token.AccessToken);
            var response = await _http.SendAsync(request, cancellationToken);
            if (response.StatusCode != HttpStatusCode.Unauthorized || attempt == 1) return response;
            response.Dispose();
            _token = _token with { ExpiresAt = DateTimeOffset.MinValue };
        }
        throw new InvalidOperationException("Spotify 계정을 다시 연결해 주세요.");
    }

    /// <summary>Exchanges only this client's authorization, preserving refresh-token rotation and required scope validation.</summary>
    private async Task<SpotifyToken> ExchangeAsync(Dictionary<string, string> form, string? previousRefresh, CancellationToken cancellationToken)
    {
        using var response = await _http.PostAsync("https://accounts.spotify.com/api/token", new FormUrlEncodedContent(form), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (previousRefresh is not null && response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
            { _store.Write(null); _token = null; _device = null; }
            throw new InvalidOperationException("Spotify 계정 인증에 실패했습니다. 다시 연결해 주세요.");
        }
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var root = json.RootElement;
        var scope = root.TryGetProperty("scope", out var granted) ? granted.GetString() ?? string.Empty : string.Empty;
        if (previousRefresh is null && Scopes.Split(' ').Except(scope.Split(' ')).Any())
            throw new InvalidOperationException("Spotify 재생 상태 확인과 제어 권한을 모두 허용해 주세요.");
        var refresh = root.TryGetProperty("refresh_token", out var rotated) ? rotated.GetString() : previousRefresh;
        if (string.IsNullOrEmpty(refresh)) throw new InvalidOperationException("Spotify 계정을 다시 연결해 주세요.");
        return new SpotifyToken(_clientId, root.GetProperty("access_token").GetString() ?? throw new JsonException(), refresh,
            DateTimeOffset.UtcNow.AddSeconds(root.GetProperty("expires_in").GetInt32()));
    }

    /// <summary>Reports provider restrictions without exposing response bodies or authorization headers.</summary>
    private Task CheckResponseAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (response.IsSuccessStatusCode) return Task.CompletedTask;
        if ((int)response.StatusCode == 429)
        {
            _retryAfter = response.Headers.RetryAfter?.Date ?? DateTimeOffset.UtcNow.Add(response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(30));
            throw new InvalidOperationException("Spotify 요청이 많습니다. 잠시 후 다시 시도해 주세요.");
        }
        throw new InvalidOperationException(response.StatusCode switch
        {
            HttpStatusCode.Forbidden => "Spotify Premium 또는 이 기기의 음량 제어 권한을 확인해 주세요.",
            HttpStatusCode.Unauthorized => "Spotify 계정을 다시 연결해 주세요.",
            HttpStatusCode.NotFound => "재생 중인 Spotify 기기를 찾을 수 없습니다.",
            _ => $"Spotify에 연결할 수 없습니다. ({(int)response.StatusCode})",
        });
    }

    /// <summary>Escapes every OAuth and playback query value independently.</summary>
    private static string Query(Dictionary<string, string> values) => string.Join("&", values.Select(item => Uri.EscapeDataString(item.Key) + "=" + Uri.EscapeDataString(item.Value)));
    /// <summary>Publishes account state for the shared settings page.</summary>
    private void SetStatus(string value) { Status = value; Changed?.Invoke(this, EventArgs.Empty); }
}

/// <summary>Actual remote device and matching track metadata, retained so a slider cannot silently target a newly selected device.</summary>
internal sealed record SpotifyDeviceVolume(string DeviceId, string TrackId, string Title, string Artist, double Volume,
    string Album = "", TimeSpan? Duration = null)
{
    /// <summary>Rejects another account or song when Windows and the authorized Web API do not describe the same playback.</summary>
    internal bool Matches(MediaSessionSnapshot session) => Title.Equals(session.Title, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrEmpty(TrackId) && (Artist.Equals(session.Artist, StringComparison.OrdinalIgnoreCase) ||
        Album.Length > 0 && Album.Equals(session.Album, StringComparison.OrdinalIgnoreCase) &&
        Duration is { } duration && session.Duration is { } observed && Math.Abs((duration - observed).TotalSeconds) < 2);
}

/// <summary>This application's own official OAuth credentials; never obtained from the Spotify desktop app.</summary>
internal sealed record SpotifyToken(string ClientId, string AccessToken, string RefreshToken, DateTimeOffset ExpiresAt);
/// <summary>Separates protected per-user credentials from test transports.</summary>
internal interface ISpotifyTokenStore
{
    /// <summary>Reads this user's existing Reprise authorization if it can be decrypted.</summary>
    SpotifyToken? Read();
    /// <summary>Atomically saves refreshed authorization, or clears it on disconnect.</summary>
    void Write(SpotifyToken? token);
}

/// <summary>Stores official OAuth tokens with Windows DPAPI CurrentUser protection, separately for each release stage.</summary>
internal sealed class SpotifyTokenStore : ISpotifyTokenStore
{
    private readonly string _path = Path.Combine(Path.GetDirectoryName(PreferencesStore.DefaultPath)!, "spotify-connection.bin");
    /// <inheritdoc />
    public SpotifyToken? Read()
    {
        try { return File.Exists(_path) ? JsonSerializer.Deserialize<SpotifyToken>(ProtectedData.Unprotect(File.ReadAllBytes(_path), null, DataProtectionScope.CurrentUser)) : null; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException or JsonException) { return null; }
    }
    /// <inheritdoc />
    public void Write(SpotifyToken? token)
    {
        if (token is null) { File.Delete(_path); return; }
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(temporary, ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(token), null, DataProtectionScope.CurrentUser));
            File.Move(temporary, _path, true);
        }
        finally { File.Delete(temporary); }
    }
}
