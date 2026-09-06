namespace Reprise.Desktop;

/// <summary>
/// Launch-at-login through the XDG autostart directory.
/// </summary>
/// <remarks>
/// The Linux counterpart of the macOS login item: a desktop entry under
/// <c>$XDG_CONFIG_HOME/autostart</c> that every freedesktop session runs at
/// login. The entry points at the running executable, so a copy of the
/// self-contained archive placed anywhere in the home directory works
/// without a system install. Failures are reported rather than thrown, as
/// the settings toggle needs a message, not a crash.
/// </remarks>
public sealed class AutostartEntry
{
    private const string FileName = "dev.junx.Reprise.desktop";

    private readonly string _path;
    private readonly string? _executable;

    /// <summary>
    /// Creates the entry for the current user and executable.
    /// </summary>
    public AutostartEntry()
        : this(DefaultPath, Environment.ProcessPath)
    {
    }

    /// <summary>
    /// Creates an entry at an explicit path, for tests.
    /// </summary>
    /// <param name="path">Desktop file to manage.</param>
    /// <param name="executable">Program the entry should launch.</param>
    public AutostartEntry(string path, string? executable)
    {
        _path = path;
        _executable = executable;
    }

    /// <summary>
    /// Location of the autostart entry for this user.
    /// </summary>
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

            return Path.Combine(configHome, "autostart", FileName);
        }
    }

    /// <summary>
    /// Whether the desktop can run Reprise at login at all.
    /// </summary>
    /// <remarks>
    /// False when the executable path is unknown, which happens only in
    /// unusual hosting situations such as tests.
    /// </remarks>
    public bool IsAvailable => !string.IsNullOrEmpty(_executable);

    /// <summary>
    /// Whether an entry exists and is not marked hidden.
    /// </summary>
    public bool IsEnabled
    {
        get
        {
            try
            {
                if (!File.Exists(_path))
                {
                    return false;
                }

                return !File.ReadLines(_path).Any(line =>
                    line.Trim().Equals("Hidden=true", StringComparison.OrdinalIgnoreCase));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return false;
            }
        }
    }

    /// <summary>
    /// Creates or removes the entry.
    /// </summary>
    /// <param name="enabled">Whether Reprise should start at login.</param>
    /// <returns>Null on success, or a message explaining the failure.</returns>
    /// <example>
    /// <code>
    /// var error = new AutostartEntry().SetEnabled(true);
    /// </code>
    /// </example>
    public string? SetEnabled(bool enabled)
    {
        if (!IsAvailable)
        {
            return "실행 파일 위치를 알 수 없어 자동 실행을 설정할 수 없습니다.";
        }

        try
        {
            if (!enabled)
            {
                if (File.Exists(_path))
                {
                    File.Delete(_path);
                }

                return null;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, DesktopFile(_executable!));
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"자동 실행 항목을 저장하지 못했습니다: {exception.Message}";
        }
    }

    /// <summary>
    /// Builds the desktop entry text.
    /// </summary>
    /// <param name="executable">Program to launch.</param>
    /// <returns>A complete <c>.desktop</c> file.</returns>
    private static string DesktopFile(string executable) =>
        $"""
        [Desktop Entry]
        Type=Application
        Name=Reprise
        Comment=Control MPRIS-compatible music players from one panel
        Exec="{executable}"
        Icon=multimedia-player
        Terminal=false
        X-GNOME-Autostart-enabled=true

        """;
}
