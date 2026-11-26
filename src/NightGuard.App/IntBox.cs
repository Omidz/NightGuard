using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace NightGuard.App;

/// <summary>A whole-number box with a range. Typing is limited to digits; the value is clamped when read.</summary>
public sealed class IntBox : TextBox
{
    public int Min { get; set; }
    public int Max { get; set; } = int.MaxValue;

    public event EventHandler? ValueChanged;

    public IntBox()
    {
        Width = 90;
        HorizontalAlignment = HorizontalAlignment.Left;
        TextAlignment = TextAlignment.Right;
        PreviewTextInput += (_, e) => e.Handled = !e.Text.All(char.IsAsciiDigit);
        DataObject.AddPastingHandler(this, (_, e) =>
        {
            if (e.DataObject.GetData(typeof(string)) is not string t || !t.All(char.IsAsciiDigit)) e.CancelCommand();
        });
        TextChanged += (_, _) => ValueChanged?.Invoke(this, EventArgs.Empty);
        LostFocus += (_, _) => Text = Value.ToString(CultureInfo.InvariantCulture);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Up) { Value++; e.Handled = true; }
            else if (e.Key == Key.Down) { Value--; e.Handled = true; }
        };
    }

    public int Value
    {
        get => int.TryParse(Text, NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? Math.Clamp(v, Min, Max) : Min;
        set => Text = Math.Clamp(value, Min, Max).ToString(CultureInfo.InvariantCulture);
    }
}
