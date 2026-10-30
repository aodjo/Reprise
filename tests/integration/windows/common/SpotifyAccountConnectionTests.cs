using System.Net;
using System.Text;
using Reprise.Core;
using Reprise.Platform.Windows.Media;
using Xunit;

namespace Reprise.Platform.Windows.Tests;

/// <summary>Exercises the real PKCE loopback flow and official playback request routing with isolated OAuth credentials.</summary>
public sealed class SpotifyAccountConnectionTests
{
    /// <summary>Public test client identifier, never an actual registered app or secret.</summary>
    private const string ClientId = "0123456789abcdef0123456789abcdef";

    /// <summary>Matches RFC 7636's published SHA-256 verifier/challenge vector.</summary>
    [Fact]
    public void PkceUsesPublishedSha256Vector() => Assert.Equal("E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM",
        SpotifyAccountConnection.Challenge("dBjftJeZ4CVP-mB92K27uhbUJU1p1r_wW1gFWFOEjXk"));

    /// <summary>Localized collaborator names may differ while title, album, and duration still identify the same recording.</summary>
    [Fact]
    public void LocalizedArtistRequiresMatchingAlbumAndDuration()
    {
        var device = new SpotifyDeviceVolume("mac", "one", "Song", "Localized Artist", 0.6, "Album", TimeSpan.FromMinutes(3));
        Assert.True(device.Matches(Session()));
        Assert.False(device.Matches(Session() with { Album = "Another album" }));
        Assert.False(device.Matches(Session() with { Duration = TimeSpan.FromMinutes(4) }));
    }

    /// <summary>Rejects forged, duplicate, unrelated, and denied callbacks before token exchange.</summary>
    [Theory]
    [InlineData("http://127.0.0.1/reprise/spotify/callback?code=one&state=wrong")]
    [InlineData("http://127.0.0.1/reprise/spotify/callback?code=one&state=expected&state=expected")]
    [InlineData("http://127.0.0.1/other?code=one&state=expected")]
    [InlineData("http://example.com/reprise/spotify/callback?code=one&state=expected")]
    [InlineData("http://127.0.0.1/reprise/spotify/callback?error=access_denied&state=expected")]
    public void RejectsInvalidCallbacks(string uri) => Assert.Throws<InvalidOperationException>(() => SpotifyAccountConnection.CallbackCode(new Uri(uri), "expected"));

    /// <summary>Authorizes over an actual loopback socket and sends a verifier without any client secret.</summary>
    [Fact]
    public async Task BrowserAuthorizationUsesPkceAndPersistsOnlyOwnToken()
    {
        var store = new Store(null);
        string? verifier = null;
        var transport = new Transport(async request =>
        {
            var form = await request.Content!.ReadAsStringAsync();
            var values = Parse(form);
            Assert.Equal("https://accounts.spotify.com/api/token", request.RequestUri!.AbsoluteUri);
            Assert.False(values.ContainsKey("client_secret"));
            verifier = values["code_verifier"];
            Assert.InRange(verifier.Length, 43, 128);
            Assert.Equal(ClientId, values["client_id"]);
            Assert.Equal("test-authorization-code", values["code"]);
            return Json("""{"access_token":"own-test-access","refresh_token":"own-test-refresh","expires_in":3600,"scope":"user-read-playback-state user-modify-playback-state"}""");
        });
        using var http = new HttpClient(transport);
        var connection = new SpotifyAccountConnection(ClientId, http, store);
        Task<HttpResponseMessage>? callback = null;
        string? challenge = null;
        using var browser = new HttpClient();
        await connection.ConnectAsync(uri =>
        {
            Assert.Equal("accounts.spotify.com", uri.Host);
            var query = Parse(uri.Query.TrimStart('?'));
            Assert.Equal("S256", query["code_challenge_method"]);
            challenge = query["code_challenge"];
            var redirect = new Uri(query["redirect_uri"]);
            Assert.Equal("127.0.0.1", redirect.Host);
            Assert.Equal(SpotifyAccountConnection.CallbackPath, redirect.AbsolutePath);
            callback = browser.GetAsync(redirect + "?code=test-authorization-code&state=" + Uri.EscapeDataString(query["state"]));
            return Task.CompletedTask;
        });
        using var response = await callback!;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(challenge, SpotifyAccountConnection.Challenge(verifier!));
        Assert.True(connection.IsConnected);
        Assert.Equal("own-test-refresh", store.Value?.RefreshToken);
        connection.Disconnect();
        Assert.Null(store.Value);
        Assert.False(connection.IsConnected);
    }

