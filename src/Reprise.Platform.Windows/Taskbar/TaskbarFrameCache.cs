namespace Reprise.Platform.Windows.Taskbar;

/// <summary>Retains measured taskbar geometry during transient shell accessibility outages, without guessing tray positions.</summary>
internal sealed class TaskbarFrameCache
{
    private nint _taskbar;
    private TaskbarFrame? _frame;

    /// <summary>Stores a successfully measured frame for the current shell HWND.</summary>
    internal TaskbarFrame Remember(nint taskbar, TaskbarFrame frame)
    {
        _taskbar = taskbar;
        _frame = frame;
        return frame;
    }

    /// <summary>Accepts complete desktop measurements while conserving all known tray and button space during partial shell updates.</summary>
    internal TaskbarFrame? Resolve(nint taskbar, TaskbarRect bar, TaskbarRect monitor, uint dpi,
        TaskbarRect? tray, TaskbarRect? occupied, bool shellHasFocus)
    {
        if (shellHasFocus)
        {
            var retained = DuringShell(taskbar, bar, monitor, dpi, occupied, true);
            if (retained is null) return null;
            if (tray is { } current && current.Width > 0 && current.Left > bar.Left && current.Right <= bar.Right + 2)
                retained = retained with { Tray = retained.Tray with { Left = Math.Min(retained.Tray.Left, current.Left) } };
            return Remember(taskbar, retained);
        }
        if (tray is { } measuredTray && occupied is { } measuredButtons)
            return Remember(taskbar, new(bar, measuredTray, measuredButtons, monitor, dpi));
        return DuringShell(taskbar, bar, monitor, dpi, occupied, false);
    }

    /// <summary>Reuses a frame only while a shell surface has focus and the native handle, bounds, monitor, and DPI remain unchanged.</summary>
    internal TaskbarFrame? DuringShell(nint taskbar, TaskbarRect bar, TaskbarRect monitor, uint dpi,
        TaskbarRect? currentTaskList, bool shellHasFocus)
    {
        if (_taskbar != taskbar || _frame is not { } frame || frame.Bar != bar || frame.Monitor != monitor || frame.Dpi != dpi)
        {
            Clear();
            return null;
        }
        if (!shellHasFocus) return null;
        var occupied = currentTaskList ?? frame.TaskList;
        if (currentTaskList is { } current && frame.TaskList is { } measured)
        {
            // Legacy HWND bounds can omit XAML buttons; retain all known occupied space until accessibility recovers.
            occupied = current with { Left = Math.Min(current.Left, measured.Left), Right = Math.Max(current.Right, measured.Right) };
        }
        return frame with { TaskList = occupied };
    }

    /// <summary>Discards geometry when Explorer, monitor layout, or taskbar visibility changes.</summary>
    internal void Clear() { _taskbar = 0; _frame = null; }
}
