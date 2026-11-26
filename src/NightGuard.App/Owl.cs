using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace NightGuard.App;

public enum OwlMood { Awake, Happy, Sleepy }

/// <summary>
/// Hoot, NightGuard's night-owl mascot. Drawn from simple vector shapes (so it's crisp at any size
/// and scaling), it blinks every few seconds and bobs gently. Sleepy wears a nightcap; Happy squints.
/// </summary>
public sealed class Owl : Viewbox
{
    private static readonly Color Body = Art.C("#7B5CC4");
    private static readonly Color Wing = Art.C("#5E43A6");
    private static readonly Color Belly = Art.C("#F3D9A4");
    private static readonly Color Feather = Art.C("#D6B271");
    private static readonly Color Face = Art.C("#E9E0FF");
    private static readonly Color Pupil = Art.C("#1A1433");
    private static readonly Color Beak = Art.C("#FFB347");

    private OwlMood _mood = OwlMood.Awake;
    private bool _bob = true;

    public Owl()
    {
        Stretch = Stretch.Uniform;
        Width = 120;
        Height = 130;
        IsHitTestVisible = false;
        Build();
    }

    public OwlMood Mood { get => _mood; set { _mood = value; Build(); } }
    public bool Bob { get => _bob; set { _bob = value; Build(); } }

    private static SolidColorBrush B(Color c) => new(c);

    private void Build()
    {
        var c = new Canvas { Width = 120, Height = 130 };

        // Ear tufts
        c.Children.Add(Poly(B(Body), (24, 36), (20, 8), (48, 26)));
        c.Children.Add(Poly(B(Body), (96, 36), (100, 8), (72, 26)));
        // Wings behind the body
        c.Children.Add(At(new Ellipse { Width = 26, Height = 52, Fill = B(Wing), RenderTransform = new RotateTransform(14, 13, 26) }, 8, 56));
        c.Children.Add(At(new Ellipse { Width = 26, Height = 52, Fill = B(Wing), RenderTransform = new RotateTransform(-14, 13, 26) }, 86, 56));
        // Body and belly
        c.Children.Add(At(new Ellipse { Width = 86, Height = 104, Fill = B(Body) }, 17, 16));
        c.Children.Add(At(new Ellipse { Width = 50, Height = 54, Fill = B(Belly) }, 35, 62));
        foreach (var (x, y) in new[] { (48.0, 80.0), (60.0, 76.0), (72.0, 80.0), (54.0, 92.0), (66.0, 92.0), (60.0, 104.0) })
            c.Children.Add(new Path { Data = Geometry.Parse($"M {x - 4},{y} Q {x},{y + 5} {x + 4},{y}"), Stroke = B(Feather), StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
        // Feet
        c.Children.Add(At(new Ellipse { Width = 16, Height = 8, Fill = B(Beak) }, 38, 116));
        c.Children.Add(At(new Ellipse { Width = 16, Height = 8, Fill = B(Beak) }, 66, 116));
        // Face disks and blush
        c.Children.Add(At(new Ellipse { Width = 42, Height = 42, Fill = B(Face) }, 23, 31));
        c.Children.Add(At(new Ellipse { Width = 42, Height = 42, Fill = B(Face) }, 55, 31));
        c.Children.Add(At(new Ellipse { Width = 12, Height = 7, Fill = B(Art.C("#FF8AB4")), Opacity = 0.55 }, 22, 70));
        c.Children.Add(At(new Ellipse { Width = 12, Height = 7, Fill = B(Art.C("#FF8AB4")), Opacity = 0.55 }, 86, 70));

        // Eyes
        if (_mood == OwlMood.Awake)
        {
            var eyes = new Canvas { Width = 120, Height = 130 };
            foreach (var cx in new[] { 46.0, 74.0 })
            {
                eyes.Children.Add(At(new Ellipse { Width = 22, Height = 22, Fill = B(Pupil) }, cx - 11, 42));
                eyes.Children.Add(At(new Ellipse { Width = 7, Height = 7, Fill = Brushes.White }, cx - 1, 45));
                eyes.Children.Add(At(new Ellipse { Width = 3.5, Height = 3.5, Fill = Brushes.White, Opacity = 0.8 }, cx - 7, 56));
            }
            var blink = new ScaleTransform(1, 1, 60, 53);
            eyes.RenderTransform = blink;
            var anim = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever, Duration = TimeSpan.FromSeconds(4.6) };
            anim.KeyFrames.Add(new DiscreteDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.Zero)));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.3))));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(0.08, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.4))));
            anim.KeyFrames.Add(new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(4.55))));
            blink.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
            c.Children.Add(eyes);
        }
        else
        {
            // Closed (sleepy, curved down) or squinting (happy, curved up)
            var up = _mood == OwlMood.Happy;
            foreach (var cx in new[] { 46.0, 74.0 })
            {
                var d = up ? $"M {cx - 9},{56} Q {cx},{44} {cx + 9},{56}" : $"M {cx - 9},{52} Q {cx},{61} {cx + 9},{52}";
                c.Children.Add(new Path { Data = Geometry.Parse(d), Stroke = B(Pupil), StrokeThickness = 3.5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            }
        }
        // Beak
        c.Children.Add(Poly(B(Beak), (54, 62), (66, 62), (60, 73)));

        if (_mood == OwlMood.Sleepy)
        {
            // A nightcap with a pompom
            c.Children.Add(new Path { Data = Geometry.Parse("M 30,30 Q 60,4 98,2 Q 84,18 92,34 Q 62,22 30,30 Z"), Fill = B(Art.C("#5FD8CB")) });
            c.Children.Add(new Path { Data = Geometry.Parse("M 28,30 Q 60,20 94,34"), Stroke = Brushes.White, StrokeThickness = 5, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round });
            c.Children.Add(At(new Ellipse { Width = 12, Height = 12, Fill = Brushes.White }, 93, -4));
        }

        if (_bob)
        {
            var move = new TranslateTransform();
            c.RenderTransform = move;
            move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -3, TimeSpan.FromSeconds(1.6))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
            });
        }
        Child = c;
    }

    private static T At<T>(T e, double x, double y) where T : UIElement
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        return e;
    }

    private static Polygon Poly(Brush fill, params (double X, double Y)[] pts) =>
        new() { Fill = fill, Points = new PointCollection(pts.Select(p => new Point(p.X, p.Y))) };
}
