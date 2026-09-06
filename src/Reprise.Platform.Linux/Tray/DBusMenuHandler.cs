using System.Text;
using Tmds.DBus.Protocol;

namespace Reprise.Platform.Linux.Tray;

/// <summary>
/// Serves the tray entry's context menu over <c>com.canonical.dbusmenu</c>.
/// </summary>
/// <remarks>
/// Status notifier hosts do not draw menus themselves; they ask the item
/// for a menu object and render it from the layout this class describes.
/// The menu is fixed - open and quit - so the layout never changes and no
/// update signals are needed.
/// </remarks>
internal sealed class DBusMenuHandler : IPathMethodHandler
{
    private const string MenuInterface = "com.canonical.dbusmenu";
    private const string PropertiesInterface = "org.freedesktop.DBus.Properties";
    private const int OpenId = 1;
    private const int SeparatorId = 2;
    private const int QuitId = 3;
    private const uint Revision = 1;

    private static readonly int[] ItemIds = [OpenId, SeparatorId, QuitId];
    private static readonly string[] MenuPropertyNames = ["Version", "TextDirection", "Status", "IconThemePath"];

    private static readonly ReadOnlyMemory<byte> IntrospectionXml = Encoding.UTF8.GetBytes("""
        <interface name="com.canonical.dbusmenu">
          <property name="Version" type="u" access="read"/>
          <property name="TextDirection" type="s" access="read"/>
          <property name="Status" type="s" access="read"/>
          <property name="IconThemePath" type="as" access="read"/>
          <method name="GetLayout">
            <arg name="parentId" type="i" direction="in"/><arg name="recursionDepth" type="i" direction="in"/><arg name="propertyNames" type="as" direction="in"/>
            <arg name="revision" type="u" direction="out"/><arg name="layout" type="(ia{sv}av)" direction="out"/>
          </method>
          <method name="GetGroupProperties">
            <arg name="ids" type="ai" direction="in"/><arg name="propertyNames" type="as" direction="in"/>
            <arg name="properties" type="a(ia{sv})" direction="out"/>
          </method>
          <method name="GetProperty">
            <arg name="id" type="i" direction="in"/><arg name="name" type="s" direction="in"/><arg name="value" type="v" direction="out"/>
          </method>
          <method name="Event">
            <arg name="id" type="i" direction="in"/><arg name="eventId" type="s" direction="in"/><arg name="data" type="v" direction="in"/><arg name="timestamp" type="u" direction="in"/>
          </method>
          <method name="EventGroup">
            <arg name="events" type="a(isvu)" direction="in"/><arg name="idErrors" type="ai" direction="out"/>
          </method>
          <method name="AboutToShow"><arg name="id" type="i" direction="in"/><arg name="needUpdate" type="b" direction="out"/></method>
          <method name="AboutToShowGroup">
            <arg name="ids" type="ai" direction="in"/><arg name="updatesNeeded" type="ai" direction="out"/><arg name="idErrors" type="ai" direction="out"/>
          </method>
          <signal name="ItemsPropertiesUpdated"><arg type="a(ia{sv})"/><arg type="a(ias)"/></signal>
          <signal name="LayoutUpdated"><arg type="u"/><arg type="i"/></signal>
          <signal name="ItemActivationRequested"><arg type="i"/><arg type="u"/></signal>
        </interface>
        """);

    private readonly Action _openRequested;
    private readonly Action _quitRequested;

    /// <summary>
    /// Creates the menu with its two actions.
    /// </summary>
    /// <param name="openRequested">Runs when the open entry is clicked.</param>
    /// <param name="quitRequested">Runs when the quit entry is clicked.</param>
    public DBusMenuHandler(Action openRequested, Action quitRequested)
    {
        _openRequested = openRequested;
        _quitRequested = quitRequested;
    }

    /// <inheritdoc />
    public string Path => "/MenuBar";

    /// <inheritdoc />
    public bool HandlesChildPaths => false;

