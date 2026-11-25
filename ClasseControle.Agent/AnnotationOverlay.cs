using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ClasseControle.Common;
using ClasseControle.Shared;

namespace ClasseControle.Agent;

/// <summary>
/// Calque transparent, au premier plan, qui couvre tout l'écran de l'élève et affiche les
/// annotations du professeur (stylo, surligneur, formes, flèches).
/// Il est exclu des captures d'écran : le professeur voit l'écran réel + ses propres traits
/// (pas de doublon). Si la transparence n'est pas disponible, une image figée de l'écran
/// sert de fond pour que l'élève voie toujours son écran sous les annotations.
/// </summary>
internal sealed class AnnotationOverlay : Window
{
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
    [DllImport("user32.dll")]
    private static extern bool SetWindowDisplayAffinity(IntPtr hWnd, uint affinity);

    private readonly Canvas _canvas = new();
    private readonly Image _frozen = new() { Stretch = Stretch.Fill, IsVisible = false };
    private readonly AnnotationRenderer _renderer;

    public AnnotationOverlay()
    {
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Background = Brushes.Transparent;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        WindowStartupLocation = WindowStartupLocation.Manual;
        Content = new Grid { Children = { _frozen, _canvas } };
        _renderer = new AnnotationRenderer(_canvas);

        // Couvre l'écran virtuel (tous les moniteurs), comme la capture envoyée au professeur.
        var cap = new ScreenCapture();
        Position = new PixelPoint(cap.VirtualX, cap.VirtualY);
        double scale = Screens.Primary?.Scaling ?? 1;
        Width = cap.VirtualWidth / scale;
        Height = cap.VirtualHeight / scale;

        Opened += (_, _) =>
        {
            try
            {
                if (TryGetPlatformHandle()?.Handle is { } h && h != IntPtr.Zero)
                    SetWindowDisplayAffinity(h, WDA_EXCLUDEFROMCAPTURE);
            }
            catch { }
            if (ActualTransparencyLevel == WindowTransparencyLevel.None) FreezeBackground(cap);
            _renderer.SetArea(new Rect(0, 0, Bounds.Width, Bounds.Height));
        };
        SizeChanged += (_, _) => _renderer.SetArea(new Rect(0, 0, Bounds.Width, Bounds.Height));
    }

    /// <summary>Repli sans transparence : l'écran actuel (figé) sert de fond.</summary>
    private void FreezeBackground(ScreenCapture cap)
    {
        try
        {
            var (jpeg, _, _, _, _) = cap.CaptureJpeg(0, 85);
            using var ms = new MemoryStream(jpeg);
            _frozen.Source = new Bitmap(ms);
            _frozen.IsVisible = true;
            Background = Brushes.Black;
        }
        catch { }
    }

    public void Apply(AnnotationPayload p)
    {
        switch (p.Action)
        {
            case AnnotationAction.Stroke when p.Stroke != null: _renderer.Upsert(p.Stroke); break;
            case AnnotationAction.Undo: _renderer.Remove(p.StrokeId); break;
            case AnnotationAction.Clear: _renderer.Clear(); break;
        }
    }
}
