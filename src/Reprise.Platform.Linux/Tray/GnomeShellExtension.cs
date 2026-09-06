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
    /// Installing is only needed for the archive build, where the extension
    /// travels beside the application rather than in a system directory the
    /// shell already searches. A shell that has not rescanned since the
    /// files appeared cannot enable them yet, which is reported rather than
    /// treated as a failure: logging out once is all it takes.
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

            var state = await GetStateAsync(connection, cancellationToken);
            if (state is null)
            {
                return GnomeShellExtensionResult.NoShell;
            }

            if (state == EnabledState)
            {
                return GnomeShellExtensionResult.AlreadyEnabled;
            }

            var installed = state > 0 || Install();
            if (!installed)
            {
                return GnomeShellExtensionResult.Failed;
            }

            var enabled = await EnableAsync(connection, cancellationToken);
            if (!enabled)
            {
                return GnomeShellExtensionResult.Failed;
            }

            var confirmed = await GetStateAsync(connection, cancellationToken);
            return confirmed == EnabledState
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
    /// Looks beside the application, which is where the archive puts it. The
    /// Debian package installs the same files system-wide, so nothing is
    /// found and nothing is copied.
    /// </remarks>
    /// <returns>True when the extension is now in place for this user.</returns>
    private static bool Install()
    {
        var source = FindBundle();
        if (source is null)
        {
            return false;
        }

        try
        {
            var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
            if (string.IsNullOrWhiteSpace(dataHome))
            {
                dataHome = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".local",
                    "share");
            }

            var target = Path.Combine(dataHome, "gnome-shell", "extensions", Uuid);
            Directory.CreateDirectory(target);
            foreach (var file in ExtensionFiles)
            {
                File.Copy(Path.Combine(source, file), Path.Combine(target, file), overwrite: true);
            }

            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

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
        };

        return candidates.FirstOrDefault(candidate =>
            ExtensionFiles.All(file => File.Exists(Path.Combine(candidate, file))));
    }

    /// <summary>
    /// Asks the shell what state the extension is in.
    /// </summary>
    /// <param name="connection">Session bus connection.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>
    /// The state, zero when the shell does not know the extension, or null
    /// when no shell answered.
    /// </returns>
    private static async Task<double?> GetStateAsync(
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

            if (!info.TryGetValue("state", out var value))
            {
                return 0;
            }

            return value.Type switch
            {
                VariantValueType.Double => value.GetDouble(),
                VariantValueType.Int32 => value.GetInt32(),
                _ => 0,
            };
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }
    }

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
