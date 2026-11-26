using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace NightGuard.App;

/// <summary>A row of pill buttons; exactly one is picked. A value that isn't one of the choices gets its own pill.</summary>
public sealed class PillPicker : WrapPanel
{
    private readonly List<(int Value, ToggleButton Pill)> _pills = [];
    private Func<int, string> _label = v => v.ToString(System.Globalization.CultureInfo.CurrentCulture);
    private int _value;

    public event EventHandler? ValueChanged;

    public void SetChoices(IEnumerable<(int Value, string Label)> choices, Func<int, string> labelFor)
    {
        _label = labelFor;
        Children.Clear();
        _pills.Clear();
        foreach (var (v, l) in choices) Add(v, l);
        Select(_value);
    }

    private ToggleButton Add(int value, string label)
    {
        var pill = new ToggleButton { Content = label, Tag = value };
        pill.SetResourceReference(StyleProperty, "Pill");
        pill.Click += (_, _) => { Value = value; ValueChanged?.Invoke(this, EventArgs.Empty); };
        var i = _pills.FindIndex(p => p.Value > value);
        if (i < 0) i = _pills.Count;
        _pills.Insert(i, (value, pill));
        Children.Insert(i, pill);
        return pill;
    }

    private void Select(int value)
    {
        foreach (var (v, p) in _pills) p.IsChecked = v == value;
    }

    public int Value
    {
        get => _value;
        set
        {
            _value = value;
            if (_pills.Count > 0 && _pills.All(p => p.Value != value)) Add(value, _label(value));
            Select(value);
        }
    }
}

/// <summary>Pills where any number can be picked (the screen time warnings).</summary>
public sealed class MultiPills : WrapPanel
{
    private readonly List<(int Value, ToggleButton Pill)> _pills = [];

    public void SetChoices(IEnumerable<(int Value, string Label)> choices)
    {
        Children.Clear();
        _pills.Clear();
        foreach (var (v, l) in choices) Add(v, l);
    }

    private void Add(int value, string label)
    {
        var pill = new ToggleButton { Content = label, Tag = value };
        pill.SetResourceReference(StyleProperty, "Pill");
        var i = _pills.FindIndex(p => p.Value < value);
        if (i < 0) i = _pills.Count;
        _pills.Insert(i, (value, pill));
        Children.Insert(i, pill);
    }

    public List<int> Values
    {
        get => _pills.Where(p => p.Pill.IsChecked == true).Select(p => p.Value).ToList();
        set
        {
            foreach (var v in value.Where(v => _pills.All(p => p.Value != v))) Add(v, $"{v} min");
            foreach (var (v, p) in _pills) p.IsChecked = value.Contains(v);
        }
    }
}

/// <summary>A progress ring (0 to 1), drawn as a vector arc.</summary>
public sealed class RingArc : FrameworkElement
{
    public static readonly DependencyProperty FractionProperty = DependencyProperty.Register(
        nameof(Fraction), typeof(double), typeof(RingArc), new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Fraction { get => (double)GetValue(FractionProperty); set => SetValue(FractionProperty, value); }
    public double Thickness { get; set; } = 10;
    public Brush Track { get; set; } = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
    public Brush Fill { get; set; } = Application.Current?.TryFindResource("NgMoon") as Brush ?? new SolidColorBrush(Color.FromRgb(0xFF, 0xC9, 0x4D));

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= Thickness) return;
        var r = (size - Thickness) / 2;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        dc.DrawEllipse(null, new Pen(Track, Thickness), center, r, r);
        var f = Math.Clamp(Fraction, 0, 1);
        if (f <= 0) return;
        var pen = new Pen(Fill, Thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        if (f >= 0.999) { dc.DrawEllipse(null, pen, center, r, r); return; }
        var angle = f * 2 * Math.PI;
        var start = new Point(center.X, center.Y - r);
        var end = new Point(center.X + r * Math.Sin(angle), center.Y - r * Math.Cos(angle));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(start, false, false);
            ctx.ArcTo(end, new Size(r, r), 0, f > 0.5, SweepDirection.Clockwise, true, false);
        }
        g.Freeze();
        dc.DrawGeometry(null, pen, g);
    }
}

