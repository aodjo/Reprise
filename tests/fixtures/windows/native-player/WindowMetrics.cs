using System.Runtime.InteropServices;
using System.Text;
using Avalonia.Controls;

/// <summary>Measures the diagnostic's own HWND and client origin in physical screen pixels.</summary>
internal static class WindowMetrics
{
    /// <summary>Reports actual Win32 geometry independently of Avalonia's requested position.</summary>
    /// <param name="window">The fixture's own lyric window.</param>
    internal static object Read(Window window)
    {
        var handle = window.TryGetPlatformHandle()!.Handle;
        GetWindowRect(handle, out var outer);
        GetClientRect(handle, out var client);
        var origin = new Point();
        ClientToScreen(handle, ref origin);
        var ownerClass = new StringBuilder(256);
        GetClassName(GetWindow(handle, 4), ownerClass, ownerClass.Capacity);
        return new
        {
            Outer = new { outer.Left, outer.Top, outer.Right, outer.Bottom },
            Client = new { client.Left, client.Top, client.Right, client.Bottom },
            Origin = new { origin.X, origin.Y }, window.ClientSize, window.FrameSize,
            OwnerClass = ownerClass.ToString(),
        };
    }

    /// <summary>Native rectangle storage with serializable coordinates.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rectangle { public int Left, Top, Right, Bottom; }
    /// <summary>Native point storage with serializable coordinates.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Point { public int X, Y; }
    /// <summary>Reads the fixture's native outer bounds.</summary>
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint handle, out Rectangle bounds);
    /// <summary>Reads the fixture's client dimensions.</summary>
    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint handle, out Rectangle bounds);
    /// <summary>Maps the fixture's client origin into screen pixels.</summary>
    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(nint handle, ref Point point);
    /// <summary>Reads only the diagnostic window's top-level owner.</summary>
    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint window, uint command);
    /// <summary>Identifies the native owner class without reading its UI content.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    private static extern int GetClassName(nint window, StringBuilder name, int capacity);
}
