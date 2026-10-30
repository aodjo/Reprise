namespace Reprise.Platform.Windows.Media;

/// <summary>
/// Human-readable names for common Windows application media identifiers.
/// </summary>
internal static class WindowsPlayerNames
{
    /// <summary>
    /// Resolves known player names and keeps an unknown source visible with a cleaned identifier.
    /// </summary>
    /// <param name="appId">SourceAppUserModelId advertised by Windows.</param>
    /// <returns>A compact player name.</returns>
    public static string DisplayName(string appId)
    {
        foreach (var (key, name) in new[]
        {
            ("spotify", "Spotify"), ("chrome", "Google Chrome"), ("msedge", "Microsoft Edge"),
            ("firefox", "Firefox"), ("brave", "Brave"), ("vivaldi", "Vivaldi"),
            ("vlc", "VLC"), ("zunemusic", "Media Player"), ("music.ui", "Media Player"),
        })
        {
            if (appId.Contains(key, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        var namePart = appId.Split('!')[^1];
        return string.IsNullOrWhiteSpace(namePart) ? "미디어 플레이어" : Path.GetFileNameWithoutExtension(namePart);
    }
}
