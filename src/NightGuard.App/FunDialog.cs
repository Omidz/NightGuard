using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NightGuard.App;

/// <summary>NightGuard's own message box: a coloured header with a big emoji, friendly text, and buttons.</summary>
internal static class FunDialog
{
    public enum Mood { Info, Happy, Warn, Error, Question, Sure }

    public static bool Show(Window? owner, Mood mood, string text, string yes = "OK", string? no = null, bool defaultNo = false)
    {
        var (emoji, title, bg) = mood switch
        {
            Mood.Happy => ("\U0001F389", "Nice!", Art.Sunrise()),
            Mood.Warn => ("⚠️", "Hold up", (Brush)new SolidColorBrush(Art.C("#C2610A"))),
            Mood.Error => ("\U0001F62C", "Oops", Art.Alarm()),
            Mood.Question => ("\U0001F914", "Quick question", Art.NightSky()),
            Mood.Sure => ("\U0001F6D1", "Are you sure?", Art.Alarm()),
            _ => ("\U0001F319", "NightGuard", Art.NightSky()),
        };
        var w = new Window
        {
            Title = "NightGuard", Width = 520, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is { IsLoaded: true } ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            ShowInTaskbar = owner is null,
        };
        if (owner is { IsLoaded: true }) w.Owner = owner;
        try { w.Icon = new System.Windows.Media.Imaging.BitmapImage(new Uri("pack://application:,,,/Assets/NightGuard.ico")); } catch { }

        var root = new DockPanel();
        var header = new Border { Background = bg, Padding = new Thickness(22, 16, 22, 16) };
        var hp = new StackPanel { Orientation = Orientation.Horizontal };
        if (mood is Mood.Info or Mood.Happy or Mood.Question)
            hp.Children.Add(new Owl { Width = 50, Height = 54, Bob = false, Mood = mood == Mood.Happy ? OwlMood.Happy : OwlMood.Awake });
        else hp.Children.Add(Art.Emoji(emoji, 30));
        hp.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center });
        header.Child = hp;
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);

        var body = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        body.Children.Add(new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 14, LineHeight = 21 });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        var yesButton = new Button { Content = yes, MinWidth = 100, Padding = new Thickness(16, 8, 16, 8), IsDefault = !defaultNo };
        yesButton.SetResourceReference(FrameworkElement.StyleProperty, "MoonButton");
        yesButton.Click += (_, _) => w.DialogResult = true;
        buttons.Children.Add(yesButton);
        if (no is not null)
        {
            var noButton = new Button { Content = no, MinWidth = 100, Margin = new Thickness(10, 0, 0, 0), IsCancel = true, IsDefault = defaultNo };
            noButton.SetResourceReference(FrameworkElement.StyleProperty, "ChunkyButton");
            buttons.Children.Add(noButton);
            if (defaultNo) w.Loaded += (_, _) => noButton.Focus();
        }
        else yesButton.IsCancel = true;
        body.Children.Add(buttons);
        root.Children.Add(body);
        w.Content = root;
        return w.ShowDialog() == true;
    }
}
