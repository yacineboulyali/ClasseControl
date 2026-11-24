using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ClasseControle.Common;

namespace ClasseControle.Console;

/// <summary>
/// Fenêtre affichant l'écran d'un élève en direct, avec prise de contrôle
/// optionnelle (souris + clavier) renvoyée à l'agent.
/// </summary>
internal sealed class RemoteViewWindow : Window
{
    private readonly AgentConnection _conn;
    private readonly Image _image;
    private readonly CheckBox _controlToggle;
    private Bitmap? _current;
    private int _srcW = 1, _srcH = 1;

    public RemoteViewWindow(AgentConnection conn)
    {
        _conn = conn;
        Title = $"Écran — {conn.Title}";
        Width = 1000;
        Height = 650;
        Background = Brushes.Black;

        _image = new Image
        {
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Focusable = true,
        };

        _controlToggle = new CheckBox { Content = "Prendre le contrôle (souris + clavier)", Margin = new Thickness(8, 0, 0, 0) };

        var toolbar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Background = new SolidColorBrush(Color.FromRgb(22, 27, 34)),
            Height = 40,
            Children = { _controlToggle },
        };

        var grid = new Grid();
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        grid.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        Grid.SetRow(toolbar, 0);
        Grid.SetRow(_image, 1);
        grid.Children.Add(toolbar);
        grid.Children.Add(_image);
        Content = grid;

        _conn.FrameReceived += OnFrame;

        // Entrées souris
        _image.PointerMoved += (_, e) => SendMouse(InputKind.MouseMove, e);
        _image.PointerPressed += (_, e) => { _image.Focus(); SendMouseButton(e, down: true); };
        _image.PointerReleased += (_, e) => SendMouseButton(e, down: false);
        _image.PointerWheelChanged += (_, e) =>
        {
            if (!Control) return;
            _ = _conn.SendInput(new RemoteInputPayload { Kind = InputKind.MouseWheel, Wheel = (int)(e.Delta.Y * 120) });
        };

        // Entrées clavier
        KeyDown += (_, e) => SendKey(e, down: true);
        KeyUp += (_, e) => SendKey(e, down: false);

        Opened += async (_, _) => await _conn.StartScreen(new StartScreenPayload
        {
            Quality = 55, MaxWidth = 1600, Fps = 6, AllowControl = true,
        });
        Closed += async (_, _) =>
        {
            _conn.FrameReceived -= OnFrame;
            try { await _conn.StopScreen(); } catch { }
            _current?.Dispose();
        };
    }

    private bool Control => _controlToggle.IsChecked == true;

    private void OnFrame(ScreenFramePayload f) => Dispatcher.UIThread.Post(() =>
    {
        try
        {
            _srcW = f.ScreenWidth > 0 ? f.ScreenWidth : f.Width;
            _srcH = f.ScreenHeight > 0 ? f.ScreenHeight : f.Height;
            using var ms = new MemoryStream(f.Jpeg);
            var bmp = new Bitmap(ms);
            _image.Source = bmp;
            _current?.Dispose();
            _current = bmp;
        }
        catch { }
    });

    /// <summary>Convertit une position pointeur en coordonnées normalisées 0..1,
    /// en tenant compte du letterboxing (Stretch=Uniform).</summary>
    private bool TryNormalize(Point p, out double nx, out double ny)
    {
        nx = ny = 0;
        double cw = _image.Bounds.Width, ch = _image.Bounds.Height;
        if (cw <= 0 || ch <= 0 || _srcW <= 0 || _srcH <= 0) return false;

        double scale = Math.Min(cw / _srcW, ch / _srcH);
        double dispW = _srcW * scale, dispH = _srcH * scale;
        double offX = (cw - dispW) / 2, offY = (ch - dispH) / 2;

        double x = p.X - offX, y = p.Y - offY;
        if (x < 0 || y < 0 || x > dispW || y > dispH) return false;

        nx = x / dispW;
        ny = y / dispH;
        return true;
    }

    private void SendMouse(InputKind kind, PointerEventArgs e)
    {
        if (!Control) return;
        if (!TryNormalize(e.GetPosition(_image), out double nx, out double ny)) return;
        _ = _conn.SendInput(new RemoteInputPayload { Kind = kind, X = nx, Y = ny });
    }

    private void SendMouseButton(PointerEventArgs e, bool down)
    {
        if (!Control) return;
        var pt = e.GetCurrentPoint(_image);
        int button = pt.Properties.IsRightButtonPressed || (!down && LastWasRight(pt)) ? 1 : 0;
        // Déterminer le bouton de façon robuste selon l'état.
        button = pt.Properties.PointerUpdateKind switch
        {
            PointerUpdateKind.LeftButtonPressed or PointerUpdateKind.LeftButtonReleased => 0,
            PointerUpdateKind.RightButtonPressed or PointerUpdateKind.RightButtonReleased => 1,
            PointerUpdateKind.MiddleButtonPressed or PointerUpdateKind.MiddleButtonReleased => 2,
            _ => 0,
        };
        if (!TryNormalize(e.GetPosition(_image), out double nx, out double ny)) return;
        // On déplace d'abord puis on clique, pour positionner le curseur distant.
        _ = _conn.SendInput(new RemoteInputPayload { Kind = InputKind.MouseMove, X = nx, Y = ny });
        _ = _conn.SendInput(new RemoteInputPayload
        {
            Kind = down ? InputKind.MouseDown : InputKind.MouseUp,
            X = nx, Y = ny, Button = button,
        });
    }

    private static bool LastWasRight(PointerPoint _) => false;

    private void SendKey(KeyEventArgs e, bool down)
    {
        if (!Control) return;
        int vk = VkMap.ToVk(e.Key);
        if (vk == 0) return;
        e.Handled = true;
        _ = _conn.SendInput(new RemoteInputPayload
        {
            Kind = down ? InputKind.KeyDown : InputKind.KeyUp,
            KeyCode = vk,
        });
    }
}
