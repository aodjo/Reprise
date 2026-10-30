using System.Text.Json;
using Tmds.DBus.Protocol;

namespace Reprise.Platform.Linux.Tray;

/// <summary>
/// What became of an attempt to turn the shell extension on.
/// </summary>
public enum GnomeShellExtensionResult
{
    /// <summary>
    /// No GNOME Shell is running, so there is nothing to enable.
    /// </summary>
    NoShell,

    /// <summary>
    /// The extension was already enabled.
    /// </summary>
    AlreadyEnabled,

    /// <summary>
    /// The extension was switched on and is running now.
    /// </summary>
    Enabled,

    /// <summary>
    /// The files were put in place, but this shell has to be reloaded before
    /// it will pick them up.
    /// </summary>
    NeedsReload,

    /// <summary>
    /// The shell refused, or the bundled files could not be found.
    /// </summary>
    Failed,
}

/// <summary>
/// Turns Reprise's GNOME Shell extension on without the user doing it.
/// </summary>
/// <remarks>
/// Enabling an extension is per-user state in dconf, so a package's
/// post-install script - which runs as root, often with nobody logged in -
/// cannot reliably do it. Reprise itself runs as the user on their own
/// session bus, which is exactly where the shell expects the request to
/// come from, so it asks on every start: cheap when the extension is
/// already on, and the difference between "it works" and "run this command
/// first" when it is not.
/// <para>
/// The extension only ever changes what Reprise's own entry looks like, and
/// the shell is asked rather than told - it can and does refuse - so doing
/// this unprompted stays within what installing Reprise implies.
/// </para>
/// </remarks>
public static class GnomeShellExtension
{
    /// <summary>
    /// Identifier the extension is installed and enabled under.
    /// </summary>
    public const string Uuid = "reprise@junx.dev";

    private const string ShellService = "org.gnome.Shell";

    /// <summary>
    /// Object carrying the extensions interface.
    /// </summary>
    /// <remarks>
    /// The shell exports <c>org.gnome.Shell.Extensions</c> on its main
    /// object rather than on a path of the interface's own name, which is
    /// the obvious guess and answers nothing.
    /// </remarks>
    private const string ExtensionsPath = "/org/gnome/Shell";

    private const string ExtensionsInterface = "org.gnome.Shell.Extensions";

    /// <summary>
    /// State value GNOME reports for an extension that is running.
    /// </summary>
    private const double EnabledState = 1;

    /// <summary>
    /// How long any single call to the shell may take.
    /// </summary>
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Files the extension is made of, in load order.
    /// </summary>
    private static readonly string[] ExtensionFiles = ["metadata.json", "extension.js"];

    /// <summary>
    /// Makes sure the extension is installed for this user and enabled.
    /// </summary>
    /// <remarks>
    /// Refreshes bundled files even when the extension is already enabled.
    /// Otherwise an older per-user copy can shadow the packaged extension
    /// indefinitely. GNOME keeps loaded JavaScript until the next session,
    /// so replacing it reports NeedsReload instead of claiming it is active.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    /// <returns>What happened, for the caller to log.</returns>
    /// <example>
    /// <code>
    /// var result = await GnomeShellExtension.EnsureEnabledAsync();
    /// </code>
    /// </example>
    public static async Task<GnomeShellExtensionResult> EnsureEnabledAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var connection = DBusConnection.Session;
            await connection.ConnectAsync();

            var info = await GetStateAsync(connection, cancellationToken);
            if (info is null)
            {
                return GnomeShellExtensionResult.NoShell;
            }

            var state = info.Value.State;
            var bundledVersion = ReadBundledVersion();

            bool installed;
            bool updated;
            try
            {
                installed = Install(out updated);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                return GnomeShellExtensionResult.Failed;
            }

            if (state == EnabledState)
            {
                return updated || (bundledVersion > 0 && info.Value.Version != bundledVersion)
                    ? GnomeShellExtensionResult.NeedsReload
                    : GnomeShellExtensionResult.AlreadyEnabled;
            }

            if (state <= 0 && !installed)
            {
                return GnomeShellExtensionResult.Failed;
            }

            var enabled = await EnableAsync(connection, cancellationToken);
            if (!enabled)
            {
                return installed && state == 0
                    ? GnomeShellExtensionResult.NeedsReload
                    : GnomeShellExtensionResult.Failed;
            }

