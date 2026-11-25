using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using NightGuard.Core.Trusted;

namespace NightGuard.App;

/// <summary>
/// The trusted person's code: asking for it, the "no recovery" warning, and setting a new one.
/// The code is never shown, saved, emailed or printed; only the service keeps a salted hash.
/// </summary>
internal static class CodeDialogs
{
    private static Window NewDialog(Window? owner, string title, double width)
    {
        var w = new Window
        {
            Title = "NightGuard - " + title, Width = width, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = owner is null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
            ShowInTaskbar = owner is null, Owner = owner,
        };
        return w;
    }

    private static Border Header(string text, Brush background) => new()
    {
        Background = background, Padding = new Thickness(20, 14, 20, 14),
        Child = new TextBlock { Text = text, Foreground = Brushes.White, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap },
    };

    private static TextBlock Para(string text, double top = 0, bool bold = false, Brush? color = null)
    {
        var t = new TextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 0), FontSize = 14,
            FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal,
        };
        if (color is not null) t.Foreground = color;
        return t;
    }

    private static PasswordBox DigitsBox() => new()
    {
        MaxLength = 8, Width = 180, FontFamily = new FontFamily("Consolas"), FontSize = 18, HorizontalAlignment = HorizontalAlignment.Left,
    };

    private static StackPanel Buttons(Button ok, Button cancel)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 20, 0, 0) };
        ok.Padding = cancel.Padding = new Thickness(16, 8, 16, 8);
        ok.Margin = new Thickness(0, 0, 10, 0);
        ok.IsDefault = true;
        cancel.IsCancel = true;
        sp.Children.Add(ok);
        sp.Children.Add(cancel);
        return sp;
    }

    private static Brush Red => (Brush)Application.Current.FindResource("NgRed");
    private static Brush Indigo => (Brush)Application.Current.FindResource("NgIndigo");

    /// <summary>Asks for the code. Returns what was typed, or null if cancelled.</summary>
    public static string? AskCode(Window? owner, string why)
    {
        var w = NewDialog(owner, "code needed", 440);
        var box = DigitsBox();
        var ok = new Button { Content = "OK" };
        var cancel = new Button { Content = "Cancel" };
        var body = new StackPanel { Margin = new Thickness(20) };
        body.Children.Add(Para(why));
        body.Children.Add(Para("Enter the trusted person's 8-digit code:", 10, bold: true));
        box.Margin = new Thickness(0, 10, 0, 0);
        body.Children.Add(box);
        body.Children.Add(Buttons(ok, cancel));
        w.Content = body;
        ok.Click += (_, _) => w.DialogResult = true;
        w.Loaded += (_, _) => box.Focus();
        var result = w.ShowDialog() == true ? box.Password : null;
        box.Clear();
        return result;
    }

    /// <summary>The "read this first" warning before a code is set. True if they understood and continue.</summary>
    public static bool Warning(Window? owner)
    {
        var w = NewDialog(owner, "read this first", 580);
        var root = new DockPanel();
        var header = Header("WARNING: THERE IS NO WAY TO RECOVER A LOST CODE", Red);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        var body = new StackPanel { Margin = new Thickness(20) };
        body.Children.Add(Para(
            "With trusted-person mode, someone you trust sets a secret 8-digit code. After that:\n\n" +
            "  •  Changing ANY setting or uninstalling NightGuard needs the code.\n" +
            "  •  Only the trusted person knows it. They type it in themselves, and this PC only keeps a scrambled copy.\n" +
            "  •  If the code is forgotten, NOBODY can recover it. Not you, not the trusted person, not the makers of this tool.\n\n" +
            "Have your trusted person next to you when you save. They'll type the code twice. They GOT TO REMEMBER THE CODE (or store it safely)."));
        var chk = new CheckBox { Content = "I understand: if the code is lost, it can never be recovered.", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 16, 0, 0) };
        body.Children.Add(chk);
        var ok = new Button { Content = "Continue", IsEnabled = false };
        var cancel = new Button { Content = "Cancel" };
        chk.Checked += (_, _) => ok.IsEnabled = true;
        chk.Unchecked += (_, _) => ok.IsEnabled = false;
        ok.Click += (_, _) => w.DialogResult = true;
        body.Children.Add(Buttons(ok, cancel));
        root.Children.Add(body);
        w.Content = root;
        return w.ShowDialog() == true;
    }

    /// <summary>The trusted person picks their own code and types it twice, hidden. Returns it, or null if cancelled.</summary>
    public static string? NewCode(Window? owner, string? name)
    {
        var who = string.IsNullOrWhiteSpace(name) ? "the trusted person" : name.Trim();
        var w = NewDialog(owner, "trusted person sets the code", 620);
        var root = new DockPanel();
        var header = Header($"Hand the keyboard to {who}", Indigo);
        DockPanel.SetDock(header, Dock.Top);
        root.Children.Add(header);
        var body = new StackPanel { Margin = new Thickness(20) };
        body.Children.Add(Para(
            "Trusted person: pick your own 8-digit code and type it twice below. Only you will know it. Nothing is shown on screen, emailed or printed.\n\n" +
            "Remember it, or store it somewhere safe that only you can get to (like your password manager). Don't tell the person you're doing this for, and don't write it anywhere near this PC."));
        var grid = new Grid { Margin = new Thickness(0, 16, 0, 0) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(170) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        var b1 = DigitsBox();
        var b2 = DigitsBox();
        b2.Margin = new Thickness(0, 8, 0, 0);
        var l1 = Para("Code (8 digits):", bold: true);
        var l2 = Para("Type it again:", 8, bold: true);
        l1.VerticalAlignment = l2.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetRow(l2, 1); Grid.SetRow(b2, 1); Grid.SetColumn(b1, 1); Grid.SetColumn(b2, 1);
        grid.Children.Add(l1); grid.Children.Add(b1); grid.Children.Add(l2); grid.Children.Add(b2);
        body.Children.Add(grid);
        var problem = Para("", 10, color: Red);
        body.Children.Add(problem);
        body.Children.Add(Para("THERE IS NO WAY TO RECOVER A LOST CODE. If it's forgotten, settings and uninstall stay locked for good " +
                               "(bedtime still ends at unlock time, and the end date still removes NightGuard).", 10, bold: true, color: Red));
        var chk = new CheckBox
        {
            Margin = new Thickness(0, 14, 0, 0),
            Content = new TextBlock { Text = "I am the trusted person. I will remember this code or keep it safe, and I understand it can never be recovered.", TextWrapping = TextWrapping.Wrap },
        };
        body.Children.Add(chk);
        var ok = new Button { Content = "Set the code", IsEnabled = false };
        var cancel = new Button { Content = "Cancel" };
        void Check()
        {
            var a = b1.Password;
            var b = b2.Password;
            var p = a.Length < 8 ? "" : TrustedCode.Problem(a) ?? (b.Length == 8 && a != b ? "The two codes don't match." : "");
            problem.Text = p;
            ok.IsEnabled = chk.IsChecked == true && a.Length == 8 && a == b && TrustedCode.Problem(a) is null;
        }
        void DigitsOnly(object s, TextCompositionEventArgs e) { if (!e.Text.All(char.IsAsciiDigit)) e.Handled = true; }
        b1.PreviewTextInput += DigitsOnly;
        b2.PreviewTextInput += DigitsOnly;
        b1.PasswordChanged += (_, _) => Check();
        b2.PasswordChanged += (_, _) => Check();
        chk.Checked += (_, _) => Check();
        chk.Unchecked += (_, _) => Check();
        ok.Click += (_, _) => w.DialogResult = true;
        body.Children.Add(Buttons(ok, cancel));
        root.Children.Add(body);
        w.Content = root;
        w.Loaded += (_, _) => b1.Focus();
        var result = w.ShowDialog() == true ? b1.Password : null;
        b1.Clear();
        b2.Clear();
        return result;
    }
}
