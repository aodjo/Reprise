using System.Windows.Automation;

namespace Reprise.Platform.Windows.Taskbar;

/// <summary>Immutable UI Automation geometry, read on a background MTA without moving shell controls.</summary>
/// <param name="Id">Nonlocalized automation identity.</param>
/// <param name="Class">Provider class.</param>
/// <param name="Button">Whether this element is an actionable taskbar button.</param>
/// <param name="Bounds">Element bounds in screen pixels.</param>
internal sealed record TaskbarElement(string Id, string Class, bool Button, TaskbarRect Bounds);

/// <summary>Reads Windows 11 XAML taskbar geometry, whose legacy tray HWND has zero width.</summary>
internal static class TaskbarAccessibility
{
    private static Task<TaskbarElement[]>? _pending;
    private static TaskbarElement[] _elements = [];
    private static DateTimeOffset _lastRequest;
    private static DateTimeOffset _lastResult;
    private static nint _root;
    private static nint _pendingRoot;

    /// <summary>Returns a cached immutable snapshot and schedules a bounded-frequency background refresh.</summary>
    /// <param name="taskbar">Current shell handle, reacquired after Explorer restarts.</param>
    internal static TaskbarElement[] Read(nint taskbar)
    {
        var now = DateTimeOffset.UtcNow;
        if (_root != taskbar)
        {
            _root = taskbar;
            _elements = [];
        }
        if (_pending is { IsCompleted: true })
        {
            if (_pending.IsCompletedSuccessfully && _pendingRoot == taskbar)
            {
                _elements = _pending.Result;
                _lastResult = now;
            }
            else
            {
                _ = _pending.Exception;
                _elements = [];
            }
            _pending = null;
        }
        if (_pending is null && now - _lastRequest >= TimeSpan.FromMilliseconds(500))
        {
            _lastRequest = now;
            _pendingRoot = taskbar;
            _pending = Task.Run(() => Query(taskbar));
        }
        return now - _lastResult <= TimeSpan.FromSeconds(2) ? _elements : [];
    }

    /// <summary>Collects only public taskbar element geometry; no names, messages, or user document data.</summary>
    private static TaskbarElement[] Query(nint taskbar)
    {
        try
        {
            var cache = new CacheRequest();
            cache.Add(AutomationElement.AutomationIdProperty);
            cache.Add(AutomationElement.ClassNameProperty);
            cache.Add(AutomationElement.BoundingRectangleProperty);
            cache.Add(AutomationElement.ControlTypeProperty);
            cache.Add(AutomationElement.IsOffscreenProperty);
            using var active = cache.Activate();
            var root = AutomationElement.FromHandle(taskbar);
            var descendants = root.FindAll(TreeScope.Descendants, Condition.TrueCondition);
            var elements = new List<TaskbarElement>();
            for (var index = 0; index < Math.Min(descendants.Count, 512); index++)
            {
                var element = descendants[index].Cached;
                var bounds = element.BoundingRectangle;
                if (element.IsOffscreen || bounds.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0) continue;
                elements.Add(new(element.AutomationId, element.ClassName, element.ControlType == ControlType.Button,
                    new((int)Math.Floor(bounds.Left), (int)Math.Floor(bounds.Top),
                        (int)Math.Ceiling(bounds.Right), (int)Math.Ceiling(bounds.Bottom))));
            }
            return elements.ToArray();
        }
        catch (Exception error) when (error is ElementNotAvailableException or System.Runtime.InteropServices.COMException
            or InvalidOperationException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>Finds the real XAML tray group and the rightmost visible taskbar button.</summary>
    /// <param name="bar">Physical taskbar bounds.</param>
    /// <param name="elements">Cached public accessibility geometry.</param>
    internal static (TaskbarRect? Tray, TaskbarRect? TaskList) Resolve(TaskbarRect bar, TaskbarElement[] elements)
    {
        var trayItems = elements.Where(item =>
            (item.Id.Contains("SystemTray", StringComparison.OrdinalIgnoreCase) ||
             item.Class.Contains("SystemTray", StringComparison.OrdinalIgnoreCase)) &&
            item.Bounds.Width < bar.Width / 2 && item.Bounds.Top >= bar.Top && item.Bounds.Bottom <= bar.Bottom + 2).ToArray();
        if (trayItems.Length == 0) return (null, null);
        var tray = new TaskbarRect(trayItems.Min(item => item.Bounds.Left), bar.Top, bar.Right, bar.Bottom);
        var buttons = elements.Where(item => item.Button && item.Bounds.Right <= tray.Left &&
            item.Bounds.Top >= bar.Top && item.Bounds.Bottom <= bar.Bottom + 2).ToArray();
        TaskbarRect? list = buttons.Length == 0 ? null : new TaskbarRect(bar.Left, bar.Top,
            buttons.Max(item => item.Bounds.Right), bar.Bottom);
        return (tray, list);
    }
}