    /// <summary>
    /// Answers layout queries, property reads, and click events.
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
        else if (request.InterfaceAsString == MenuInterface)
        {
            HandleMenuMethod(context);
        }
        else
        {
            context.ReplyUnknownMethodError();
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Dispatches one <c>com.canonical.dbusmenu</c> method.
    /// </summary>
    /// <param name="context">The incoming call.</param>
    private void HandleMenuMethod(MethodContext context)
    {
        var reader = context.Request.GetBodyReader();
        switch (context.Request.MemberAsString)
        {
            case "GetLayout":
            {
                var parentId = reader.ReadInt32();
                DBusReply.Send(context, "u(ia{sv}av)", (ref MessageWriter writer) =>
                {
                    writer.WriteUInt32(Revision);
                    WriteLayout(ref writer, parentId, includeChildren: true);
                });
                break;
            }

            case "GetGroupProperties":
            {
                var requested = reader.ReadArrayOfInt32();
                var ids = requested.Length == 0 ? [0, .. ItemIds] : requested;
                DBusReply.Send(context, "a(ia{sv})", (ref MessageWriter writer) =>
                {
                    var array = writer.WriteArrayStart(DBusType.Struct);
                    foreach (var id in ids)
                    {
                        writer.WriteStructureStart();
                        writer.WriteInt32(id);
                        WriteItemProperties(ref writer, id);
                    }

                    writer.WriteArrayEnd(array);
                });
                break;
            }

            case "GetProperty":
            {
                var id = reader.ReadInt32();
                var name = reader.ReadString();
                if (!PropertyNamesFor(id).Contains(name))
                {
                    context.ReplyError("org.freedesktop.DBus.Error.InvalidArgs", $"Unknown property {name}");
                    break;
                }

                DBusReply.Send(context, "v", (ref MessageWriter writer) => WriteItemProperty(ref writer, id, name));
                break;
            }

            case "Event":
            {
                var id = reader.ReadInt32();
                var eventId = reader.ReadString();
                HandleEvent(id, eventId);
                DBusReply.SendEmpty(context);
                break;
            }

            case "EventGroup":
            {
                var end = reader.ReadArrayStart(DBusType.Struct);
                while (reader.HasNext(end))
                {
                    reader.AlignStruct();
                    var id = reader.ReadInt32();
                    var eventId = reader.ReadString();
                    reader.ReadVariantValue();
                    reader.ReadUInt32();
                    HandleEvent(id, eventId);
                }

                DBusReply.Send(context, "ai", static (ref MessageWriter writer) => writer.WriteArray(Array.Empty<int>()));
                break;
            }

            case "AboutToShow":
                DBusReply.Send(context, "b", static (ref MessageWriter writer) => writer.WriteBool(false));
                break;

            case "AboutToShowGroup":
                DBusReply.Send(context, "aiai", static (ref MessageWriter writer) =>
                {
                    writer.WriteArray(Array.Empty<int>());
                    writer.WriteArray(Array.Empty<int>());
                });
                break;

            default:
                context.ReplyUnknownMethodError();
                break;
        }
    }

    /// <summary>
    /// Serves the menu's own properties.
    /// </summary>
    /// <param name="context">The incoming call.</param>
    private static void HandleProperties(MethodContext context)
    {
        var reader = context.Request.GetBodyReader();
        switch (context.Request.MemberAsString)
        {
            case "Get":
            {
                reader.ReadString();
                var name = reader.ReadString();
                if (!MenuPropertyNames.Contains(name))
                {
                    context.ReplyError("org.freedesktop.DBus.Error.InvalidArgs", $"Unknown property {name}");
                    break;
                }

                DBusReply.Send(context, "v", (ref MessageWriter writer) => WriteMenuProperty(ref writer, name));
                break;
            }

            case "GetAll":
                DBusReply.Send(context, "a{sv}", static (ref MessageWriter writer) =>
                {
                    var dictionary = writer.WriteDictionaryStart();
                    foreach (var name in MenuPropertyNames)
                    {
                        writer.WriteDictionaryEntryStart();
                        writer.WriteString(name);
                        WriteMenuProperty(ref writer, name);
                    }

                    writer.WriteDictionaryEnd(dictionary);
                });
                break;

            case "Set":
                DBusReply.SendEmpty(context);
                break;

            default:
                context.ReplyUnknownMethodError();
                break;
        }
    }

    /// <summary>
    /// Writes one menu-level property as a variant.
    /// </summary>
    /// <param name="writer">Writer positioned where the variant goes.</param>
    /// <param name="name">Property name.</param>
    private static void WriteMenuProperty(ref MessageWriter writer, string name)
    {
        switch (name)
        {
            case "Version":
                writer.WriteVariantUInt32(3);
                break;
            case "TextDirection":
                writer.WriteVariantString("ltr");
                break;
            case "Status":
                writer.WriteVariantString("normal");
                break;
            case "IconThemePath":
                writer.WriteSignature("as");
                writer.WriteArray(Array.Empty<string>());
                break;
        }
    }

    /// <summary>
    /// Writes an item and, for the root, its children as a layout struct.
    /// </summary>
    /// <param name="writer">Writer positioned at the struct.</param>
    /// <param name="id">Item to describe; 0 is the root.</param>
    /// <param name="includeChildren">Whether to descend into the root's items.</param>
    private static void WriteLayout(ref MessageWriter writer, int id, bool includeChildren)
    {
        writer.WriteStructureStart();
        writer.WriteInt32(id);
        WriteItemProperties(ref writer, id);
        var children = writer.WriteArrayStart(DBusType.Variant);
        if (id == 0 && includeChildren)
        {
            foreach (var childId in ItemIds)
            {
                writer.WriteSignature("(ia{sv}av)");
                WriteLayout(ref writer, childId, includeChildren: false);
            }
        }

        writer.WriteArrayEnd(children);
    }

    /// <summary>
    /// Writes every property of one item as an <c>a{sv}</c>.
    /// </summary>
    /// <param name="writer">Writer positioned at the dictionary.</param>
    /// <param name="id">Item to describe.</param>
    private static void WriteItemProperties(ref MessageWriter writer, int id)
    {
        var dictionary = writer.WriteDictionaryStart();
        foreach (var name in PropertyNamesFor(id))
        {
            writer.WriteDictionaryEntryStart();
            writer.WriteString(name);
            WriteItemProperty(ref writer, id, name);
        }

        writer.WriteDictionaryEnd(dictionary);
    }

    /// <summary>
    /// Names of the properties an item publishes.
    /// </summary>
    /// <param name="id">Item to describe.</param>
    /// <returns>Property names in a stable order.</returns>
    private static string[] PropertyNamesFor(int id) => id switch
    {
        0 => ["children-display"],
        SeparatorId => ["type"],
        OpenId or QuitId => ["label", "enabled", "visible"],
        _ => [],
    };

    /// <summary>
    /// Writes one item property as a variant.
    /// </summary>
    /// <param name="writer">Writer positioned where the variant goes.</param>
    /// <param name="id">Item the property belongs to.</param>
    /// <param name="name">Property name, already known to exist for the item.</param>
    private static void WriteItemProperty(ref MessageWriter writer, int id, string name)
    {
        switch (name)
        {
            case "children-display":
                writer.WriteVariantString("submenu");
                break;
            case "type":
                writer.WriteVariantString("separator");
                break;
            case "label":
                writer.WriteVariantString(id == OpenId ? "Reprise 열기" : "Reprise 종료");
                break;
            case "enabled":
            case "visible":
                writer.WriteVariantBool(true);
                break;
        }
    }

    /// <summary>
    /// Runs the action behind a clicked item.
    /// </summary>
    /// <param name="id">Item that received the event.</param>
    /// <param name="eventId">Event kind; only <c>clicked</c> matters.</param>
    private void HandleEvent(int id, string eventId)
    {
        if (eventId != "clicked")
        {
            return;
        }

        switch (id)
        {
            case OpenId:
                _openRequested();
                break;
            case QuitId:
                _quitRequested();
                break;
        }
    }
}
