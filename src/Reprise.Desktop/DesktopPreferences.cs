using System.Text.Json;
using System.Text.Json.Serialization;

namespace Reprise.Desktop;

/// <summary>
/// The user's panel settings, as one immutable value.
/// </summary>
/// <remarks>
/// A record so a change is expressed as <c>with</c> and applied through
/// <see cref="PreferencesStore.Update"/>, which is the only place that
/// persists and broadcasts it. Defaults match the values the macOS app
/// registers on first launch.
/// </remarks>
/// <param name="PanelTheme">Visual treatment of the panel.</param>
/// <param name="LeadingTimeStyle">Value shown in the left time label.</param>
/// <param name="TrailingTimeStyle">Value shown in the right time label.</param>
/// <param name="AutomaticallyScrollsTitles">
/// Whether a long title scrolls on its own, or only while hovered.
/// </param>
/// <param name="MarqueePointsPerSecond">Title scrolling speed.</param>
/// <param name="MenuBarTitleFormat">Text shown beside the tray icon.</param>
/// <param name="MenuBarShowsLyrics">
/// Whether the tray label shows the current lyric line instead of the title
/// when synced lyrics exist.
/// </param>
/// <param name="MenuBarArtworkStyle">What the tray icon shows for a track.</param>
/// <param name="MenuBarLabelLength">
/// Longest label the tray shows before it starts scrolling, in characters.
/// </param>
/// <param name="MenuBarReservesLabelWidth">
/// Whether the tray label keeps its full width while showing lyrics, so
/// neighbouring items do not shift as lines change length.
/// </param>
/// <param name="ResetsMenuTitleWhenPanelOpens">
/// Whether a scrolling tray label restarts from the beginning when the panel
/// is opened.
/// </param>
/// <param name="AutomaticallyPausesOtherPlayer">
/// Whether a player that starts playing pauses any other player that was
/// already playing.
/// </param>
/// <param name="RemembersLastPlayedPlayer">
/// Whether the player the user most recently started takes precedence over
/// the display priority.
/// </param>
/// <param name="LastPlayedPlayer">Kind of the player most recently started.</param>
/// <param name="PlayerDisplayPriority">
/// Comma-separated player kinds, most preferred first; see
/// <see cref="PlayerPriority"/>.
/// </param>
public sealed record DesktopPreferences(
    PanelTheme PanelTheme = PanelTheme.Liquid,
    PanelLeadingTimeStyle LeadingTimeStyle = PanelLeadingTimeStyle.Elapsed,
    PanelTrailingTimeStyle TrailingTimeStyle = PanelTrailingTimeStyle.Remaining,
    bool AutomaticallyScrollsTitles = true,
    double MarqueePointsPerSecond = 30,
    MenuBarTitleFormat MenuBarTitleFormat = MenuBarTitleFormat.TitleOnly,
    bool MenuBarShowsLyrics = false,
    MenuBarArtworkStyle MenuBarArtworkStyle = MenuBarArtworkStyle.AlbumArtwork,
    int MenuBarLabelLength = 30,
    bool MenuBarReservesLabelWidth = true,
    bool ResetsMenuTitleWhenPanelOpens = true,
    bool AutomaticallyPausesOtherPlayer = false,
    bool RemembersLastPlayedPlayer = false,
    string LastPlayedPlayer = "",
    string PlayerDisplayPriority = "spotify,youTubeMusic,generic");

/// <summary>
/// Loads, holds, and saves <see cref="DesktopPreferences"/>.
/// </summary>
/// <remarks>
/// The file lives under the XDG configuration directory so it survives
/// reinstalling the application and follows the user's home. Reading is
/// forgiving - a missing or damaged file yields defaults rather than a
/// crash at startup - while writing is best-effort, since losing a setting
/// is preferable to losing the panel.
/// </remarks>
public sealed class PreferencesStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _path;

    /// <summary>
    /// Creates a store bound to one file, reading it immediately.
    /// </summary>
    /// <param name="path">File the preferences are kept in.</param>
    /// <example>
    /// <code>
    /// var store = new PreferencesStore(PreferencesStore.DefaultPath);
    /// </code>
    /// </example>
    public PreferencesStore(string path)
    {
        _path = path;
        Current = Read(path);
    }

    /// <summary>
    /// Raised after <see cref="Current"/> changes, on the calling thread.
    /// </summary>
    public event EventHandler? Changed;

    /// <summary>
    /// The preferences in effect right now.
    /// </summary>
    public DesktopPreferences Current { get; private set; }

    /// <summary>
    /// Location of the preferences file for this user.
    /// </summary>
    /// <remarks>
    /// Honours <c>XDG_CONFIG_HOME</c> and falls back to <c>~/.config</c>, as
    /// the base directory specification requires.
    /// </remarks>
    public static string DefaultPath
    {
        get
        {
            var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
            if (string.IsNullOrWhiteSpace(configHome))
            {
                configHome = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".config");
            }

            return Path.Combine(configHome, "reprise", "preferences.json");
        }
    }

    /// <summary>
    /// Applies a change, saves it, and notifies listeners.
    /// </summary>
    /// <remarks>
    /// An update that produces an equal value is dropped without saving or
    /// notifying, so menus can re-apply the current choice freely.
    /// </remarks>
    /// <param name="change">Produces the new value from the current one.</param>
    /// <example>
    /// <code>
    /// store.Update(current => current with { PanelTheme = PanelTheme.Dark });
    /// </code>
    /// </example>
    public void Update(Func<DesktopPreferences, DesktopPreferences> change)
    {
        ArgumentNullException.ThrowIfNull(change);

        var next = change(Current);
        if (next == Current)
        {
            return;
        }

        Current = next;
        Write(_path, next);
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Reads a preferences file, tolerating its absence or corruption.
    /// </summary>
    /// <param name="path">File to read.</param>
    /// <returns>The stored preferences, or defaults when unreadable.</returns>
    private static DesktopPreferences Read(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return new DesktopPreferences();
            }

            using var stream = File.OpenRead(path);
            return JsonSerializer.Deserialize<DesktopPreferences>(stream, SerializerOptions)
                ?? new DesktopPreferences();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return new DesktopPreferences();
        }
    }

    /// <summary>
    /// Writes a preferences file, creating its directory as needed.
    /// </summary>
    /// <remarks>
    /// Failures are swallowed on purpose: the setting still applies for
    /// this session, and a read-only home directory should not make the
    /// menu misbehave.
    /// </remarks>
    /// <param name="path">File to write.</param>
    /// <param name="preferences">Value to store.</param>
    private static void Write(string path, DesktopPreferences preferences)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var stream = File.Create(path);
            JsonSerializer.Serialize(stream, preferences, SerializerOptions);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
