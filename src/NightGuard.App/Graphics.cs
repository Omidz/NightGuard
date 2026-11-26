using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace NightGuard.App;

/// <summary>
/// NightGuard's little bits of artwork, drawn as vectors so they stay crisp at any size and cost
/// nothing when the window is closed. WPF draws emoji in one colour, so they're tinted to match.
/// </summary>
internal static class Art
{
    public static readonly FontFamily EmojiFont = new("Segoe UI Emoji");

    public static Color C(string hex) => (Color)ColorConverter.ConvertFromString(hex);

    public static Brush NightSky() => new LinearGradientBrush(
        [new GradientStop(C("#141836"), 0), new GradientStop(C("#2A2466"), 0.55), new GradientStop(C("#5A3E9E"), 1)],
        new Point(0, 0), new Point(1, 1));

    public static Brush Sunrise() => new LinearGradientBrush(
        [new GradientStop(C("#FF8A5B"), 0), new GradientStop(C("#FFB86B"), 0.5), new GradientStop(C("#FFD98A"), 1)],
        new Point(0, 0), new Point(1, 1));

    public static Brush Alarm() => new LinearGradientBrush(
        [new GradientStop(C("#B3122E"), 0), new GradientStop(C("#E0434A"), 1)], new Point(0, 0), new Point(1, 1));

    public static Brush Moon => new SolidColorBrush(C("#FFD666"));

    /// <summary>An emoji as a tinted glyph.</summary>
    public static TextBlock Emoji(string emoji, double size, Brush? color = null) => new()
    {
        Text = emoji, FontFamily = EmojiFont, FontSize = size, Foreground = color ?? Brushes.White,
        VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
    };

    /// <summary>A crescent moon.</summary>
    public static Shape Crescent(double size, Brush fill)
    {
        var full = new EllipseGeometry(new Point(size / 2, size / 2), size / 2, size / 2);
        var bite = new EllipseGeometry(new Point(size * 0.72, size * 0.36), size * 0.44, size * 0.44);
        return new Path { Data = new CombinedGeometry(GeometryCombineMode.Exclude, full, bite), Fill = fill, Width = size, Height = size };
    }

    /// <summary>Scatters twinkling stars over a canvas. Same layout every time (seeded), so it doesn't jump around.</summary>
    public static void Stars(Canvas canvas, double width, double height, int count, int seed, bool twinkle = true)
    {
        var rng = new Random(seed);
        for (var i = 0; i < count; i++)
        {
            var size = 1.5 + rng.NextDouble() * 2.5;
            var star = new Ellipse { Width = size, Height = size, Fill = Brushes.White, Opacity = 0.35 + rng.NextDouble() * 0.6 };
            Canvas.SetLeft(star, rng.NextDouble() * width);
            Canvas.SetTop(star, rng.NextDouble() * height);
            canvas.Children.Add(star);
            if (!twinkle || i % 3 != 0) continue;
            var anim = new DoubleAnimation(0.15, 1, TimeSpan.FromSeconds(1.2 + rng.NextDouble() * 2))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(rng.NextDouble() * 2),
            };
            star.BeginAnimation(UIElement.OpacityProperty, anim);
        }
    }

    /// <summary>A four-point sparkle.</summary>
    public static Shape Sparkle(double size, Brush fill)
    {
        var s = size / 2;
        var g = Geometry.Parse($"M {s},0 Q {s * 1.15},{s * 0.85} {size},{s} Q {s * 1.15},{s * 1.15} {s},{size} Q {s * 0.85},{s * 1.15} 0,{s} Q {s * 0.85},{s * 0.85} {s},0 Z");
        return new Path { Data = g, Fill = fill, Width = size, Height = size };
    }

    /// <summary>"Z z z" drifting up, for sleepy scenes.</summary>
    public static StackPanel Zzz(double size)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        for (var i = 0; i < 3; i++)
        {
            var t = new TextBlock
            {
                Text = "z", FontSize = size * (0.6 + i * 0.25), FontWeight = FontWeights.Bold, Foreground = Brushes.White,
                Margin = new Thickness(2, 0, 2, i * size * 0.35), VerticalAlignment = VerticalAlignment.Bottom, Opacity = 0,
            };
            var fade = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromSeconds(i * 0.6) };
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.9, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.8))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(2.4))));
            fade.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(3))));
            t.BeginAnimation(UIElement.OpacityProperty, fade);
            sp.Children.Add(t);
        }
        return sp;
    }
}

/// <summary>
/// The top of each page: a round badge with a tinted emoji, "STEP 3 OF 9", a big title and a
/// cheeky one-liner. Everything wraps, so it works at any window size and scaling.
/// </summary>
public sealed class PageHeader : DockPanel
{
    private readonly TextBlock _emoji = Art.Emoji("", 30, Art.Moon);
    private readonly TextBlock _step = new();
    private readonly TextBlock _title = new();
    private readonly TextBlock _sub = new() { Margin = new Thickness(0, 6, 0, 0) };

