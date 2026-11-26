using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NightGuard.App;

/// <summary>
/// A whole number with a range, shown as [ − ] 60 min [ + ]. Typing is limited to digits; the
/// value is clamped when read. (Kept its old name so the rest of the app didn't have to change.)
/// </summary>
public sealed class IntBox : Grid
{
    private readonly TextBox _box = new()
    {
        TextAlignment = TextAlignment.Center, MinWidth = 64, FontSize = 15, FontWeight = FontWeights.SemiBold,
        VerticalContentAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 6, 0),
    };
    private readonly TextBlock _suffix = new() { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };

    public int Min { get; set; }
    public int Max { get; set; } = int.MaxValue;
    public int Step { get; set; } = 1;
    public string Suffix { get => _suffix.Text; set => _suffix.Text = value; }

    public event EventHandler? ValueChanged;

    public IntBox()
    {
        HorizontalAlignment = HorizontalAlignment.Left;
        _suffix.SetResourceReference(TextBlock.ForegroundProperty, "NgMuted");
        for (var i = 0; i < 4; i++) ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var minus = MakeButton("−", -1);
        var plus = MakeButton("+", 1);
        SetColumn(minus, 0); SetColumn(_box, 1); SetColumn(_suffix, 2); SetColumn(plus, 3);
        Children.Add(minus); Children.Add(_box); Children.Add(_suffix); Children.Add(plus);

        _box.PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsAsciiDigit);
        DataObject.AddPastingHandler(_box, (_, e) =>
        {
            if (e.DataObject.GetData(typeof(string)) is not string t || !t.All(char.IsAsciiDigit)) e.CancelCommand();
        });
        _box.TextChanged += (_, _) => ValueChanged?.Invoke(this, EventArgs.Empty);
        _box.LostFocus += (_, _) => _box.Text = Value.ToString(CultureInfo.InvariantCulture);
        _box.PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Up) { Value += Step; e.Handled = true; }
            else if (e.Key == Key.Down) { Value -= Step; e.Handled = true; }
        };
    }

    private Button MakeButton(string text, int direction)
    {
        var b = new Button { Content = text, Width = 38, Height = 38, Padding = new Thickness(0), FontSize = 17 };
        b.SetResourceReference(StyleProperty, "ChunkyButton");
        b.Click += (_, _) => Value += direction * Step;
        b.ToolTip = direction < 0 ? "Less" : "More";
        return b;
    }

    public int Value
    {
        get => int.TryParse(_box.Text, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, Min, Max) : Min;
        set => _box.Text = Math.Clamp(value, Min, Max).ToString(CultureInfo.InvariantCulture);
    }
}
