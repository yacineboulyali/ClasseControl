using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ClasseControle.Agent;

/// <summary>Petit bandeau en haut de l'écran : l'élève voit pourquoi rien ne répond.</summary>
internal sealed class InputLockBanner : Window
{
    public InputLockBanner()
    {
        SystemDecorations = SystemDecorations.None;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;
        CanResize = false;
        Focusable = false;
        SizeToContent = SizeToContent.WidthAndHeight;
        // Fenêtre opaque (pas de transparence : fiable sur tous les postes).
        Background = new SolidColorBrush(Color.FromRgb(21, 41, 77));
        Content = new Border
        {
            Padding = new Thickness(22, 10),
            Child = new TextBlock
            {
                Text = "🔒  Clavier et souris bloqués par le professeur — regardez votre écran",
                Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeight.SemiBold,
            },
        };
        WindowStartupLocation = WindowStartupLocation.Manual;
        // La taille finale n'est connue qu'après la mise en page : on place le bandeau juste après.
        Opened += (_, _) => Avalonia.Threading.Dispatcher.UIThread.Post(PlaceTopCenter, Avalonia.Threading.DispatcherPriority.Background);
    }

    private void PlaceTopCenter()
    {
        var screen = Screens.Primary ?? Screens.ScreenFromWindow(this) ?? Screens.All.FirstOrDefault();
        if (screen == null) return;
        var area = screen.WorkingArea;
        double scale = screen.Scaling;
        int w = (int)(Math.Max(Bounds.Width, ClientSize.Width) * scale);
        Position = new PixelPoint(area.X + (area.Width - w) / 2, area.Y);
    }
}