/// <summary>
/// Press and hold for three seconds to sign the commitment. A ring fills while you hold; let go
/// early and it drains again. Works with the mouse, touch, and Space or Enter.
/// </summary>
public sealed class HoldButton : Border
{
    private static readonly TimeSpan HoldTime = TimeSpan.FromSeconds(3);
    private readonly RingArc _ring = new() { Width = 54, Height = 54, Thickness = 7 };
    private readonly TextBlock _icon = new() { Text = "✍️", FontFamily = Art.EmojiFont, FontSize = 22, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _title = new() { FontSize = 17, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _sub = new() { FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private DateTime _started;
    private bool _holding;
    private bool _signed;

    public event EventHandler? Signed;

    public HoldButton()
    {
        SetResourceReference(StyleProperty, "Card");
        Cursor = Cursors.Hand;
        Focusable = true;
        FocusVisualStyle = null;
        _sub.SetResourceReference(TextBlock.ForegroundProperty, "NgMuted");
        var ringHolder = new Grid { Margin = new Thickness(0, 0, 16, 0) };
        ringHolder.Children.Add(_ring);
        ringHolder.Children.Add(_icon);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(_title);
        text.Children.Add(_sub);
        var row = new DockPanel();
        DockPanel.SetDock(ringHolder, Dock.Left);
        row.Children.Add(ringHolder);
        row.Children.Add(text);
        Child = row;
        AutomationProperties_SetName("Hold to sign your commitment");
        _timer.Tick += (_, _) => Tick();
        MouseLeftButtonDown += (_, e) => { CaptureMouse(); Begin(); e.Handled = true; };
        MouseLeftButtonUp += (_, _) => { ReleaseMouseCapture(); End(); };
        LostMouseCapture += (_, _) => End();
        TouchDown += (_, e) => { CaptureTouch(e.TouchDevice); Begin(); e.Handled = true; };
        TouchUp += (_, e) => { ReleaseTouchCapture(e.TouchDevice); End(); };
        KeyDown += (_, e) => { if (e.Key is Key.Space or Key.Enter && !e.IsRepeat) { Begin(); e.Handled = true; } };
        KeyUp += (_, e) => { if (e.Key is Key.Space or Key.Enter) { End(); e.Handled = true; } };
        GotKeyboardFocus += (_, _) => SetResourceReference(BorderBrushProperty, "NgMoonSoft");
        LostKeyboardFocus += (_, _) => SetResourceReference(BorderBrushProperty, _signed ? "NgMoon" : "NgStroke");
        Update();
    }

    private void AutomationProperties_SetName(string name) => System.Windows.Automation.AutomationProperties.SetName(this, name);

    public string Prompt { get; set; } = "Hold to sign your commitment";

    public bool IsSigned
    {
        get => _signed;
        set { _signed = value; Update(); }
    }

    public string SignedText { get; set; } = "Signed. Night 1 starts tonight.";

    private void Begin()
    {
        if (_signed || _holding || !IsEnabled) return;
        _holding = true;
        _started = DateTime.UtcNow;
        _ring.BeginAnimation(RingArc.FractionProperty, null);
        _timer.Start();
        _sub.Text = "Keep holding…";
    }

    private void End()
    {
        if (!_holding) return;
        _holding = false;
        _timer.Stop();
        if (_signed) return;
        _ring.BeginAnimation(RingArc.FractionProperty, new DoubleAnimation(_ring.Fraction, 0, TimeSpan.FromMilliseconds(350)));
        _sub.Text = "So close! Hold it for the full three seconds.";
    }

    private void Tick()
    {
        var f = (DateTime.UtcNow - _started).TotalSeconds / HoldTime.TotalSeconds;
        _ring.Fraction = f;
        if (f < 1) return;
        _timer.Stop();
        _holding = false;
        ReleaseMouseCapture();
        _signed = true;
        Update();
        Signed?.Invoke(this, EventArgs.Empty);
    }

    private void Update()
    {
        _ring.BeginAnimation(RingArc.FractionProperty, null);
        _ring.Fraction = _signed ? 1 : 0;
        _icon.Text = _signed ? "✅" : "✍️";
        _title.Text = _signed ? SignedText : Prompt;
        _sub.Text = _signed ? "Change your choice above to sign again." : "Press and hold for 3 seconds. Like a handshake with future you.";
        SetResourceReference(BorderBrushProperty, _signed ? "NgMoon" : "NgStroke");
    }
}

/// <summary>Celebration! Confetti that rains (or bursts) over a panel for a few seconds, then cleans itself up.</summary>
internal static class Confetti
{
    private static readonly Color[] Colors =
    [
        Art.C("#FFC94D"), Art.C("#FF8AB4"), Art.C("#5FD8CB"), Art.C("#B49CFF"), Art.C("#FFA25E"), Art.C("#62DB9F"), Art.C("#FFFFFF"),
    ];

    private sealed class Piece
    {
        public required Shape Shape;
        public required RotateTransform Spin;
        public double X, Y, Vx, Vy, Vr, Wobble;
    }

    /// <param name="origin">Burst from this point (in the host's coordinates), or rain from the top if null.</param>
    public static void Burst(Panel host, Point? origin = null, int count = 170)
    {
        if (host.ActualWidth < 10) return;
        var canvas = new Canvas { IsHitTestVisible = false, ClipToBounds = true };
        Panel.SetZIndex(canvas, 9999);
        if (host is Grid)
        {
            Grid.SetRowSpan(canvas, 99);
            Grid.SetColumnSpan(canvas, 99);
        }
        host.Children.Add(canvas);
        var rng = new Random();
        var w = host.ActualWidth;
        var h = host.ActualHeight;
        var pieces = new List<Piece>();
        for (var i = 0; i < count; i++)
        {
            var color = new SolidColorBrush(Colors[rng.Next(Colors.Length)]);
            Shape s = rng.Next(4) switch
            {
                0 => new Ellipse { Width = 8, Height = 8, Fill = color },
                1 => Art.Sparkle(12, color),
                _ => new Rectangle { Width = 7, Height = 13, Fill = color, RadiusX = 1.5, RadiusY = 1.5 },
            };
            var spin = new RotateTransform(rng.Next(360), 4, 6);
            s.RenderTransform = spin;
            var p = new Piece { Shape = s, Spin = spin, Vr = rng.NextDouble() * 540 - 270, Wobble = rng.NextDouble() * Math.PI * 2 };
            if (origin is { } o)
            {
                var angle = -Math.PI / 2 + (rng.NextDouble() - 0.5) * Math.PI * 1.1;
                var speed = 380 + rng.NextDouble() * 520;
                p.X = o.X; p.Y = o.Y;
                p.Vx = Math.Cos(angle) * speed;
                p.Vy = Math.Sin(angle) * speed;
            }
            else
            {
                p.X = rng.NextDouble() * w;
                p.Y = -20 - rng.NextDouble() * h * 0.6;
                p.Vx = rng.NextDouble() * 120 - 60;
                p.Vy = 120 + rng.NextDouble() * 160;
            }
            canvas.Children.Add(s);
            pieces.Add(p);
        }

        var started = DateTime.UtcNow;
        var last = started;
        const double life = 4.2;
        EventHandler? onFrame = null;
        onFrame = (_, _) =>
        {
            var now = DateTime.UtcNow;
            var dt = Math.Min(0.05, (now - last).TotalSeconds);
            last = now;
            var age = (now - started).TotalSeconds;
            foreach (var p in pieces)
            {
                p.Vy += 620 * dt;                  // gravity
                p.Vx *= 1 - 1.6 * dt;              // air
                p.Vy *= 1 - 0.9 * dt;
                p.Wobble += dt * 6;
                p.X += (p.Vx + Math.Sin(p.Wobble) * 30) * dt;
                p.Y += p.Vy * dt;
                p.Spin.Angle += p.Vr * dt;
                Canvas.SetLeft(p.Shape, p.X);
                Canvas.SetTop(p.Shape, p.Y);
            }
            if (age > life - 0.8) canvas.Opacity = Math.Max(0, (life - age) / 0.8);
            if (age >= life)
            {
                CompositionTarget.Rendering -= onFrame;
                host.Children.Remove(canvas);
            }
        };
        CompositionTarget.Rendering += onFrame;
    }
}
