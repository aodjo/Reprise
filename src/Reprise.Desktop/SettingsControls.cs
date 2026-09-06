using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;

namespace Reprise.Desktop;

/// <summary>
/// Builders for the grouped-form look of the macOS settings window.
/// </summary>
/// <remarks>
/// SwiftUI's grouped form gives every section a caption, a rounded card
/// of rows, and a footnote. These helpers produce the same shapes from
/// Fluent theme resources, so the settings window reads like the Mac one
/// while still following the desktop's light or dark variant.
/// </remarks>
public static class SettingsForm
{
    /// <summary>
    /// Stacks sections with the spacing of a grouped form.
    /// </summary>
    /// <param name="sections">Sections in order.</param>
    /// <returns>A scrollable page.</returns>
    public static Control Page(params Control[] sections)
    {
        var stack = new StackPanel
        {
            Spacing = 22,
            Margin = new Thickness(20, 16, 20, 20),
        };
        foreach (var section in sections)
        {
            stack.Children.Add(section);
        }

        return new ScrollViewer
        {
            Content = stack,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
    }

    /// <summary>
    /// One captioned card of rows with an optional footnote.
    /// </summary>
    /// <param name="header">Caption above the card, or null.</param>
    /// <param name="footer">Note below the card, or null.</param>
    /// <param name="rows">Rows inside the card, separated by hairlines.</param>
    /// <returns>The section.</returns>
    public static Control Section(string? header, string? footer, params Control[] rows)
    {
        var stack = new StackPanel { Spacing = 6 };
        if (header is not null)
        {
            stack.Children.Add(Caption(header));
        }

        var card = new StackPanel();
        for (var index = 0; index < rows.Length; index++)
        {
            if (index > 0)
            {
                card.Children.Add(Hairline());
            }

            card.Children.Add(rows[index]);
        }

        var border = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Child = card,
        };
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable("SystemControlBackgroundAltHighBrush"));
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("SystemControlBackgroundBaseLowBrush"));
        stack.Children.Add(border);

        if (footer is not null)
        {
            stack.Children.Add(Footnote(footer));
        }

        return stack;
    }

    /// <summary>
    /// A row with a label on the left and a control on the right.
    /// </summary>
    /// <param name="label">Row title.</param>
    /// <param name="control">Control aligned to the trailing edge.</param>
    /// <returns>The row.</returns>
    public static Control Row(string label, Control control)
    {
        control.HorizontalAlignment = HorizontalAlignment.Right;
        control.VerticalAlignment = VerticalAlignment.Center;
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(14, 8),
            MinHeight = 24,
            Children =
            {
                new TextBlock
                {
                    Text = label,
                    FontSize = 13,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                },
                control,
            },
        };
        Grid.SetColumn(control, 1);
        return grid;
    }

    /// <summary>
    /// A row holding one control across its full width.
    /// </summary>
    /// <param name="control">Control to place.</param>
    /// <returns>The row.</returns>
    public static Control Fill(Control control)
    {
        control.Margin = new Thickness(14, 10);
        return control;
    }

    /// <summary>
    /// A row with a switch.
    /// </summary>
    /// <param name="label">Row title.</param>
    /// <param name="isOn">Initial state.</param>
    /// <param name="changed">Runs when the user flips the switch.</param>
    /// <param name="isEnabled">Whether the switch can be used.</param>
    /// <returns>The row and its switch, so callers can enable or disable it later.</returns>
    public static (Control Row, ToggleSwitch Switch) Toggle(string label, bool isOn, Action<bool> changed, bool isEnabled = true)
    {
        var toggle = new ToggleSwitch
        {
            IsChecked = isOn,
            OnContent = null,
            OffContent = null,
            IsEnabled = isEnabled,
            MinWidth = 0,
        };
        toggle.IsCheckedChanged += (_, _) => changed(toggle.IsChecked == true);
        return (Row(label, toggle), toggle);
    }

    /// <summary>
    /// A row showing a label and a read-only value.
    /// </summary>
    /// <param name="label">Row title.</param>
    /// <param name="value">Value text.</param>
    /// <returns>The row.</returns>
    public static Control Value(string label, string value)
    {
        var text = new TextBlock { Text = value, FontSize = 13 };
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("SystemControlForegroundBaseMediumBrush"));
        return Row(label, text);
    }

    /// <summary>
    /// A row whose whole width is a segmented picker under a label.
    /// </summary>
    /// <param name="label">Row title, or null for the picker alone.</param>
    /// <param name="picker">Picker to place.</param>
    /// <returns>The row.</returns>
    public static Control Picker(string? label, SegmentedPicker picker)
    {
        if (label is null)
        {
            return Fill(picker);
        }

        var stack = new StackPanel
        {
            Spacing = 8,
            Margin = new Thickness(14, 10),
            Children =
            {
                new TextBlock { Text = label, FontSize = 13 },
                picker,
            },
        };
        return stack;
    }

    /// <summary>
    /// The small secondary caption above a card.
    /// </summary>
    /// <param name="text">Caption text.</param>
    /// <returns>The caption.</returns>
    public static TextBlock Caption(string text)
    {
        var caption = new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(12, 0, 0, 0),
        };
        caption.Bind(TextBlock.ForegroundProperty, caption.GetResourceObservable("SystemControlForegroundBaseMediumBrush"));
        return caption;
    }

    /// <summary>
    /// The small secondary note below a card.
    /// </summary>
    /// <param name="text">Note text.</param>
    /// <returns>The note.</returns>
    public static TextBlock Footnote(string text)
    {
        var note = new TextBlock
        {
            Text = text,
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12, 0, 12, 0),
        };
        note.Bind(TextBlock.ForegroundProperty, note.GetResourceObservable("SystemControlForegroundBaseMediumBrush"));
        return note;
    }

    /// <summary>
    /// The one-pixel line between rows.
    /// </summary>
    /// <returns>The separator.</returns>
    public static Control Hairline()
    {
        var line = new Border { Height = 1, Margin = new Thickness(14, 0, 0, 0) };
        line.Bind(Border.BackgroundProperty, line.GetResourceObservable("SystemControlBackgroundBaseLowBrush"));
        return line;
    }

    /// <summary>
    /// A link-styled button that opens an address in the browser.
    /// </summary>
    /// <param name="text">Link text.</param>
    /// <param name="uri">Address to open.</param>
    /// <returns>The link.</returns>
    public static Control Link(string text, Uri uri)
    {
        var link = new TextBlock
        {
            Text = text,
            FontSize = 13,
            Cursor = new Cursor(StandardCursorType.Hand),
            TextDecorations = TextDecorations.Underline,
        };
        link.Bind(TextBlock.ForegroundProperty, link.GetResourceObservable("SystemAccentColorBrush"));
        link.PointerPressed += async (_, e) =>
        {
            e.Handled = true;
            if (TopLevel.GetTopLevel(link) is { } top)
            {
                await top.Launcher.LaunchUriAsync(uri);
            }
        };
        return link;
    }
}