            var confirmed = await GetStateAsync(connection, cancellationToken);
            return confirmed?.State == EnabledState && (!updated || state == 0)
                && (bundledVersion == 0 || confirmed.Value.Version == bundledVersion)
                ? GnomeShellExtensionResult.Enabled
                : GnomeShellExtensionResult.NeedsReload;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return GnomeShellExtensionResult.NoShell;
        }
    }

    /// <summary>
    /// Copies the bundled extension into the user's extension directory.
    /// </summary>
    /// <remarks>
    /// Archive builds install a per-user copy. Debian builds use the system
    /// copy unless an older per-user installation shadows it; that override
    /// is refreshed from the package too.
    /// </remarks>
    /// <param name="updated">True when files were replaced or installed.</param>
    /// <returns>True when the extension is now in place for this user.</returns>
    private static bool Install(out bool updated)
    {
        updated = false;
        var source = FindBundle();
        if (source is null)
        {
            return false;
        }

        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(dataHome))
        {
            dataHome = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local",
                "share");
        }

        var target = Path.Combine(dataHome, "gnome-shell", "extensions", Uuid);
        updated = SynchronizeBundle(source, target, SystemBundlePath);
        return true;
    }

    /// <summary>
    /// Refreshes an installed extension by content, including same-version fixes.
    /// </summary>
    /// <param name="source">Complete extension shipped with the application.</param>
    /// <param name="target">Per-user extension directory to refresh.</param>
    /// <param name="systemBundle">
    /// System installation to use when identical and no user override exists.
    /// </param>
    /// <returns>True when at least one file changed.</returns>
    /// <exception cref="IOException">The bundle is incomplete or a write fails.</exception>
    internal static bool SynchronizeBundle(string source, string target, string? systemBundle = null)
    {
        // Read the complete bundle before touching the installed copy.
        var contents = ExtensionFiles.ToDictionary(
            file => file,
            file => File.ReadAllBytes(Path.Combine(source, file)));
        if (!Directory.Exists(target) && systemBundle is not null
            && contents.All(entry =>
                File.Exists(Path.Combine(systemBundle, entry.Key))
                && File.ReadAllBytes(Path.Combine(systemBundle, entry.Key)).AsSpan().SequenceEqual(entry.Value)))
        {
            return false;
        }

        var changed = false;
        foreach (var (file, bytes) in contents)
        {
            var path = Path.Combine(target, file);
            if (File.Exists(path) && File.ReadAllBytes(path).AsSpan().SequenceEqual(bytes))
            {
                continue;
            }

            Directory.CreateDirectory(target);
            File.WriteAllBytes(path, bytes);
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// System-wide installation used by the Debian package.
    /// </summary>
    private static readonly string SystemBundlePath =
        Path.Combine("/usr/share/gnome-shell/extensions", Uuid);

    /// <summary>
    /// Finds the extension shipped alongside the application.
    /// </summary>
    /// <returns>The directory holding it, or null when it is not there.</returns>
    private static string? FindBundle()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "gnome-extension"),
            Path.Combine(AppContext.BaseDirectory, "..", "gnome-extension"),
            SystemBundlePath,
        };

        return candidates.FirstOrDefault(candidate =>
            ExtensionFiles.All(file => File.Exists(Path.Combine(candidate, file))));
    }

    /// <summary>
    /// Reads the shipped version to detect old code still loaded by GNOME.
    /// </summary>
    /// <returns>The packaged version, or zero when metadata is unavailable.</returns>
    private static double ReadBundledVersion()
    {
        var bundle = FindBundle();
        if (bundle is null)
        {
            return 0;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(bundle, "metadata.json")));
            return document.RootElement.TryGetProperty("version", out var version)
                && version.TryGetDouble(out var number) ? number : 0;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            return 0;
        }
    }

    /// <summary>
    /// State and metadata version currently known to the running shell.
    /// </summary>
    /// <param name="State">GNOME extension lifecycle state.</param>
    /// <param name="Version">Version loaded by the current shell session.</param>
    private readonly record struct ShellExtensionState(double State, double Version);

    /// <summary>
    /// Asks the shell what state the extension is in.
    /// </summary>
    /// <param name="connection">Session bus connection.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The state and loaded version, or null when no shell answered.
    /// </returns>
    private static async Task<ShellExtensionState?> GetStateAsync(
        DBusConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            var info = await connection
                .CallMethodAsync(
                    CreateCall(connection, "GetExtensionInfo", Uuid),
                    static (Message message, object? _) =>
                    {
                        var reader = message.GetBodyReader();
                        return reader.ReadDictionaryOfStringToVariantValue();
                    })
                .WaitAsync(CallTimeout, cancellationToken);

            return new ShellExtensionState(ReadNumber(info, "state"), ReadNumber(info, "version"));
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads numeric GNOME metadata regardless of its D-Bus integer encoding.
    /// </summary>
    /// <param name="info">Metadata returned by GNOME.</param>
    /// <param name="name">Numeric field to read.</param>
    /// <returns>The field value, or zero if absent.</returns>
    private static double ReadNumber(Dictionary<string, VariantValue> info, string name) =>
        info.TryGetValue(name, out var value) ? value.Type switch
        {
            VariantValueType.Double => value.GetDouble(),
            VariantValueType.Int32 => value.GetInt32(),
            _ => 0,
        } : 0;

    /// <summary>
    /// Asks the shell to enable the extension.
    /// </summary>
    /// <param name="connection">Session bus connection.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>True when the shell accepted the request.</returns>
    private static async Task<bool> EnableAsync(
        DBusConnection connection,
        CancellationToken cancellationToken)
    {
        try
        {
            return await connection
                .CallMethodAsync(
                    CreateCall(connection, "EnableExtension", Uuid),
                    static (Message message, object? _) => message.GetBodyReader().ReadBool())
                .WaitAsync(CallTimeout, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Builds a call to the shell's extensions interface.
    /// </summary>
    /// <remarks>
    /// Separate from its callers because <c>MessageWriter</c> is a ref
    /// struct and cannot live across an await.
    /// </remarks>
    /// <param name="connection">Connection whose writer builds the message.</param>
    /// <param name="member">Method to call.</param>
    /// <param name="uuid">Extension the call is about.</param>
    /// <returns>An encoded method call ready to send.</returns>
    private static MessageBuffer CreateCall(DBusConnection connection, string member, string uuid)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: ShellService,
            path: ExtensionsPath,
            @interface: ExtensionsInterface,
            member: member,
            signature: "s");
        writer.WriteString(uuid);
        return writer.CreateMessage();
    }
}
