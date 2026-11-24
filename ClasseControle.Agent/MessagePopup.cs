using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

namespace ClasseControle.Agent;

/// <summary>
/// Popup affichant un message/consigne envoyé par le professeur.
/// Se ferme sur clic de l'élève ou après une durée optionnelle.
/// </summary>
internal sealed class MessagePopup : Window
{
    public MessagePopup(string title, string text, int durationSeconds)
    {
        Title = title;
        Width = 520;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        CanResize = false;
        Background = new SolidColorBrush(Color.FromRgb(22, 27, 34));

        var titleBlock = new TextBlock
        {
            Text = title,
            Foreground = Brushes.White,
            FontSize = 20,
            FontWeight = FontWeight.Bold,
            Margin = new Thickness(0, 0, 0, 12),
            TextWrapping = TextWrapping.Wrap,
        };

        var textBlock = new TextBlock
        {
            Text = text,
            Foreground = new SolidColorBrush(Color.FromRgb(201, 209, 217)),
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
        };

        var ok = new Button
        {
            Content = "J'ai compris",
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0),
            Padding = new Thickness(18, 8, 18, 8),
        };
        ok.Click += (_, _) => Close();

        Content = new StackPanel
        {
            Margin = new Thickness(24),
            Children = { titleBlock, textBlock, ok },
        };

        if (durationSeconds > 0)
        {
            DispatcherTimer.RunOnce(() => { try { Close(); } catch { } },
                TimeSpan.FromSeconds(durationSeconds));
        }
    }
}
