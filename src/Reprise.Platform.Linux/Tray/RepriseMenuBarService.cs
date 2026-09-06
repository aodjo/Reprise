using System.Text;
using Reprise.Desktop;
using Reprise.Platform.Linux.Mpris;
using Tmds.DBus.Protocol;

namespace Reprise.Platform.Linux.Tray;

/// <summary>
/// Publishes what the top bar should show, for the Reprise shell extension.
/// </summary>
/// <remarks>
/// A tray label is a string the desktop panel draws, so it can only ever
/// step a whole character at a time. A shell extension draws the item
/// itself and can scroll it pixel by pixel, exactly as the macOS menu bar
/// does - but it needs the raw material rather than a finished label. This
/// service hands it over on the session bus: the whole line, the cover as
/// a PNG, and the user's scrolling settings, with a change signal so the
/// extension redraws only when something moved.
/// <para>
/// It runs alongside <see cref="StatusNotifierItem"/> rather than instead
/// of it. Desktops without the extension keep the ordinary tray entry, and
/// the extension hides that entry while it is running.
/// </para>
/// </remarks>
public sealed class RepriseMenuBarService : IMenuBarPublisher
{
    /// <summary>
    /// Bus name the extension looks for.
    /// </summary>
    public const string ServiceName = "dev.junx.Reprise";

    /// <summary>
    /// Object the extension talks to.
    /// </summary>
    public const string ObjectPath = "/dev/junx/Reprise";

    /// <summary>
    /// Interface carrying the top-bar content.
    /// </summary>
    public const string MenuBarInterface = "dev.junx.Reprise.MenuBar";

    private const string PropertiesInterface = "org.freedesktop.DBus.Properties";

    private readonly Handler _handler;
    private DBusConnection? _connection;
    private bool _disposed;

    /// <summary>
    /// Creates the service; nothing touches the bus until <see cref="StartAsync"/>.
    /// </summary>
    public RepriseMenuBarService()
    {
        _handler = new Handler(this);
    }

    /// <inheritdoc />
    public event EventHandler? Activated;

    /// <summary>
    /// Claims the bus name and exports the object.
    /// </summary>
    /// <remarks>
    /// Uses a connection of its own, as Tmds.DBus reserves the shared
    /// session connection for client calls and refuses to export on it.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the start-up.</param>
    /// <returns>A task that completes once the object is exported.</returns>
    /// <exception cref="MprisUnavailableException">
    /// Thrown when the session bus is unreachable.
    /// </exception>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var address = DBusAddress.Session ?? throw new MprisUnavailableException();
        var connection = new DBusConnection(address);
        try
        {
            await connection.ConnectAsync();
            await connection.RequestNameAsync(ServiceName, RequestNameOptions.ReplaceExisting);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            connection.Dispose();
            throw new MprisUnavailableException(exception);
        }

