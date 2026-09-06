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
            Spacing = 18,
            Margin = new Thickness(20, 14, 18, 18),
        };
        foreach (var section in sections)
        {
            stack.Children.Add(section);
        }

        return new ScrollViewer
        {
            Content = stack,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Padding = new Thickness(0, 0, 2, 0),
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
        border.Bind(Border.BackgroundProperty, border.GetResourceObservable("SettingsCardBrush"));
        border.Bind(Border.BorderBrushProperty, border.GetResourceObservable("SettingsLineBrush"));
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
                Text(label, PanelTypography.Body),
                control,
            },
        };
        Grid.SetColumn(control, 1);
        return grid;
    }

    /// <summary>
    /// A label in the window's primary colour.
    /// </summary>
    /// <param name="text">Text to show.</param>
    /// <param name="size">Font size.</param>
    /// <param name="weight">Font weight.</param>
    /// <returns>The label.</returns>
    public static TextBlock Text(string text, double size, FontWeight weight = FontWeight.Normal)
    {
        var block = new TextBlock
        {
            Text = text,
            FontSize = size,
            FontWeight = weight,
            VerticalAlignment = VerticalAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
        };
        block.Bind(TextBlock.ForegroundProperty, block.GetResourceObservable("SettingsPrimaryBrush"));
        return block;
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
        var text = new TextBlock { Text = value, FontSize = PanelTypography.Body };
        text.Bind(TextBlock.ForegroundProperty, text.GetResourceObservable("SettingsSecondaryBrush"));
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
                new TextBlock { Text = label, FontSize = PanelTypography.Body },
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
            FontSize = PanelTypography.Small,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(12, 0, 0, 0),
        };
        caption.Bind(TextBlock.ForegroundProperty, caption.GetResourceObservable("SettingsSecondaryBrush"));
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
            FontSize = PanelTypography.Subtitle,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12, 0, 12, 0),
        };
        note.Bind(TextBlock.ForegroundProperty, note.GetResourceObservable("SettingsSecondaryBrush"));
        return note;
    }

    /// <summary>
    /// The one-pixel line between rows.
    /// </summary>
    /// <returns>The separator.</returns>
    public static Control Hairline()
    {
        var line = new Border { Height = 1, Margin = new Thickness(14, 0, 0, 0) };
        line.Bind(Border.BackgroundProperty, line.GetResourceObservable("SettingsLineBrush"));
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
            FontSize = PanelTypography.Body,
            Cursor = new Cursor(StandardCursorType.Hand),
            TextDecorations = TextDecorations.Underline,
        };
        link.Bind(TextBlock.ForegroundProperty, link.GetResourceObservable("SettingsAccentBrush"));
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
        this.Bind(BackgroundProperty, this.GetResourceObservable("SettingsWellBrush"));

        var grid = new UniformGrid { Rows = 1 };
        foreach (var (label, value) in options)
        {
            var text = SettingsForm.Text(label, PanelTypography.Small);
            text.HorizontalAlignment = HorizontalAlignment.Center;
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
                cell.Bind(BackgroundProperty, cell.GetResourceObservable("SettingsCardBrush"));
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

/// <summary>
/// The row of tabs across the top of the settings window.
/// </summary>
/// <remarks>
/// Built by hand rather than from <c>TabControl</c>: the Fluent tab strip
/// left-aligns its items, tints their labels with the accent colour, and
/// underlines only the selected label, none of which resembles the macOS
/// settings toolbar. This draws evenly sized cells, centred, with the glyph
/// over its label and the selected cell on a rounded card.
/// </remarks>
public sealed class SettingsTabBar : Border
{
    private readonly List<(Border Cell, PanelGlyph Glyph, TextBlock Label)> _tabs = [];
    private int _selected = -1;

    /// <summary>
    /// Builds the bar from a tab per entry.
    /// </summary>
    /// <param name="tabs">Label and glyph for each tab, in order.</param>
    public SettingsTabBar(IReadOnlyList<(string Title, Geometry Icon)> tabs)
    {
        ArgumentNullException.ThrowIfNull(tabs);
        BorderThickness = new Thickness(0, 0, 0, 1);
        this.Bind(BorderBrushProperty, this.GetResourceObservable("SettingsLineBrush"));
        Padding = new Thickness(8, 8, 8, 6);

        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        for (var index = 0; index < tabs.Count; index++)
        {
            var (title, icon) = tabs[index];
            var glyph = new PanelGlyph
            {
                Icon = icon,
                IconSize = 17,
                Width = 20,
                Height = 20,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            var label = new TextBlock
            {
                Text = title,
                FontSize = PanelTypography.Small,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center,
            };
            var cell = new Border
            {
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(10, 5, 10, 4),
                Cursor = new Cursor(StandardCursorType.Arrow),
                Child = new StackPanel { Spacing = 2, Children = { glyph, label } },
            };
            var chosen = index;
            cell.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                Select(chosen);
            };
            _tabs.Add((cell, glyph, label));
            row.Children.Add(cell);
        }

        Child = row;
        Select(0);
    }

    /// <summary>
    /// Raised when the user picks a different tab.
    /// </summary>
    public event EventHandler<int>? SelectionChanged;

    /// <summary>
    /// Index of the tab on show.
    /// </summary>
    public int SelectedIndex => _selected;

    /// <summary>
    /// Shows one tab and restyles the cells.
    /// </summary>
    /// <param name="index">Tab to select.</param>
    public void Select(int index)
    {
        if (index < 0 || index >= _tabs.Count || index == _selected)
        {
            return;
        }

        _selected = index;
        for (var position = 0; position < _tabs.Count; position++)
        {
            var (cell, glyph, label) = _tabs[position];
            var chosen = position == index;
            if (chosen)
            {
                cell.Bind(BackgroundProperty, cell.GetResourceObservable("SettingsWellBrush"));
                glyph.Bind(PanelGlyph.ForegroundProperty, glyph.GetResourceObservable("SettingsAccentBrush"));
                label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("SettingsAccentBrush"));
                label.FontWeight = FontWeight.SemiBold;
            }
            else
            {
                cell.Background = Brushes.Transparent;
                glyph.Bind(PanelGlyph.ForegroundProperty, glyph.GetResourceObservable("SettingsSecondaryBrush"));
                label.Bind(TextBlock.ForegroundProperty, label.GetResourceObservable("SettingsSecondaryBrush"));
                label.FontWeight = FontWeight.Normal;
            }
        }

        SelectionChanged?.Invoke(this, index);
    }
}