    /// <summary>Controls the displayed device explicitly and refuses a later device switch without emitting a write.</summary>
    [Fact]
    public async Task RemoteVolumeTargetsOnlyDisplayedDeviceAndTrack()
    {
        var device = "mac device/one";
        var writes = new List<Uri>();
        using var http = new HttpClient(new Transport(request =>
        {
            if (request.Method == HttpMethod.Put)
            {
                writes.Add(request.RequestUri!);
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
            }
            return Task.FromResult(Playback(device));
        }));
        var connection = Connected(http);
        var snapshot = Session();
        var volume = await connection.ReadVolumeAsync(snapshot, default);
        Assert.NotNull(volume);
        Assert.Equal(0.6, volume.Volume);
        Assert.True(await connection.SetVolumeAsync(volume, 0.35, default));
        Assert.Single(writes);
        var query = Parse(writes[0].Query.TrimStart('?'));
        Assert.Equal("35", query["volume_percent"]);
        Assert.Equal(device, query["device_id"]);
        device = "another-device";
        Assert.False(await connection.SetVolumeAsync(volume, 0.8, default));
        Assert.Single(writes);
        Assert.Null(await connection.ReadVolumeAsync(snapshot with { Title = "Another account's song" }, default));
    }

    /// <summary>Refreshes an expired token with its own client identifier, preserving rotated refresh tokens.</summary>
    [Fact]
    public async Task RefreshesExpiredAuthorizationBeforeReadingDevice()
    {
        var store = new Store(new SpotifyToken(ClientId, "expired-test-access", "own-test-refresh", DateTimeOffset.MinValue));
        var calls = new List<string>();
        using var http = new HttpClient(new Transport(async request =>
        {
            calls.Add(request.RequestUri!.Host);
            if (request.RequestUri.Host == "accounts.spotify.com")
            {
                var form = Parse(await request.Content!.ReadAsStringAsync());
                Assert.Equal("refresh_token", form["grant_type"]);
                Assert.Equal("own-test-refresh", form["refresh_token"]);
                Assert.False(form.ContainsKey("client_secret"));
                return Json("""{"access_token":"new-test-access","refresh_token":"rotated-test-refresh","expires_in":3600}""");
            }
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal("new-test-access", request.Headers.Authorization?.Parameter);
            return Playback("mac");
        }));
        var connection = new SpotifyAccountConnection(ClientId, http, store);
        Assert.NotNull(await connection.ReadVolumeAsync(Session(), default));
        Assert.Equal(new[] { "accounts.spotify.com", "api.spotify.com" }, calls);
        Assert.Equal("rotated-test-refresh", store.Value?.RefreshToken);
    }

