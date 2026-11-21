using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Text.Json;
using Reprise.Core;

namespace Reprise.Desktop;

/// <summary>Opt-in, online-only collection with account-wide coordination owned by the server.</summary>
public sealed class RecordShopConnection : IDisposable
{
    private readonly HttpClient _http;
    private readonly Uri _origin;
    private readonly string _secretPath;
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _pairing;
    private Credentials? _credentials;
    private Sample? _sample;
    private long _observedTick;
    private Lease? _lease;
    private object? _pending;
    private Sample? _pendingSample;
    private readonly HashSet<string> _uploadedArtwork = [];
    private readonly Task _loop;

    /// <summary>Loads a private installation credential; no traffic is sent until the user connects.</summary>
    public RecordShopConnection(string? origin = null, string? secretPath = null, HttpMessageHandler? handler = null)
    {
        _origin = new Uri(origin ?? Environment.GetEnvironmentVariable("REPRISE_RECORD_SHOP_URL") ?? "https://reprise.mx");
        if (_origin.Scheme != "https" && !(_origin.Scheme == "http" && _origin.IsLoopback))
            throw new ArgumentException("Record Shop requires HTTPS or a loopback development server.", nameof(origin));
        _secretPath = secretPath ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Reprise", "record-shop.bin");
        _http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false }) { BaseAddress = _origin, Timeout = TimeSpan.FromSeconds(15) };
        try
        {
            if (File.Exists(_secretPath))
            {
                var bytes = File.ReadAllBytes(_secretPath);
                if (OperatingSystem.IsWindows()) bytes = Protect(bytes, decrypt: true);
                _credentials = JsonSerializer.Deserialize<Credentials>(bytes);
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or System.ComponentModel.Win32Exception) { }
        Status = _credentials is null ? "연결되지 않음" : "연결됨 · 다음 재생을 기다리는 중";
        _loop = RunAsync();
    }

    /// <summary>Human-readable network and collection status, without exposing bearer credentials.</summary>
    public string Status { get; private set; }
    /// <summary>The one-time code shown while the browser asks the user for consent.</summary>
    public string? PairingCode { get; private set; }
    /// <summary>Whether this installation has a stored credential.</summary>
    public bool IsConnected => _credentials is not null;
    /// <summary>Whether a browser pairing attempt is already in progress.</summary>
    public bool IsConnecting => _pairing is not null;

    /// <summary>Captures the freshest real poll; local output takes priority over mirrored remote state.</summary>
    public void Observe(IReadOnlyList<MediaSessionSnapshot> sessions, MediaSessionSnapshot? active, byte[]? artwork)
    {
        var selected = sessions.FirstOrDefault(s => s.Status == PlaybackStatus.Playing && s.CollectionEvidence == "local_output")
            ?? active;
        if (selected is null || string.IsNullOrWhiteSpace(selected.Title) || string.IsNullOrWhiteSpace(selected.Artist))
        {
            _sample = null;
        }
        else
        {
            var provider = Provider(selected.PlayerName);
            _sample = new Sample(new Observation(selected.Title, selected.Artist, selected.Album,
                selected.Duration is { TotalSeconds: > 0 } duration ? duration.TotalSeconds : null,
                selected.Position?.TotalSeconds, selected.Status == PlaybackStatus.Playing, provider,
                selected.CollectionEvidence ?? (provider == "spotify" ? "remote_state" : "app_state")),
                selected.ArtworkUri, selected == active ? artwork : null);
        }
        _observedTick = Stopwatch.GetTimestamp();
    }

    /// <summary>Pairs through the user's browser, then stores the one-time issued installation token.</summary>
    public async Task ConnectAsync(Action<Uri> openBrowser)
    {
        if (IsConnecting || IsConnected) return;
        using var pairing = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        pairing.CancelAfter(TimeSpan.FromMinutes(10));
        _pairing = pairing;
        try
        {
            using var start = await _http.PostAsJsonAsync("/api/device/start", new { name = $"Reprise · {Environment.MachineName}" }, pairing.Token);
            start.EnsureSuccessStatusCode();
            var grant = await start.Content.ReadFromJsonAsync<Grant>(JsonOptions, pairing.Token) ?? throw new IOException("Empty pairing response.");
            var uri = new Uri(grant.VerificationUri);
            if (uri.GetLeftPart(UriPartial.Authority) != _origin.GetLeftPart(UriPartial.Authority) || uri.AbsolutePath != "/connect")
                throw new IOException("Unexpected verification URL.");
            PairingCode = grant.UserCode;
            Status = $"브라우저에서 Google 로그인 후 코드 {grant.UserCode}를 확인하세요.";
            openBrowser(uri);
            while (!pairing.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(grant.Interval, 5, 30)), pairing.Token);
                using var poll = await _http.PostAsJsonAsync("/api/device/token", new { device_code = grant.DeviceCode }, pairing.Token);
                if (poll.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.TooManyRequests) continue;
                poll.EnsureSuccessStatusCode();
                var result = await poll.Content.ReadFromJsonAsync<TokenResult>(JsonOptions, pairing.Token) ?? throw new IOException("Empty token response.");
                var credentials = new Credentials(result.AccessToken, result.DeviceId);
                Save(credentials);
                _credentials = credentials;
                _lease = null;
                _pending = null;
                Status = "연결됨 · 재생 기록 전송에 동의했습니다.";
                return;
            }
        }
        catch (OperationCanceledException) { Status = "연결이 취소되었거나 만료되었습니다."; }
        catch (Exception error) when (error is HttpRequestException or IOException or JsonException or System.ComponentModel.Win32Exception)
        { Status = "연결하지 못했습니다. 서버 주소와 Google 로그인 설정을 확인하세요."; }
        finally { PairingCode = null; _pairing = null; }
    }

    /// <summary>Cancels the pending browser pairing without changing an existing credential.</summary>
    public void CancelConnect() => _pairing?.Cancel();

    /// <summary>Stops local transmission immediately and attempts server-side installation revocation.</summary>
    public async Task DisconnectAsync()
    {
        _pairing?.Cancel();
        var credentials = _credentials;
        _credentials = null; _lease = null; _pending = null; _sample = null;
        if (File.Exists(_secretPath)) File.Delete(_secretPath);
        Status = "연결 해제됨";
        if (credentials is null) return;
        try
        {
            using var request = Authenticated(HttpMethod.Delete, $"/api/devices/{credentials.DeviceId}", credentials);
            using var response = await _http.SendAsync(request, _lifetime.Token);
            if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Unauthorized)
                Status = "로컬 연결 해제됨 · 웹 프로필에서 서버 연결도 해제해 주세요.";
        }
        catch (Exception error) when (error is HttpRequestException or OperationCanceledException)
        { Status = "로컬 연결 해제됨 · 웹 프로필에서 서버 연결도 해제해 주세요."; }
    }

    /// <summary>Reports online observations at ten-second intervals; offline history is never backfilled.</summary>
    private async Task RunAsync()
    {
        while (!_lifetime.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromSeconds(10), _lifetime.Token);
                if (_credentials is { } credentials) await SendAsync(credentials, _lifetime.Token);
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { break; }
            catch (Exception error) when (error is HttpRequestException or IOException or JsonException or OperationCanceledException)
            { Status = "연결 재시도 중 · 확인되지 않은 구간은 집계하지 않습니다."; }
        }
    }

    /// <summary>Retries the exact pending sequence after transport failure and uploads real artwork after credit.</summary>
    private async Task SendAsync(Credentials credentials, CancellationToken cancellationToken)
    {
        var sample = Stopwatch.GetElapsedTime(_observedTick) < TimeSpan.FromSeconds(5) ? _sample : null;
        if (_pending is null)
        {
            _pendingSample = sample;
            _pending = new { epoch = _lease?.Epoch, nonce = _lease?.Nonce, sequence = _lease?.NextSequence, observation = sample?.Observation };
        }
        using var request = Authenticated(HttpMethod.Post, "/api/collector", credentials);
        request.Content = JsonContent.Create(_pending, options: JsonOptions);
        using var response = await _http.SendAsync(request, cancellationToken);
        if (_credentials != credentials) return;
        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            _credentials = null; _lease = null; _pending = null;
            if (File.Exists(_secretPath)) File.Delete(_secretPath);
            Status = "서버에서 연결이 해제되었습니다."; return;
        }
        if (response.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.TooManyRequests)
        { _lease = null; _pending = null; return; }
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<Lease>(JsonOptions, cancellationToken) ?? throw new IOException("Empty collector response.");
        _pending = null;
        _lease = result.Role == "owner" ? result : null;
        Status = result.Role == "observer" ? "연결됨 · 다른 기기에서 집계 중" : result.CreditedSeconds > 0
            ? "연결됨 · 서버에서 청취 시간 집계 중" : "연결됨 · 재생 상태 확인 중";
        if (result.CreditedSeconds <= 0 || result.TrackId is not { } trackId || sample is null || _uploadedArtwork.Contains(trackId)) return;
        sample = _pendingSample;
        if (sample is null) return;
        var artwork = sample.Artwork;
        if (artwork is null && sample.ArtworkUri is { } uri) artwork = await new ArtworkLoader().LoadAsync(uri, cancellationToken);
        if (artwork is not { Length: > 0 and <= 2097152 }) return;
        using var upload = Authenticated(HttpMethod.Put, $"/api/artwork/{trackId}", credentials);
        upload.Content = new ByteArrayContent(artwork);
        using var uploaded = await _http.SendAsync(upload, cancellationToken);
        if (uploaded.IsSuccessStatusCode) _uploadedArtwork.Add(trackId);
    }

    /// <summary>Attaches a bearer credential only to a request for the configured service origin.</summary>
    private static HttpRequestMessage Authenticated(HttpMethod method, string path, Credentials credentials)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", credentials.Token);
        return request;
    }

    /// <summary>Writes a private credential file, protected with DPAPI on Windows.</summary>
    private void Save(Credentials credentials)
    {
        var directory = Path.GetDirectoryName(_secretPath)!;
        Directory.CreateDirectory(directory);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(credentials);
        if (OperatingSystem.IsWindows()) bytes = Protect(bytes, decrypt: false);
        var temporary = _secretPath + ".tmp";
        var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write, Share = FileShare.None };
        if (!OperatingSystem.IsWindows()) options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using (var file = new FileStream(temporary, options)) file.Write(bytes);
        File.Move(temporary, _secretPath, overwrite: true);
    }

    /// <summary>Encrypts or decrypts a current-user DPAPI blob without UI prompts or shared secrets.</summary>
    private static byte[] Protect(byte[] bytes, bool decrypt)
    {
        var input = new Blob { Length = bytes.Length, Data = Marshal.AllocHGlobal(bytes.Length) };
        try
        {
            Marshal.Copy(bytes, 0, input.Data, bytes.Length);
            var success = decrypt ? CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out var output)
                : CryptProtectData(ref input, null, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            try { var result = new byte[output.Length]; Marshal.Copy(output.Data, result, 0, output.Length); return result; }
            finally { LocalFree(output.Data); }
        }
        finally { Marshal.FreeHGlobal(input.Data); }
    }

    /// <summary>Maps player identities to stable collection providers.</summary>
    internal static string Provider(string name) => name.ToLowerInvariant() switch
    {
        var value when value.Contains("spotify") => "spotify",
        var value when value.Contains("melon") || value.Contains("멜론") => "melon",
        var value when value.Contains("youtube") => "youtube-music",
        var value when value.Contains("apple") || value == "music" => "apple-music",
        _ => "other",
    };

    /// <summary>Stops timers and cancels all requests without changing server playback.</summary>
    public void Dispose() { _pairing?.Cancel(); _lifetime.Cancel(); _http.Dispose(); }

    /// <summary>JSON field naming shared with the Worker device protocol.</summary>
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
    /// <summary>Only this installation's opaque credential is persisted.</summary>
    private sealed record Credentials(string Token, string DeviceId);
    /// <summary>Short-lived browser pairing values.</summary>
    private sealed record Grant(string DeviceCode, string UserCode, string VerificationUri, int Interval);
    /// <summary>A one-time credential returned after browser consent.</summary>
    private sealed record TokenResult(string AccessToken, string DeviceId);
    /// <summary>Server-owned lease values; clients never supply a credited time.</summary>
    private sealed record Lease(string Role, string? Epoch, string? Nonce, long? NextSequence, double CreditedSeconds, string? TrackId);
    /// <summary>Metadata and state measured from the existing platform backend.</summary>
    private sealed record Observation(string Title, string Artist, string Album, double? DurationSeconds, double? PositionSeconds, bool Playing, string Provider, string Evidence);
    /// <summary>Transient artwork stays local until a real interval has been accepted.</summary>
    private sealed record Sample(Observation Observation, Uri? ArtworkUri, byte[]? Artwork);
    /// <summary>Native DATA_BLOB used by the current-user Windows credential protector.</summary>
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Length; public IntPtr Data; }
    /// <summary>Protects bytes with the current Windows user's key.</summary>
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob data, string? description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    /// <summary>Unprotects bytes only for the Windows user who stored them.</summary>
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob data, IntPtr description, IntPtr entropy, IntPtr reserved, IntPtr prompt, int flags, out Blob output);
    /// <summary>Releases a buffer returned by DPAPI.</summary>
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