        connection.AddMethodHandler(_handler);
        _connection = connection;
    }

    /// <summary>
    /// Publishes new content and tells the extension what changed.
    /// </summary>
    /// <remarks>
    /// The signal carries only the properties that actually differ, so a
    /// still label costs one comparison and no bus traffic, while a
    /// scrolling one sends just its text.
    /// </remarks>
    /// <param name="state">What the top bar should show.</param>
    public void Publish(MenuBarState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var previous = _handler.State;
        _handler.State = state;
        if (_connection is not { } connection || _disposed)
        {
            return;
        }

        var changed = new List<string>(4);
        if (previous.Text != state.Text) changed.Add("Text");
        if (previous.ToolTip != state.ToolTip) changed.Add("ToolTip");
        if (!previous.IconPng.AsSpan().SequenceEqual(state.IconPng)) changed.Add("IconPng");
        if (previous.IsRunning != state.IsRunning) changed.Add("IsRunning");
        if (previous.IsPlaying != state.IsPlaying) changed.Add("IsPlaying");
        if (previous.ScrollsText != state.ScrollsText) changed.Add("ScrollsText");
        if (Math.Abs(previous.PointsPerSecond - state.PointsPerSecond) > 0.01) changed.Add("PointsPerSecond");
        if (previous.MaxWidthChars != state.MaxWidthChars) changed.Add("MaxWidthChars");
        if (changed.Count == 0)
        {
            return;
        }

        DBusReply.Emit(connection, ObjectPath, PropertiesInterface, "PropertiesChanged", "sa{sv}as",
            (ref MessageWriter writer) =>
            {
                writer.WriteString(MenuBarInterface);
                var dictionary = writer.WriteDictionaryStart();
                foreach (var name in changed)
                {
                    writer.WriteDictionaryEntryStart();
                    writer.WriteString(name);
                    Handler.WriteProperty(ref writer, state, name);
                }

                writer.WriteDictionaryEnd(dictionary);
                writer.WriteArray(Array.Empty<string>());
            });
    }

    /// <summary>
    /// Releases the bus name and stops serving.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connection?.Dispose();
        _connection = null;
    }

    /// <summary>
    /// Serves the exported object.
    /// </summary>
    private sealed class Handler : IPathMethodHandler
    {
        private static readonly string[] PropertyNames =
        [
            "Text", "ToolTip", "IconPng", "IsRunning", "IsPlaying",
            "ScrollsText", "PointsPerSecond", "MaxWidthChars",
        ];

        private static readonly ReadOnlyMemory<byte> IntrospectionXml = Encoding.UTF8.GetBytes("""
            <interface name="dev.junx.Reprise.MenuBar">
              <property name="Text" type="s" access="read"/>
              <property name="ToolTip" type="s" access="read"/>
              <property name="IconPng" type="ay" access="read"/>
              <property name="IsRunning" type="b" access="read"/>
              <property name="IsPlaying" type="b" access="read"/>
              <property name="ScrollsText" type="b" access="read"/>
              <property name="PointsPerSecond" type="d" access="read"/>
              <property name="MaxWidthChars" type="i" access="read"/>
              <method name="Activate"/>
            </interface>
            """);

        private readonly RepriseMenuBarService _owner;

        /// <summary>
        /// Creates the handler for its owning service.
        /// </summary>
        /// <param name="owner">Service whose events to raise.</param>
        public Handler(RepriseMenuBarService owner)
        {
            _owner = owner;
        }

        /// <summary>
        /// Content currently served.
        /// </summary>
        public MenuBarState State { get; set; } =
            new(string.Empty, "Reprise", [], false, false, true, 30, 20);

        /// <inheritdoc />
        public string Path => ObjectPath;

        /// <inheritdoc />
        public bool HandlesChildPaths => false;

        /// <summary>
        /// Answers property reads, the activation call, and introspection.
        /// </summary>
        /// <param name="context">The incoming call.</param>
        /// <returns>A completed task; every reply is written synchronously.</returns>
        public ValueTask HandleMethodAsync(MethodContext context)
        {
            var request = context.Request;
            if (context.IsDBusIntrospectRequest)
            {
                context.ReplyIntrospectXml([IntrospectionXml], Array.Empty<string>());
            }
            else if (request.InterfaceAsString == PropertiesInterface)
            {
                HandleProperties(context);
            }
            else if (request.InterfaceAsString == MenuBarInterface && request.MemberAsString == "Activate")
            {
                _owner.Activated?.Invoke(_owner, EventArgs.Empty);
                DBusReply.SendEmpty(context);
            }
            else
            {
                context.ReplyUnknownMethodError();
            }

            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Writes one property as a variant.
        /// </summary>
        /// <param name="writer">Writer positioned where the variant goes.</param>
        /// <param name="state">Content being served.</param>
        /// <param name="name">Property to write.</param>
        public static void WriteProperty(ref MessageWriter writer, MenuBarState state, string name)
        {
            switch (name)
            {
                case "Text":
                    writer.WriteVariantString(state.Text);
                    break;
                case "ToolTip":
                    writer.WriteVariantString(state.ToolTip);
                    break;
                case "IconPng":
                    writer.WriteSignature("ay");
                    writer.WriteArray(state.IconPng);
                    break;
                case "IsRunning":
                    writer.WriteVariantBool(state.IsRunning);
                    break;
                case "IsPlaying":
                    writer.WriteVariantBool(state.IsPlaying);
                    break;
                case "ScrollsText":
                    writer.WriteVariantBool(state.ScrollsText);
                    break;
                case "PointsPerSecond":
                    writer.WriteVariantDouble(state.PointsPerSecond);
                    break;
                case "MaxWidthChars":
                    writer.WriteVariantInt32(state.MaxWidthChars);
                    break;
            }
        }

        /// <summary>
        /// Serves <c>Get</c> and <c>GetAll</c>.
        /// </summary>
        /// <param name="context">The incoming call.</param>
        private void HandleProperties(MethodContext context)
        {
            var reader = context.Request.GetBodyReader();
            var state = State;
            switch (context.Request.MemberAsString)
            {
                case "Get":
                {
                    reader.ReadString();
                    var name = reader.ReadString();
                    if (Array.IndexOf(PropertyNames, name) < 0)
                    {
                        context.ReplyError("org.freedesktop.DBus.Error.InvalidArgs", $"Unknown property {name}");
                        break;
                    }

                    DBusReply.Send(context, "v", (ref MessageWriter writer) => WriteProperty(ref writer, state, name));
                    break;
                }

                case "GetAll":
                    DBusReply.Send(context, "a{sv}", (ref MessageWriter writer) =>
                    {
                        var dictionary = writer.WriteDictionaryStart();
                        foreach (var name in PropertyNames)
                        {
                            writer.WriteDictionaryEntryStart();
                            writer.WriteString(name);
                            WriteProperty(ref writer, state, name);
                        }

                        writer.WriteDictionaryEnd(dictionary);
                    });
                    break;

                default:
                    context.ReplyUnknownMethodError();
                    break;
            }
        }
    }
}
