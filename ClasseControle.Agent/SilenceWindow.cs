using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;

namespace ClasseControle.Agent;

/// <summary>
/// Demande de silence : grande fenêtre au premier plan avec l'emoji 🤫 (visage avec un
/// doigt sur la bouche) qui « respire », le message du professeur, et fermeture automatique.
/// </summary>
internal sealed class SilenceWindow : Window
{
    private readonly TextBlock _message;
    private DispatcherTimer? _autoClose;

    public SilenceWindow(string message, int seconds)
    {
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = new SolidColorBrush(Color.FromRgb(21, 41, 77));

        var emoji = new TextBlock
        {
            Text = "🤫",
            FontSize = 150,
            FontFamily = new FontFamily("Segoe UI Emoji, Noto Color Emoji, Apple Color Emoji"),
            HorizontalAlignment = HorizontalAlignment.Center,
            RenderTransformOrigin = RelativePoint.Center,
        };
        // Pulsation douce de l'emoji.
        var pulse = new Animation
        {
            Duration = TimeSpan.FromSeconds(1.2),
            IterationCount = IterationCount.Infinite,
            PlaybackDirection = PlaybackDirection.Alternate,
            Easing = new SineEaseInOut(),
            Children =
            {
                new KeyFrame { Cue = new Cue(0), Setters = { new Setter(ScaleTransform.ScaleXProperty, 1.0), new Setter(ScaleTransform.ScaleYProperty, 1.0) } },
                new KeyFrame { Cue = new Cue(1), Setters = { new Setter(ScaleTransform.ScaleXProperty, 1.12), new Setter(ScaleTransform.ScaleYProperty, 1.12) } },
            },
        };
        emoji.RenderTransform = new ScaleTransform();

        _message = new TextBlock
        {
            Text = message,
            Foreground = Brushes.White,
            FontSize = 30,
            FontWeight = FontWeight.Bold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var sub = new TextBlock
        {
            Text = "Le professeur demande le silence.",
            Foreground = new SolidColorBrush(Color.FromRgb(190, 205, 230)),
            FontSize = 16,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 6, 0, 0),
        };
        var ok = new Button
        {
            Content = "D'accord",
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, 18, 0, 0),
            Padding = new Thickness(26, 9),
            FontSize = 16,
            Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
            Foreground = Brushes.White,
        };
        ok.Click += (_, _) => Close();

        Content = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(253, 216, 53)),
            BorderThickness = new Thickness(4),
            Padding = new Thickness(28, 22),
            Child = new StackPanel { Children = { emoji, _message, sub, ok } },
        };

        Opened += (_, _) =>
        {
            _ = pulse.RunAsync(emoji);
            Restart(seconds);
        };
    }

    /// <summary>Nouvelle demande alors que la fenêtre est ouverte : message et minuterie relancés.</summary>
    public void Renew(string message, int seconds)
    {
        _message.Text = message;
        Restart(seconds);
        Activate();
    }

    private void Restart(int seconds)
    {
        _autoClose?.Stop();
        if (seconds <= 0) return;
        _autoClose ??= new DispatcherTimer();
        _autoClose.Interval = TimeSpan.FromSeconds(seconds);
        _autoClose.Tick -= OnAutoClose;
        _autoClose.Tick += OnAutoClose;
        _autoClose.Start();
    }

    private void OnAutoClose(object? sender, EventArgs e)
    {
        _autoClose?.Stop();
        try { Close(); } catch { }
    }
}