    public PageHeader()
    {
        Margin = new Thickness(0, 0, 0, 24);
        _step.SetResourceReference(StyleProperty, "Overline");
        _title.SetResourceReference(StyleProperty, "H1");
        _sub.SetResourceReference(StyleProperty, "Lead");
        _sub.Margin = new Thickness(0, 6, 0, 0);
        var badge = new Border
        {
            Width = 64, Height = 64, CornerRadius = new CornerRadius(32), Margin = new Thickness(0, 0, 18, 0),
            Background = Art.NightSky(), BorderThickness = new Thickness(2, 2, 2, 4), Child = _emoji, VerticalAlignment = VerticalAlignment.Top,
        };
        badge.SetResourceReference(Border.BorderBrushProperty, "NgStroke");
        SetDock(badge, Dock.Left);
        Children.Add(badge);
        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(_step);
        text.Children.Add(_title);
        text.Children.Add(_sub);
        Children.Add(text);
        Step = "";
    }

    public string Emoji { get => _emoji.Text; set => _emoji.Text = value; }
    public string Title { get => _title.Text; set => _title.Text = value; }
    public string Step { get => _step.Text; set { _step.Text = value.ToUpperInvariant(); _step.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible; } }
    public string Subtitle { get => _sub.Text; set { _sub.Text = value; _sub.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible; } }
    public Brush EmojiColor { get => _emoji.Foreground; set => _emoji.Foreground = value; }
}

/// <summary>The welcome page's night sky: gradient, moon, twinkling stars, hills, and Hoot the owl on a branch.</summary>
public sealed class NightScene : Border
{
    private readonly TextBlock _headline = new() { Foreground = Brushes.White, FontSize = 36, FontWeight = FontWeights.Bold, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _tagline = new() { Foreground = new SolidColorBrush(Color.FromArgb(0xE0, 0xFF, 0xFF, 0xFF)), FontSize = 17, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 0) };
    private readonly Owl _owl = new() { Width = 120, Height = 130 };

    public NightScene()
    {
        Background = Art.NightSky();
        CornerRadius = new CornerRadius(22);
        BorderThickness = new Thickness(2, 2, 2, 6);
        SetResourceReference(BorderBrushProperty, "NgStroke");
        MinHeight = 250;
        ClipToBounds = true;
        Margin = new Thickness(0, 0, 0, 24);
        _headline.SetResourceReference(TextBlock.FontFamilyProperty, "NgDisplay");
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var artColumn = new ColumnDefinition { Width = new GridLength(250) };
        grid.ColumnDefinitions.Add(artColumn);

        var sky = new Canvas { IsHitTestVisible = false };
        Grid.SetColumnSpan(sky, 2);
        sky.Loaded += (_, _) => { if (sky.Children.Count == 0) Art.Stars(sky, 1600, 260, 80, 42); };
        grid.Children.Add(sky);

        var hills = new Path
        {
            Data = Geometry.Parse("M0,60 C150,10 300,10 450,45 C600,80 750,20 900,35 C1050,50 1200,15 1400,40 L1400,90 L0,90 Z"),
            Fill = new SolidColorBrush(Art.C("#120F2E")), Stretch = Stretch.Fill, Height = 60, VerticalAlignment = VerticalAlignment.Bottom,
        };
        Grid.SetColumnSpan(hills, 2);
        grid.Children.Add(hills);

        var art = new Grid { Margin = new Thickness(0, 18, 18, 0) };
        Grid.SetColumn(art, 1);
        var moonHolder = new Grid { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 18, 0) };
        moonHolder.Children.Add(new Ellipse
        {
            Width = 130, Height = 130, Margin = new Thickness(-26),
            Fill = new RadialGradientBrush(Color.FromArgb(0x55, 0xFF, 0xD6, 0x66), Color.FromArgb(0, 0xFF, 0xD6, 0x66)),
        });
        moonHolder.Children.Add(Art.Crescent(78, Art.Moon));
        art.Children.Add(moonHolder);
        // A branch for Hoot to sit on
        art.Children.Add(new Path
        {
            Data = Geometry.Parse("M 0,10 C 60,0 140,14 240,4"), Stroke = new SolidColorBrush(Art.C("#5A3A28")), StrokeThickness = 9,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(-30, 0, -10, 46),
        });
        _owl.HorizontalAlignment = HorizontalAlignment.Left;
        _owl.VerticalAlignment = VerticalAlignment.Bottom;
        _owl.Margin = new Thickness(40, 0, 0, 50);
        art.Children.Add(_owl);
        var zzz = Art.Zzz(18);
        zzz.HorizontalAlignment = HorizontalAlignment.Left;
        zzz.VerticalAlignment = VerticalAlignment.Bottom;
        zzz.Margin = new Thickness(150, 0, 0, 160);
        art.Children.Add(zzz);
        grid.Children.Add(art);

        var text = new StackPanel { Margin = new Thickness(34, 34, 12, 70), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(_headline);
        text.Children.Add(_tagline);
        grid.Children.Add(text);
        Child = grid;

        // Narrow window: drop the artwork, keep the words
        SizeChanged += (_, e) =>
        {
            var narrow = e.NewSize.Width < 620;
            artColumn.Width = new GridLength(narrow ? 0 : 250);
            art.Visibility = narrow ? Visibility.Collapsed : Visibility.Visible;
        };
    }

    public string Headline { get => _headline.Text; set => _headline.Text = value; }
    public string Tagline { get => _tagline.Text; set => _tagline.Text = value; }
    public OwlMood OwlMood { get => _owl.Mood; set => _owl.Mood = value; }
}
