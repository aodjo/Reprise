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

    /// <summary>Reuses a frame only while a shell surface has focus and the native handle, bounds, monitor, and DPI remain unchanged.</summary>
    internal TaskbarFrame? DuringShell(nint taskbar, TaskbarRect bar, TaskbarRect monitor, uint dpi,
        TaskbarRect? currentTaskList, bool shellHasFocus)
    {
        if (_taskbar != taskbar || _frame is not { } frame || frame.Bar != bar || frame.Monitor != monitor || frame.Dpi != dpi)
        {
            Clear();
            return null;
        }
        return shellHasFocus ? frame with { TaskList = currentTaskList ?? frame.TaskList } : null;
    }

    /// <summary>Discards geometry when Explorer, monitor layout, or taskbar visibility changes.</summary>
    internal void Clear() { _taskbar = 0; _frame = null; }
}
