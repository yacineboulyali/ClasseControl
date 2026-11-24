using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Input;

namespace ClasseControle.Agent;

/// <summary>
/// Fenêtre de verrouillage : plein écran, toujours au premier plan, sans
/// décorations, qui recouvre l'écran de l'élève et affiche un message.
/// Elle avale les entrées clavier/souris pour empêcher l'utilisation du poste.
/// </summary>
internal sealed class LockOverlay : Window
{
    private readonly TextBlock _message;

    public LockOverlay(string message)
    {
        SystemDecorations = SystemDecorations.None;
        WindowState = WindowState.FullScreen;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        Background = new SolidColorBrush(Color.FromRgb(13, 17, 23));
        Cursor = new Cursor(StandardCursorType.None);

        _message = new TextBlock
        {
            Text = message,
            Foreground = Brushes.White,
            FontSize = 34,
            FontWeight = FontWeight.SemiBold,
            TextAlignment = TextAlignment.Center,
            TextWrapping = TextWrapping.Wrap,
            MaxWidth = 900,
        };

        var sub = new TextBlock
        {
            Text = "Veuillez patienter — le professeur contrôle ce poste.",
            Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158)),
            FontSize = 18,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 18, 0, 0),
        };

        var lockGlyph = new TextBlock
        {
            Text = "🔒",
            FontSize = 72,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, 24),
        };

        Content = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { lockGlyph, _message, sub },
        };

        // Avaler les entrées : on marque les événements comme traités.
        KeyDown += (_, e) => e.Handled = true;
        KeyUp += (_, e) => e.Handled = true;
        PointerPressed += (_, e) => e.Handled = true;
        // Réaffirmer le premier plan si on tente de passer derrière.
        Deactivated += (_, _) => { Topmost = true; Activate(); };
    }

    public void UpdateMessage(string message) => _message.Text = message;
}
