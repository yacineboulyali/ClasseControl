using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace ClasseControle.Agent;

/// <summary>
/// Affiche l'écran du professeur diffusé à la classe.
/// En mode exclusif : plein écran, toujours au premier plan, entrées avalées
/// (comme le verrouillage). Sinon : fenêtre normale que l'élève peut
/// redimensionner ou mettre de côté.
/// </summary>
internal sealed class BroadcastWindow : Window
{
    private readonly Image _image;
    private readonly TextBlock _waiting;
    private Bitmap? _current;
    private bool _exclusive;

    public BroadcastWindow(string title, bool exclusive)
    {
        Title = title;
        Background = Brushes.Black;
        Width = 1100;
        Height = 700;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _image = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
        };
        _waiting = new TextBlock
        {
            Text = "📺  Diffusion de l'écran du professeur…",
            Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158)),
            FontSize = 22,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        Content = new Grid { Children = { _waiting, _image } };

        KeyDown += (_, e) => { if (_exclusive) e.Handled = true; };
        KeyUp += (_, e) => { if (_exclusive) e.Handled = true; };
        PointerPressed += (_, e) => { if (_exclusive) e.Handled = true; };
        // Reprend le premier plan, sauf si l'écran de verrouillage est affiché
        // (sinon les deux fenêtres se volent le focus en boucle).
        Deactivated += (_, _) => { if (_exclusive && !(YieldTo?.Invoke() ?? false)) { Topmost = true; Activate(); } };
        Closing += (_, e) => { if (_exclusive && !_allowClose) e.Cancel = true; };

        SetMode(title, exclusive);
    }

    private bool _allowClose;

    /// <summary>Renvoie vrai si une autre fenêtre prioritaire (verrouillage) doit garder le focus.</summary>
    public Func<bool>? YieldTo { get; set; }

    public void SetMode(string title, bool exclusive)
    {
        Title = title;
        _exclusive = exclusive;
        Topmost = exclusive;
        ShowInTaskbar = !exclusive;
        SystemDecorations = exclusive ? SystemDecorations.None : SystemDecorations.Full;
        WindowState = exclusive ? WindowState.FullScreen : WindowState.Normal;
        Cursor = exclusive ? new Cursor(StandardCursorType.None) : Cursor.Default;
    }

    public void ShowFrame(byte[] jpeg)
    {
        try
        {
            using var ms = new MemoryStream(jpeg);
            var bmp = new Bitmap(ms);
            _image.Source = bmp;
            _current?.Dispose();
            _current = bmp;
            _waiting.IsVisible = false;
        }
        catch { }
    }

    public void ForceClose()
    {
        _allowClose = true;
        Close();
        _current?.Dispose();
        _current = null;
    }
}
