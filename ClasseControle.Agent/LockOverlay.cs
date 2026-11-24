using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Input;

namespace ClasseControle.Agent;

/// <summary>
/// Fenêtre de verrouillage : plein écran, toujours au premier plan, sans
/// décorations, qui recouvre l'écran de l'élève et affiche un message et,
/// si le professeur l'a configurée, une image de fond.
/// Elle avale les entrées clavier/souris pour empêcher l'utilisation du poste.
/// </summary>
internal sealed class LockOverlay : Window
{
    private readonly TextBlock _message;
    private readonly Image _background;
    private readonly Border _shade;
    private Bitmap? _bitmap;

    public Func<bool>? YieldTo { get; set; }

    public LockOverlay(string message, byte[]? image)
    {
        SystemDecorations = SystemDecorations.None;
        WindowState = WindowState.FullScreen;
        Topmost = true;
        ShowInTaskbar = false;
        CanResize = false;
        Background = new SolidColorBrush(Color.FromRgb(13, 17, 23));
        Cursor = new Cursor(StandardCursorType.None);

        _background = new Image { Stretch = Stretch.UniformToFill };
        _shade = new Border { Background = new SolidColorBrush(Color.FromArgb(150, 0, 0, 0)), IsVisible = false };

        _message = new TextBlock
        {
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
            Foreground = new SolidColorBrush(Color.FromRgb(201, 209, 217)),
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

        var center = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { lockGlyph, _message, sub },
        };
        Content = new Grid { Children = { _background, _shade, center } };

        Update(message, image);

        // Avaler les entrées : on marque les événements comme traités.
        KeyDown += (_, e) => e.Handled = true;
        KeyUp += (_, e) => e.Handled = true;
        PointerPressed += (_, e) => e.Handled = true;
        // Réaffirmer le premier plan si on tente de passer derrière.
        Deactivated += (_, _) => { if (!(YieldTo?.Invoke() ?? false)) { Topmost = true; Activate(); } };
        Closed += (_, _) => _bitmap?.Dispose();
    }

    public void Update(string message, byte[]? image)
    {
        _message.Text = message;
        if (image is { Length: > 0 })
        {
            try
            {
                using var ms = new MemoryStream(image);
                var bmp = new Bitmap(ms);
                _background.Source = bmp;
                _bitmap?.Dispose();
                _bitmap = bmp;
            }
            catch { }
        }
        else
        {
            _background.Source = null;
            _bitmap?.Dispose();
            _bitmap = null;
        }
        _shade.IsVisible = _bitmap != null;
    }
}