    /// <summary>Honors rate limits instead of retrying at the native media poll frequency.</summary>
    [Fact]
    public async Task RateLimitsDoNotFloodSpotify()
    {
        var calls = 0;
        using var http = new HttpClient(new Transport(_ =>
        {
            calls++;
            var response = new HttpResponseMessage((HttpStatusCode)429);
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromMinutes(1));
            return Task.FromResult(response);
        }));
        var connection = Connected(http);
        Assert.Null(await connection.ReadVolumeAsync(Session(), default));
        Assert.Null(await connection.ReadVolumeAsync(Session(), default));
        Assert.Equal(1, calls);
        Assert.Contains("잠시 후", connection.Status);
    }

    /// <summary>Devices without writable volume and podcast responses never produce a misleading slider.</summary>
    [Theory]
    [InlineData("""{"device":{"id":"mac","supports_volume":false,"volume_percent":60},"item":{"id":"one","name":"Song","artists":[]}}""")]
    [InlineData("""{"device":{"id":"mac","supports_volume":true,"volume_percent":null},"item":{"id":"one","name":"Song","artists":[]}}""")]
    [InlineData("""{"device":{"id":"mac","is_restricted":true,"volume_percent":60},"item":{"id":"one","name":"Song","artists":[]}}""")]
    [InlineData("""{"device":{"id":"mac","volume_percent":60},"item":{"id":"episode","name":"Podcast"}}""")]
    public async Task UnavailableVolumeDoesNotBecomeAControl(string response)
    {
        using var http = new HttpClient(new Transport(_ => Task.FromResult(Json(response))));
        Assert.Null(await Connected(http).ReadVolumeAsync(Session(), default));
    }

    /// <summary>A revoked refresh token is removed so periodic polling stops until the user reconnects.</summary>
    [Fact]
    public async Task RevokedAuthorizationRequiresAUserReconnect()
    {
        var store = new Store(new SpotifyToken(ClientId, "expired", "revoked-test-refresh", DateTimeOffset.MinValue));
        var calls = 0;
        using var http = new HttpClient(new Transport(_ =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest));
        }));
        var connection = new SpotifyAccountConnection(ClientId, http, store);
        Assert.Null(await connection.ReadVolumeAsync(Session(), default));
        Assert.False(connection.IsConnected);
        Assert.Null(store.Value);
        Assert.Null(await connection.ReadVolumeAsync(Session(), default));
        Assert.Equal(1, calls);
    }

    /// <summary>Builds a connection using this test's synthetic authorization rather than any user's real credentials.</summary>
    private static SpotifyAccountConnection Connected(HttpClient http) => new(ClientId, http,
        new Store(new SpotifyToken(ClientId, "own-test-access", "own-test-refresh", DateTimeOffset.UtcNow.AddHours(1))));
    /// <summary>Returns matching synthetic SMTC metadata for protocol-level routing tests.</summary>
    private static MediaSessionSnapshot Session() => new("windows:Spotify.exe:1", "Spotify", PlaybackStatus.Playing,
        "Song", "Artist", "Album", TimeSpan.FromMinutes(3), TimeSpan.FromSeconds(20), null, null, DateTimeOffset.UtcNow);
    /// <summary>Returns official playback response fields for an isolated test device.</summary>
    private static HttpResponseMessage Playback(string device) => Json(System.Text.Json.JsonSerializer.Serialize(new
    {
        device = new { id = device, supports_volume = true, is_restricted = false, volume_percent = 60 },
        item = new { id = "track-one", name = "Song", artists = new[] { new { name = "Artist" } } },
    }));
    /// <summary>Encodes an isolated provider response.</summary>
    private static HttpResponseMessage Json(string value) => new(HttpStatusCode.OK) { Content = new StringContent(value, Encoding.UTF8, "application/json") };
    /// <summary>Decodes form and query fields independently.</summary>
    private static Dictionary<string, string> Parse(string query) => query.Split('&').Select(item => item.Split('=', 2))
        .ToDictionary(item => Uri.UnescapeDataString(item[0]), item => Uri.UnescapeDataString(item[1].Replace('+', ' ')));
    /// <summary>In-memory credentials restricted to this test instance.</summary>
    private sealed class Store(SpotifyToken? value) : ISpotifyTokenStore
    {
        internal SpotifyToken? Value = value;
        /// <inheritdoc />
        public SpotifyToken? Read() => Value;
        /// <inheritdoc />
        public void Write(SpotifyToken? token) => Value = token;
    }
    /// <summary>Records outgoing requests without accessing a real account or network.</summary>
    private sealed class Transport(Func<HttpRequestMessage, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request);
    }
}
