using Tmds.DBus.Protocol;

namespace Reprise.Platform.Linux.Tray;

/// <summary>
/// Writes the body of a D-Bus message.
/// </summary>
/// <param name="writer">Writer positioned after the header.</param>
internal delegate void DBusBodyWriter(ref MessageWriter writer);

/// <summary>
/// Reply helpers shared by the exported tray objects.
/// </summary>
/// <remarks>
/// <see cref="MessageWriter"/> is a ref struct that has to be disposed and
/// cannot be captured or passed by reference from a <c>using</c> variable,
/// which makes every reply site the same eight lines. These helpers keep
/// that ceremony in one place.
/// </remarks>
internal static class DBusReply
{
    /// <summary>
    /// Sends a reply whose body the callback writes.
    /// </summary>
    /// <param name="context">The call being answered.</param>
    /// <param name="signature">Body signature.</param>
    /// <param name="body">Writes the body.</param>
    public static void Send(MethodContext context, string signature, DBusBodyWriter body)
    {
        var writer = context.CreateReplyWriter(signature);
        try
        {
            body(ref writer);
            context.Reply(writer.CreateMessage());
        }
        finally
        {
            writer.Dispose();
        }
    }

    /// <summary>
    /// Sends a reply with no body, unless the caller asked for none.
    /// </summary>
    /// <param name="context">The call being answered.</param>
    public static void SendEmpty(MethodContext context)
    {
        if (context.NoReplyExpected)
        {
            return;
        }

        var writer = context.CreateReplyWriter(null!);
        try
        {
            context.Reply(writer.CreateMessage());
        }
        finally
        {
            writer.Dispose();
        }
    }

    /// <summary>
    /// Broadcasts a signal from an exported object.
    /// </summary>
    /// <remarks>
    /// A signal that cannot be sent - the connection closed under us, say -
    /// is dropped: the host will simply re-read the properties later, and a
    /// tray hiccup must never take the panel down.
    /// </remarks>
    /// <param name="connection">Connection to send on.</param>
    /// <param name="path">Object the signal comes from.</param>
    /// <param name="interfaceName">Interface the signal belongs to.</param>
    /// <param name="member">Signal name.</param>
    /// <param name="signature">Body signature, or null when there is no body.</param>
    /// <param name="body">Writes the body, or null when there is none.</param>
    public static void Emit(
        DBusConnection connection,
        string path,
        string interfaceName,
        string member,
        string? signature,
        DBusBodyWriter? body)
    {
        try
        {
            var writer = connection.GetMessageWriter();
            try
            {
                writer.WriteSignalHeader(
                    destination: null,
                    path: path,
                    @interface: interfaceName,
                    member: member,
                    signature: signature);
                body?.Invoke(ref writer);
                connection.TrySendMessage(writer.CreateMessage());
            }
            finally
            {
                writer.Dispose();
            }
        }
        catch (Exception exception) when (exception is InvalidOperationException or ObjectDisposedException or DBusExceptionBase)
        {
        }
    }
}
