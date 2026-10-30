using System.Runtime.InteropServices;

namespace Reprise.Platform.Windows.Glass;

/// <summary>Owns a click-through background HWND; it never changes other applications or their capture settings.</summary>
internal static class GlassNativeWindow
{
    /// <summary>Registered callback retained for the entire process lifetime.</summary>
    private static readonly WindowProcedure Procedure = HandleMessage;
    /// <summary>Process-specific native class name, preventing collisions with other installed stages.</summary>
    private static readonly string ClassName = "Reprise.Glass." + Environment.ProcessId;
    private static bool _registered;

    /// <summary>Creates a no-activation compositor target that stays out of the taskbar and Alt+Tab.</summary>
    internal static nint Create()
    {
        if (!_registered)
        {
            var definition = new WindowClass
            {
                Size = (uint)Marshal.SizeOf<WindowClass>(), Instance = GetModuleHandle(null),
                Procedure = Marshal.GetFunctionPointerForDelegate(Procedure), ClassName = ClassName,
            };
            if (RegisterClassEx(ref definition) == 0) throw new System.ComponentModel.Win32Exception();
            _registered = true;
        }
        var handle = CreateWindowEx(0x08000000 | 0x00000080 | 0x00000020 | 0x00000008,
            ClassName, "Reprise refractive backdrop", 0x80000000 | 0x02000000, 0, 0, 1, 1, 0, 0, GetModuleHandle(null), 0);
        if (handle == 0) throw new System.ComponentModel.Win32Exception();
        return handle;
    }

    /// <summary>Positions only the owned backdrop directly behind its foreground panel.</summary>
    internal static void Place(nint backdrop, nint foreground, Rectangle bounds) =>
        SetWindowPos(backdrop, foreground, bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top, 0x0010 | 0x0040);
    /// <summary>Hides the background when the panel is hidden or another theme is selected.</summary>
    internal static void Hide(nint handle) => ShowWindow(handle, 0);
    /// <summary>Destroys only the owned native backdrop.</summary>
    internal static void Destroy(nint handle) => DestroyWindow(handle);
    /// <summary>Reads actual foreground bounds for exact physical-pixel alignment.</summary>
    internal static Rectangle Bounds(nint handle)
    {
        if (!GetWindowRect(handle, out var rectangle)) throw new System.ComponentModel.Win32Exception();
        return rectangle;
    }
    /// <summary>Reads the foreground panel's physical DPI.</summary>
    internal static uint Dpi(nint handle) => GetDpiForWindow(handle);
    /// <summary>Checks whether another top-level window has moved between the foreground and its owned material.</summary>
    internal static bool IsDirectlyBehind(nint backdrop, nint foreground) => GetWindow(foreground, 2) == backdrop;
    /// <summary>Clips the native lens to the same rounded outline as the player panel.</summary>
    internal static void SetShape(nint handle, int width, int height, float scale)
    {
        var region = CreateRoundRectRgn(0, 0, width + 1, height + 1, (int)(2 * GlassOptics.Radius * scale), (int)(2 * GlassOptics.Radius * scale));
        if (region == 0) throw new System.ComponentModel.Win32Exception();
        if (SetWindowRgn(handle, region, true) == 0)
        {
            DeleteObject(region);
            throw new System.ComponentModel.Win32Exception();
        }
    }
    /// <summary>Rejects mouse activation and lets input continue to the foreground application.</summary>
    private static nint HandleMessage(nint handle, uint message, nint first, nint second) => message switch
    {
        0x0084 => -1,
        0x0021 => 3,
        _ => DefWindowProc(handle, message, first, second),
    };

    /// <summary>Native physical rectangle.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rectangle { public int Left, Top, Right, Bottom; }
    /// <summary>Native window class registration data.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WindowClass
    {
        public uint Size, Style;
        public nint Procedure;
        public int ClassExtra, WindowExtra;
        public nint Instance, Icon, Cursor, Background;
        public string? MenuName, ClassName;
        public nint SmallIcon;
    }
    /// <summary>Win32 procedure signature.</summary>
    private delegate nint WindowProcedure(nint handle, uint message, nint first, nint second);
    /// <summary>Registers the owned window class.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "RegisterClassExW", SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WindowClass definition);
    /// <summary>Creates the owned background window.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW", SetLastError = true)]
    private static extern nint CreateWindowEx(uint extended, string name, string title, uint style,
        int x, int y, int width, int height, nint parent, nint menu, nint instance, nint parameter);
    /// <summary>Calls default processing for unrelated native messages.</summary>
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "DefWindowProcW")]
    private static extern nint DefWindowProc(nint handle, uint message, nint first, nint second);
    /// <summary>Finds this application's module.</summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetModuleHandleW")]
    private static extern nint GetModuleHandle(string? name);
    /// <summary>Moves the owned background without activating it.</summary>
    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(nint handle, nint after, int x, int y, int width, int height, uint flags);
    /// <summary>Hides the owned background window.</summary>
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(nint handle, int command);
    /// <summary>Destroys the owned background window.</summary>
    [DllImport("user32.dll")]
    private static extern bool DestroyWindow(nint handle);
    /// <summary>Reads foreground geometry.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetWindowRect(nint handle, out Rectangle bounds);
    /// <summary>Reads foreground scaling.</summary>
    [DllImport("user32.dll")]
    private static extern uint GetDpiForWindow(nint handle);
    /// <summary>Reads the adjacent window in desktop stacking order.</summary>
    [DllImport("user32.dll")]
    private static extern nint GetWindow(nint handle, uint command);
    /// <summary>Creates a rounded region owned by the application until transferred to the HWND.</summary>
    [DllImport("gdi32.dll")]
    private static extern nint CreateRoundRectRgn(int left, int top, int right, int bottom, int ellipseWidth, int ellipseHeight);
    /// <summary>Transfers the successful clipping region to the owned native window.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    private static extern int SetWindowRgn(nint handle, nint region, bool redraw);
    /// <summary>Releases a clipping region when transfer fails.</summary>
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint value);
}
