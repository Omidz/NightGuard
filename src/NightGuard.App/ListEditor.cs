using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace NightGuard.App;

/// <summary>
/// A friendly list: each entry is a little card with an icon and a ✕, and new ones are added by
/// typing (Enter or "Add") or with Browse buttons. Used for sites, apps, game folders and program names.
/// </summary>
public sealed class ListEditor : StackPanel
{
    private readonly List<string> _items = [];
    private readonly StackPanel _list = new();
    private readonly ScrollViewer _scroll;
    private readonly TextBox _input = new() { VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(10, 7, 10, 7), FontSize = 14 };
    private readonly TextBlock _placeholder = new() { IsHitTestVisible = false, Margin = new Thickness(13, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.55, FontSize = 14 };
    private readonly TextBlock _error = new() { Margin = new Thickness(2, 6, 0, 0), TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Visibility = Visibility.Collapsed };
    private readonly TextBlock _empty = new() { Margin = new Thickness(4, 6, 0, 8), Opacity = 0.6, FontStyle = FontStyles.Italic };
    private readonly WrapPanel _buttons = new() { Margin = new Thickness(0, 8, 0, 0) };
    private readonly Grid _inputRow = new() { Margin = new Thickness(0, 6, 0, 0) };

    /// <summary>The icon shown on each entry.</summary>
    public string Emoji { get; set; } = "\U0001F4C4";
    /// <summary>Paths: show the name big and the folder small.</summary>
    public bool IsPath { get; set; }
    public string Placeholder { get => _placeholder.Text; set => _placeholder.Text = value; }
    public string EmptyText { get => _empty.Text; set => _empty.Text = value; }
    /// <summary>False hides the text box (entries come only from the Browse buttons).</summary>
    public bool AllowTyping { get => _inputRow.Visibility == Visibility.Visible; set => _inputRow.Visibility = value ? Visibility.Visible : Visibility.Collapsed; }
    public double MaxListHeight { get => _scroll.MaxHeight; set => _scroll.MaxHeight = value; }

    /// <summary>Cleans up what was typed (trim, lower-case...). Return null to ignore it.</summary>
    public Func<string, string?>? Normalize { get; set; }
    /// <summary>Why an entry can't be added, or null if it's fine.</summary>
    public Func<string, string?>? Validate { get; set; }

    public event EventHandler? Changed;

    public IReadOnlyList<string> Items => _items;

    public ListEditor()
    {
        _scroll = new ScrollViewer { Content = _list, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 240 };
        Children.Add(_scroll);
        Children.Add(_empty);

        var add = new Button { Content = "+ Add", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(14, 6, 14, 6) };
        add.SetResourceReference(StyleProperty, "ChunkyButton");
        add.Click += (_, _) => AddTyped();
        _inputRow.ColumnDefinitions.Add(new ColumnDefinition());
        _inputRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _inputRow.Children.Add(_input);
        _inputRow.Children.Add(_placeholder);
        Grid.SetColumn(add, 1);
        _inputRow.Children.Add(add);
        Children.Add(_inputRow);
        Children.Add(_error);
        Children.Add(_buttons);

        _input.TextChanged += (_, _) =>
        {
            _placeholder.Visibility = _input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            _error.Visibility = Visibility.Collapsed;
        };
        _input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { AddTyped(); e.Handled = true; } };
        Loaded += (_, _) => _error.Foreground = (Brush?)TryFindResource("NgRed") ?? Brushes.IndianRed;
        Render();
    }

    /// <summary>Adds a button under the list, for example "📁 Browse for a folder...". The picker returns the chosen entry, or null.</summary>
    public void AddBrowseButton(string text, Func<string?> picker)
    {
        var b = new Button { Content = text, Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(14, 6, 14, 6) };
        b.SetResourceReference(StyleProperty, "ChunkyButton");
        b.Click += (_, _) => { if (picker() is { } picked) TryAdd(picked); };
        _buttons.Children.Add(b);
    }

    public void SetItems(IEnumerable<string> items)
    {
        _items.Clear();
        foreach (var i in items) if (!_items.Contains(i, StringComparer.OrdinalIgnoreCase)) _items.Add(i);
        Render();
    }

    /// <summary>Adds several entries at once (for example a preset list), skipping ones already there.</summary>
    public void AddRange(IEnumerable<string> items)
    {
        foreach (var i in items) if (!_items.Contains(i, StringComparer.OrdinalIgnoreCase)) _items.Add(i);
        Render();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void AddTyped()
    {
        if (TryAdd(_input.Text)) _input.Clear();
        _input.Focus();
    }

    /// <summary>Adds one entry if it's valid. Returns false (and shows why) if not.</summary>
    public bool TryAdd(string raw)
    {
        var value = raw.Trim();
        if (value.Length == 0) return false;
        if (Normalize is not null) value = Normalize(value) ?? "";
        if (value.Length == 0) return false;
        if (_items.Contains(value, StringComparer.OrdinalIgnoreCase)) { ShowError($"\"{value}\" is already in the list."); return false; }
        if (Validate?.Invoke(value) is { } problem) { ShowError(problem); return false; }
        _items.Add(value);
        Render();
        Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private void ShowError(string text)
    {
        _error.Text = "⚠️ " + text;
        _error.Visibility = Visibility.Visible;
    }

    private void Remove(string item)
    {
        _items.Remove(item);
        Render();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Render()
    {
        _list.Children.Clear();
        foreach (var item in _items) _list.Children.Add(Row(item));
        _empty.Visibility = _items.Count == 0 && _empty.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private Border Row(string item)
    {
        var icon = new TextBlock { Text = IconFor(item), FontFamily = Art.EmojiFont, FontSize = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        var remove = new Button { Content = "✕", ToolTip = "Remove", Padding = new Thickness(9, 3, 9, 3), Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        remove.SetResourceReference(StyleProperty, "Chip");
        remove.Margin = new Thickness(8, 0, 0, 0);
        remove.Click += (_, _) => Remove(item);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        if (IsPath)
        {
            var name = Path.GetFileName(item.TrimEnd('\\'));
            text.Children.Add(new TextBlock { Text = string.IsNullOrEmpty(name) ? item : name, FontWeight = FontWeights.SemiBold, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = Path.GetDirectoryName(item.TrimEnd('\\')) ?? "", FontSize = 12, Opacity = 0.65, TextTrimming = TextTrimming.CharacterEllipsis });
        }
        else
        {
            text.Children.Add(new TextBlock { Text = item, FontSize = 14, TextTrimming = TextTrimming.CharacterEllipsis });
        }

        var dock = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(remove, Dock.Right);
        dock.Children.Add(icon);
        dock.Children.Add(remove);
        dock.Children.Add(text);
        var row = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 7, 8, 7), Margin = new Thickness(0, 0, 0, 6), Child = dock, ToolTip = item };
        row.SetResourceReference(Border.BackgroundProperty, "NgSurface2");
        return row;
    }

    private string IconFor(string item)
    {
        if (!IsPath) return Emoji;
        return item.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? "\U0001F4E6" : "\U0001F4C1";
    }
}