/// <summary>
/// A horizontal group of mutually exclusive segments, like a macOS
/// segmented picker.
/// </summary>
/// <remarks>
/// Avalonia has no segmented control; this one is a rounded well holding
/// equal-width cells, with the chosen cell raised on a card. Values are
/// opaque objects so enums of any type can be picked.
/// </remarks>
public sealed class SegmentedPicker : Border
{
    private readonly List<(object Value, Border Cell)> _cells = [];
    private object? _selected;

    /// <summary>
    /// Creates the picker from labelled values.
    /// </summary>
    /// <param name="options">Segments in order.</param>
    /// <param name="selected">Initially chosen value.</param>
    public SegmentedPicker(IEnumerable<(string Label, object Value)> options, object? selected)
    {
        CornerRadius = new CornerRadius(7);
        Padding = new Thickness(2);
        this.Bind(BackgroundProperty, this.GetResourceObservable("SystemControlBackgroundBaseLowBrush"));

        var grid = new UniformGrid { Rows = 1 };
        foreach (var (label, value) in options)
        {
            var text = new TextBlock
            {
                Text = label,
                FontSize = 12,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var cell = new Border
            {
                CornerRadius = new CornerRadius(5),
                Padding = new Thickness(6, 4),
                Margin = new Thickness(1, 0),
                Child = text,
                Cursor = new Cursor(StandardCursorType.Arrow),
            };
            var captured = value;
            cell.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                Select(captured, raise: true);
            };
            _cells.Add((value, cell));
            grid.Children.Add(cell);
        }

        Child = grid;
        Select(selected, raise: false);
    }

    /// <summary>
    /// Raised when the user picks a different segment.
    /// </summary>
    public event EventHandler<object>? SelectionChanged;

    /// <summary>
    /// The chosen value.
    /// </summary>
    public object? SelectedValue
    {
        get => _selected;
        set => Select(value, raise: false);
    }

    /// <summary>
    /// Applies a choice and restyles the cells.
    /// </summary>
    /// <param name="value">Value to choose.</param>
    /// <param name="raise">Whether to notify listeners.</param>
    private void Select(object? value, bool raise)
    {
        var changed = !Equals(_selected, value);
        _selected = value;
        foreach (var (cellValue, cell) in _cells)
        {
            var chosen = Equals(cellValue, value);
            if (chosen)
            {
                cell.Bind(BackgroundProperty, cell.GetResourceObservable("SystemControlBackgroundAltHighBrush"));
                cell.BoxShadow = new BoxShadows(new BoxShadow
                {
                    Blur = 3,
                    OffsetY = 1,
                    Color = Color.FromArgb(0x30, 0, 0, 0),
                });
            }
            else
            {
                cell.Background = Brushes.Transparent;
                cell.BoxShadow = default;
            }
        }

        if (changed && raise && value is not null)
        {
            SelectionChanged?.Invoke(this, value);
        }
    }
}
