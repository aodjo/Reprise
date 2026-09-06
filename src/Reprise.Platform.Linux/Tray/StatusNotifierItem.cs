using System.Text;
using Reprise.Desktop;
using Reprise.Platform.Linux.Mpris;
using Tmds.DBus.Protocol;

namespace Reprise.Platform.Linux.Tray;

/// <summary>
/// Reprise's tray entry, spoken directly over the StatusNotifierItem protocol.
/// </summary>
/// <remarks>
/// Avalonia already ships a tray icon for Linux, but it cannot show text
/// beside the icon. The Ubuntu extension of the protocol can - the
/// <c>XAyatanaLabel</c> property, which Ubuntu's GNOME, Budgie, MATE, and
/// Xfce panels display - and that label is how the track title and lyrics
/// reach the top bar, as they do in the macOS menu bar. So this class
/// exports the item itself: the <c>org.kde.StatusNotifierItem</c> object,
/// its <c>com.canonical.dbusmenu</c> context menu, and the registration
/// with the desktop's watcher.
/// <para>
/// The item owns a dedicated bus connection rather than sharing the
/// process-wide one the MPRIS client uses: Tmds.DBus reserves that shared
/// connection for client calls and refuses to export objects or send
/// signals on it. Method calls arrive on the connection's thread; the
/// events this class raises are therefore not on the UI thread.
/// </para>
/// </remarks>
public sealed class StatusNotifierItem : IStatusItem
{
    private const string ItemInterface = "org.kde.StatusNotifierItem";
    private const string ItemPath = "/StatusNotifierItem";
    private const string MenuPath = "/MenuBar";
    private const string WatcherService = "org.kde.StatusNotifierWatcher";
    private const string WatcherPath = "/StatusNotifierWatcher";

    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);

    private readonly ItemHandler _item;
    private readonly DBusMenuHandler _menu;
    private DBusConnection? _connection;
    private string? _serviceName;
    private bool _disposed;

    /// <summary>
    /// Creates the item; nothing touches the bus until <see cref="StartAsync"/>.
    /// </summary>
    public StatusNotifierItem()
    {
        _item = new ItemHandler(this);
        _menu = new DBusMenuHandler(
            openRequested: () => OpenRequested?.Invoke(this, EventArgs.Empty),
            quitRequested: () => QuitRequested?.Invoke(this, EventArgs.Empty));
    }

    /// <inheritdoc />
    public event EventHandler? Activated;

    /// <inheritdoc />
    public event EventHandler? OpenRequested;

    /// <inheritdoc />
    public event EventHandler? QuitRequested;

    /// <summary>
    /// Exports the item on the session bus and registers it with the watcher.
    /// </summary>
    /// <remarks>
    /// The well-known name follows the <c>org.kde.StatusNotifierItem-PID-N</c>
    /// convention the specification suggests, which every host accepts. If
    /// no watcher is running yet - the panel starts after login, or the
    /// user's desktop needs an extension - the item stays exported and
    /// registers itself the moment a watcher appears.
    /// </remarks>
    /// <param name="cancellationToken">Cancels the registration.</param>
    /// <returns>True when a watcher accepted the item right away.</returns>
    /// <exception cref="MprisUnavailableException">
    /// Thrown when the session bus itself is unreachable.
    /// </exception>
    public async Task<bool> StartAsync(CancellationToken cancellationToken = default)
    {
        var address = DBusAddress.Session
            ?? throw new MprisUnavailableException();
        var connection = new DBusConnection(address);
        try
        {
            await connection.ConnectAsync();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            connection.Dispose();
            throw new MprisUnavailableException(exception);
        }

        _connection = connection;
        _serviceName = $"org.kde.StatusNotifierItem-{Environment.ProcessId}-1";
        try
        {
            await connection.RequestNameAsync(_serviceName, RequestNameOptions.None);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _serviceName = connection.UniqueName;
        }

        connection.AddMethodHandler(_item);
        connection.AddMethodHandler(_menu);

        var rule = new MatchRule
        {
            Type = MessageType.Signal,
            Sender = "org.freedesktop.DBus",
            Interface = "org.freedesktop.DBus",
            Member = "NameOwnerChanged",
            Arg0 = WatcherService,
        };
        await connection.AddMatchAsync(
            rule,
            static (Message message, object? _) =>
            {
                var reader = message.GetBodyReader();
                reader.ReadString();
                reader.ReadString();
                return reader.ReadString();
            },
            (Notification<string> notification) =>
            {
                if (notification.Exception is null && notification.HasValue && notification.Value.Length > 0)
                {
                    _ = RegisterAsync(CancellationToken.None);
                }
            },
            emitOnCapturedContext: false);

        return await RegisterAsync(cancellationToken);
    }

    /// <summary>
    /// Publishes new label, tooltip, and icon content to the host.
    /// </summary>
    /// <remarks>
    /// Hosts re-read a property only after the matching change signal, so
    /// one is emitted per part that actually changed.
    /// </remarks>
    /// <param name="state">Content to show.</param>
    public void Update(StatusItemState state)
    {
        ArgumentNullException.ThrowIfNull(state);
        var previous = _item.State;
        _item.State = state;
        if (_connection is not { } connection || _disposed)
        {
            return;
        }

        if (previous.Label != state.Label || previous.LabelGuide != state.LabelGuide)
        {
            DBusReply.Emit(connection, ItemPath, ItemInterface, "XAyatanaNewLabel", "ss", (ref MessageWriter writer) =>
            {
                writer.WriteString(state.Label);
                writer.WriteString(state.LabelGuide);
            });
        }

        if (previous.ToolTip != state.ToolTip)
        {
            DBusReply.Emit(connection, ItemPath, ItemInterface, "NewToolTip", null, null);
        }

        if (!ReferenceEquals(previous.Icons, state.Icons))
        {
            DBusReply.Emit(connection, ItemPath, ItemInterface, "NewIcon", null, null);
        }
    }

    /// <summary>
    /// Withdraws the item from the bus.
    /// </summary>
    /// <remarks>
    /// Closing the connection drops the well-known name with it, which is
    /// what makes hosts remove the entry rather than keep a dead icon.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_connection is { } connection)
        {
            _connection = null;
            connection.Dispose();
        }
    }

    /// <summary>
    /// Asks the watcher to take the item.
    /// </summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>True when the watcher accepted; false when none is running.</returns>
    private async Task<bool> RegisterAsync(CancellationToken cancellationToken)
    {
        if (_connection is not { } connection || _serviceName is not { } serviceName || _disposed)
        {
            return false;
        }

        try
        {
            await connection
                .CallMethodAsync(CreateRegisterMessage(connection, serviceName))
                .WaitAsync(CallTimeout, cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>
    /// Builds the <c>RegisterStatusNotifierItem</c> call.
    /// </summary>
    /// <param name="connection">Connection whose writer allocates the buffer.</param>
    /// <param name="serviceName">Name the item is exported under.</param>
    /// <returns>An encoded method call ready to send.</returns>
    private static MessageBuffer CreateRegisterMessage(DBusConnection connection, string serviceName)
    {
        using var writer = connection.GetMessageWriter();
        writer.WriteMethodCallHeader(
            destination: WatcherService,
            path: WatcherPath,
            @interface: WatcherService,
            member: "RegisterStatusNotifierItem",
            signature: "s");
        writer.WriteString(serviceName);
        return writer.CreateMessage();
    }

    /// <summary>
    /// Serves the <c>org.kde.StatusNotifierItem</c> object.
    /// </summary>
    private sealed class ItemHandler : IPathMethodHandler
    {
        private const string PropertiesInterface = "org.freedesktop.DBus.Properties";

        private static readonly string[] PropertyNames =
        [
            "Category", "Id", "Title", "Status", "WindowId", "IconName", "IconPixmap",
            "OverlayIconName", "OverlayIconPixmap", "AttentionIconName", "AttentionIconPixmap",
            "AttentionMovieName", "ToolTip", "IconThemePath", "ItemIsMenu", "Menu",
            "XAyatanaLabel", "XAyatanaLabelGuide", "XAyatanaOrderingIndex",
        ];

        private static readonly ReadOnlyMemory<byte> IntrospectionXml = Encoding.UTF8.GetBytes("""
            <interface name="org.kde.StatusNotifierItem">
              <property name="Category" type="s" access="read"/>
              <property name="Id" type="s" access="read"/>
              <property name="Title" type="s" access="read"/>
              <property name="Status" type="s" access="read"/>
              <property name="WindowId" type="u" access="read"/>
              <property name="IconName" type="s" access="read"/>
              <property name="IconPixmap" type="a(iiay)" access="read"/>
              <property name="OverlayIconName" type="s" access="read"/>
              <property name="OverlayIconPixmap" type="a(iiay)" access="read"/>
              <property name="AttentionIconName" type="s" access="read"/>
              <property name="AttentionIconPixmap" type="a(iiay)" access="read"/>
              <property name="AttentionMovieName" type="s" access="read"/>
              <property name="ToolTip" type="(sa(iiay)ss)" access="read"/>
              <property name="IconThemePath" type="s" access="read"/>
              <property name="ItemIsMenu" type="b" access="read"/>
              <property name="Menu" type="o" access="read"/>
              <property name="XAyatanaLabel" type="s" access="read"/>
              <property name="XAyatanaLabelGuide" type="s" access="read"/>
              <property name="XAyatanaOrderingIndex" type="u" access="read"/>
              <method name="ContextMenu"><arg name="x" type="i" direction="in"/><arg name="y" type="i" direction="in"/></method>
              <method name="Activate"><arg name="x" type="i" direction="in"/><arg name="y" type="i" direction="in"/></method>
              <method name="SecondaryActivate"><arg name="x" type="i" direction="in"/><arg name="y" type="i" direction="in"/></method>
              <method name="Scroll"><arg name="delta" type="i" direction="in"/><arg name="orientation" type="s" direction="in"/></method>
              <method name="ProvideXdgActivationToken"><arg name="token" type="s" direction="in"/></method>
              <signal name="NewTitle"/>
              <signal name="NewIcon"/>
              <signal name="NewAttentionIcon"/>
              <signal name="NewOverlayIcon"/>
              <signal name="NewToolTip"/>
              <signal name="NewStatus"><arg name="status" type="s"/></signal>
              <signal name="XAyatanaNewLabel"><arg name="label" type="s"/><arg name="guide" type="s"/></signal>
            </interface>
            """);

        private readonly StatusNotifierItem _owner;

        /// <summary>
        /// Creates the handler for its owning item.
        /// </summary>
        /// <param name="owner">Item whose events to raise.</param>
        public ItemHandler(StatusNotifierItem owner)
        {
            _owner = owner;
        }

        /// <summary>
        /// Content currently served to hosts.
        /// </summary>
        public StatusItemState State { get; set; } = new(string.Empty, string.Empty, "Reprise", []);

        /// <inheritdoc />
        public string Path => ItemPath;

        /// <inheritdoc />
        public bool HandlesChildPaths => false;

        /// <summary>
        /// Answers property reads, transport methods, and introspection.
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
            else if (request.InterfaceAsString == ItemInterface)
            {
                HandleItemMethod(context);
            }
            else
            {
                context.ReplyUnknownMethodError();
            }

            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Dispatches one <c>org.kde.StatusNotifierItem</c> method.
        /// </summary>
        /// <param name="context">The incoming call.</param>
        private void HandleItemMethod(MethodContext context)
        {
            switch (context.Request.MemberAsString)
            {
                case "Activate":
                case "SecondaryActivate":
                    _owner.Activated?.Invoke(_owner, EventArgs.Empty);
                    DBusReply.SendEmpty(context);
                    break;
                case "ContextMenu":
                case "Scroll":
                case "ProvideXdgActivationToken":
                    DBusReply.SendEmpty(context);
                    break;
                default:
                    context.ReplyUnknownMethodError();
                    break;
            }
        }

        /// <summary>
        /// Serves <c>Get</c>, <c>GetAll</c>, and a no-op <c>Set</c>.
        /// </summary>
        /// <param name="context">The incoming call.</param>
        private void HandleProperties(MethodContext context)
        {
            var reader = context.Request.GetBodyReader();
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

                    var state = State;
                    DBusReply.Send(context, "v", (ref MessageWriter writer) => WriteProperty(ref writer, state, name));
                    break;
                }

                case "GetAll":
                {
                    var state = State;
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
                }

                case "Set":
                    DBusReply.SendEmpty(context);
                    break;

                default:
                    context.ReplyUnknownMethodError();
                    break;
            }
        }

        /// <summary>
        /// Writes one property as a variant.
        /// </summary>
        /// <param name="writer">Writer positioned where the variant goes.</param>
        /// <param name="state">Content being served.</param>
        /// <param name="name">Property to write.</param>
        private static void WriteProperty(ref MessageWriter writer, StatusItemState state, string name)
        {
            switch (name)
            {
                case "Category":
                    writer.WriteVariantString("ApplicationStatus");
                    break;
                case "Id":
                    writer.WriteVariantString("reprise");
                    break;
                case "Title":
                    writer.WriteVariantString("Reprise");
                    break;
                case "Status":
                    writer.WriteVariantString("Active");
                    break;
                case "WindowId":
                case "XAyatanaOrderingIndex":
                    writer.WriteVariantUInt32(0);
                    break;
                case "IconName":
                case "OverlayIconName":
                case "AttentionIconName":
                case "AttentionMovieName":
                case "IconThemePath":
                    writer.WriteVariantString(string.Empty);
                    break;
                case "IconPixmap":
                    writer.WriteSignature("a(iiay)");
                    WritePixmaps(ref writer, state.Icons);
                    break;
                case "OverlayIconPixmap":
                case "AttentionIconPixmap":
                    writer.WriteSignature("a(iiay)");
                    WritePixmaps(ref writer, []);
                    break;
                case "ToolTip":
                    writer.WriteSignature("(sa(iiay)ss)");
                    writer.WriteStructureStart();
                    writer.WriteString(string.Empty);
                    WritePixmaps(ref writer, []);
                    writer.WriteString("Reprise");
                    writer.WriteString(state.ToolTip);
                    break;
                case "ItemIsMenu":
                    writer.WriteVariantBool(false);
                    break;
                case "Menu":
                    writer.WriteVariantObjectPath(MenuPath);
                    break;
                case "XAyatanaLabel":
                    writer.WriteVariantString(state.Label);
                    break;
                case "XAyatanaLabelGuide":
                    writer.WriteVariantString(state.LabelGuide);
                    break;
            }
        }

        /// <summary>
        /// Writes an <c>a(iiay)</c> pixmap list.
        /// </summary>
        /// <param name="writer">Writer positioned at the array.</param>
        /// <param name="icons">Icons to encode.</param>
        private static void WritePixmaps(ref MessageWriter writer, IReadOnlyList<StatusItemIcon> icons)
        {
            var array = writer.WriteArrayStart(DBusType.Struct);
            foreach (var icon in icons)
            {
                writer.WriteStructureStart();
                writer.WriteInt32(icon.Width);
                writer.WriteInt32(icon.Height);
                writer.WriteArray(icon.Argb);
            }

            writer.WriteArrayEnd(array);
        }
    }
}
