using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using NightGuard.Core.Schedule;

namespace NightGuard.App;

/// <summary>
/// Picks a time of day, shown the way the Windows clock shows it (12-hour with AM/PM, or 24-hour).
/// The value is always stored as "HH:mm".
/// </summary>
public sealed class TimePicker : StackPanel
{
    private readonly ComboBox _hour = new() { Width = 64 };
    private readonly ComboBox _minute = new() { Width = 64 };
    private readonly ComboBox _ampm = new() { Width = 70, Margin = new Thickness(6, 0, 0, 0) };
    private readonly bool _is12 = TimeText.Is12Hour;
    private bool _setting;

    public event EventHandler? ValueChanged;

    public TimePicker()
    {
        Orientation = Orientation.Horizontal;
        if (_is12)
        {
            foreach (var h in new[] { 12, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 }) _hour.Items.Add(h.ToString(CultureInfo.CurrentCulture));
            var f = CultureInfo.CurrentCulture.DateTimeFormat;
            _ampm.Items.Add(string.IsNullOrEmpty(f.AMDesignator) ? "AM" : f.AMDesignator);
            _ampm.Items.Add(string.IsNullOrEmpty(f.PMDesignator) ? "PM" : f.PMDesignator);
        }
        else
        {
            for (var h = 0; h < 24; h++) _hour.Items.Add(h.ToString("00", CultureInfo.InvariantCulture));
        }
        for (var m = 0; m < 60; m += 5) _minute.Items.Add(m.ToString("00", CultureInfo.InvariantCulture));
        Children.Add(_hour);
        Children.Add(new TextBlock { Text = ":", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 4, 0), FontWeight = FontWeights.SemiBold });
        Children.Add(_minute);
        if (_is12) Children.Add(_ampm);
        _hour.SelectionChanged += Changed;
        _minute.SelectionChanged += Changed;
        _ampm.SelectionChanged += Changed;
        _hour.ToolTip = _minute.ToolTip = TimeText.Hint;
        Value = "00:00";
    }

    private void Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_setting) ValueChanged?.Invoke(this, EventArgs.Empty);
    }

    public string Value
    {
        get
        {
            var h = _hour.SelectedIndex < 0 ? 0 : _hour.SelectedIndex;   // 12h list starts at 12, so index = hour % 12
            if (_is12 && _ampm.SelectedIndex == 1) h += 12;
            var m = int.Parse((string)(_minute.SelectedItem ?? "00"), CultureInfo.InvariantCulture);
            return $"{h:00}:{m:00}";
        }
        set
        {
            if (!ScheduleMath.IsTime(value)) value = "00:00";
            var t = ScheduleMath.ParseTime(value);
            _setting = true;
            try
            {
                _hour.SelectedIndex = _is12 ? t.Hours % 12 : t.Hours;
                if (_is12) _ampm.SelectedIndex = t.Hours >= 12 ? 1 : 0;
                var mm = t.Minutes.ToString("00", CultureInfo.InvariantCulture);
                if (!_minute.Items.Contains(mm))
                {
                    var i = 0;
                    while (i < _minute.Items.Count && string.CompareOrdinal((string)_minute.Items[i]!, mm) < 0) i++;
                    _minute.Items.Insert(i, mm);
                }
                _minute.SelectedItem = mm;
            }
            finally { _setting = false; }
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}
