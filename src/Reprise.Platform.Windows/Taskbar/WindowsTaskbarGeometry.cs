using System.Runtime.InteropServices;
using System.Text;

namespace Reprise.Platform.Windows.Taskbar;

/// <summary>Screen-pixel rectangle used for native taskbar placement.</summary>
/// <param name="Left">Left screen coordinate.</param>
/// <param name="Top">Top screen coordinate.</param>
/// <param name="Right">Exclusive right coordinate.</param>
/// <param name="Bottom">Exclusive bottom coordinate.</param>
internal readonly record struct TaskbarRect(int Left, int Top, int Right, int Bottom)
{
    /// <summary>Width in physical pixels.</summary>
    internal int Width => Right - Left;
    /// <summary>Height in physical pixels.</summary>
    internal int Height => Bottom - Top;
}

/// <summary>Native shell geometry, without changing or resizing Explorer windows.</summary>
/// <param name="Bar">Primary taskbar bounds.</param>
/// <param name="Tray">Notification area bounds, excluded from the lyric strip.</param>
/// <param name="TaskList">Task-list bounds when the shell exposes them.</param>
/// <param name="Monitor">Monitor bounds for visibility and fullscreen detection.</param>
/// <param name="Dpi">Taskbar DPI.</param>
internal sealed record TaskbarFrame(TaskbarRect Bar, TaskbarRect Tray, TaskbarRect? TaskList,
    TaskbarRect Monitor, uint Dpi);

/// <summary>Reads shell geometry and positions only Reprise's own nonactivating window.</summary>
internal static class WindowsTaskbarGeometry
{
    /// <summary>Reads the current primary taskbar, resolving handles again after Explorer restarts.</summary>
    internal static TaskbarFrame? Read()
    {
        var bar = FindWindow("Shell_TrayWnd", null);
        if (bar == 0 || !IsWindowVisible(bar) || !GetWindowRect(bar, out var barRect)) return null;
        var tray = FindChild(bar, "TrayNotifyWnd");
        GetWindowRect(tray, out var trayRect);
        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(bar, 2), ref monitorInfo)) return null;
        var list = FindChild(bar, "MSTaskListWClass");
        TaskbarRect? taskList = list != 0 && GetWindowRect(list, out var listRect) ? Convert(listRect) : null;
        var accessible = TaskbarAccessibility.Resolve(Convert(barRect), TaskbarAccessibility.Read(bar));
        var notification = accessible.Tray ?? (trayRect.Right > trayRect.Left ? Convert(trayRect) : (TaskbarRect?)null);
        if (notification is null) return null;
        return new(Convert(barRect), notification.Value, accessible.TaskList ?? taskList,
            Convert(monitorInfo.Monitor), GetDpiForWindow(bar));
    }

    /// <summary>Returns native child rectangles for the interactive platform diagnostic.</summary>
    internal static object[] Diagnose()
    {
        var root = FindWindow("Shell_TrayWnd", null);
        var result = new List<object>();
        if (root != 0) EnumChildWindows(root, (child, _) =>
        {
            var name = new StringBuilder(256);
            GetClassName(child, name, name.Capacity);
            GetWindowRect(child, out var bounds);
            result.Add(new { Class = name.ToString(), Bounds = Convert(bounds), Visible = IsWindowVisible(child) });
            return true;
        }, 0);
        if (root != 0) result.AddRange(TaskbarAccessibility.Read(root));
        return result.ToArray();
    }

    /// <summary>Hides the strip while another foreground window covers its entire monitor.</summary>
    /// <param name="monitor">Monitor containing the taskbar.</param>
    internal static bool IsFullscreen(TaskbarRect monitor)
    {
        var foreground = GetForegroundWindow();
        if (foreground == 0) return false;
        var name = new StringBuilder(256);
        GetClassName(foreground, name, name.Capacity);
        if (name.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        GetWindowThreadProcessId(foreground, out var process);
        if (process == Environment.ProcessId || !GetWindowRect(foreground, out var bounds)) return false;
        return bounds.Left <= monitor.Left && bounds.Top <= monitor.Top &&
            bounds.Right >= monitor.Right && bounds.Bottom >= monitor.Bottom;
    }

    /// <summary>Prevents focus stealing and keeps the strip out of Alt+Tab and the task list.</summary>
    /// <param name="window">Reprise's own window handle.</param>
    internal static void Configure(nint window)
    {
        var style = GetWindowLong(window, -20) | 0x08000000 | 0x00000080;
        SetWindowLong(window, -20, style & ~0x00040000);
    }

    /// <summary>Raises only Reprise's strip without taking keyboard focus.</summary>
    /// <param name="window">Reprise's own window handle.</param>
    internal static void Raise(nint window) => SetWindowPos(window, new nint(-1), 0, 0, 0, 0, 0x0013);

    /// <summary>Finds a shell child by class without relying on localized captions.</summary>
    private static nint FindChild(nint parent, string className)
    {
        nint found = 0;
        EnumChildWindows(parent, (child, _) =>
        {
            var name = new StringBuilder(256);
            GetClassName(child, name, name.Capacity);
            if (name.ToString() != className) return true;
            found = child;
            return false;
        }, 0);
        return found;
    }

    /// <summary>Copies a Win32 RECT into an immutable screen rectangle.</summary>
    private static TaskbarRect Convert(NativeRect value) => new(value.Left, value.Top, value.Right, value.Bottom);

    /// <summary>Native RECT storage.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left, Top, Right, Bottom; }
    /// <summary>Native monitor bounds and work area.</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo { public int Size; public NativeRect Monitor, Work; public uint Flags; }
    /// <summary>Native child-enumeration callback.</summary>
    private delegate bool EnumCallback(nint window, nint parameter);
    /// <summary>Resolves a top-level shell window.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "FindWindowW")]
    private static extern nint FindWindow(string className, string? title);
    /// <summary>Enumerates native descendants.</summary>
    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(nint parent, EnumCallback callback, nint parameter);
    /// <summary>Reads a native window class.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetClassNameW")]
    private static extern int GetClassName(nint window, StringBuilder name, int count);
    /// <summary>Reads native screen bounds.</summary>
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint window, out NativeRect rect);
    /// <summary>Checks native visibility.</summary>
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint window);
    /// <summary>Reads taskbar DPI.</summary>
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint window);
    /// <summary>Resolves the taskbar's monitor.</summary>
    [DllImport("user32.dll")]
    private static extern nint MonitorFromWindow(nint window, uint flags);
    /// <summary>Reads monitor coordinates.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    /// <summary>Reads the current foreground window.</summary>
    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();
    /// <summary>Reads the foreground process identity.</summary>
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint window, out uint process);
    /// <summary>Reads extended style bits.</summary>
    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(nint window, int index);
    /// <summary>Changes extended styles on Reprise's own window.</summary>
    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong(nint window, int index, int value);
    /// <summary>Changes only the strip's z-order.</summary>
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint window, nint after, int x, int y, int width, int height, uint flags);
}
