using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace ClasseControle.Agent;

/// <summary>Petite boîte de dialogue demandant le code de salle pour fermer l'agent.</summary>
internal sealed class PasswordDialog : Window
{
    public bool Success { get; private set; }

    public PasswordDialog(string expectedCode)
    {
        Title = "Fermer l'agent";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Topmost = true;
        CanResize = false;

        var label = new TextBlock
        {
            Text = "Code de salle requis pour fermer l'agent :",
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 10),
        };
        var box = new TextBox { PasswordChar = '•', Watermark = "Code de salle" };
        var error = new TextBlock { Foreground = Brushes.IndianRed, IsVisible = false, Margin = new Thickness(0, 6, 0, 0) };

        var ok = new Button { Content = "Confirmer", Padding = new Thickness(16, 6, 16, 6) };
        var cancel = new Button { Content = "Annuler", Padding = new Thickness(16, 6, 16, 6) };
        cancel.Click += (_, _) => Close();
        ok.Click += (_, _) =>
        {
            if (string.Equals(box.Text?.Trim(), expectedCode, StringComparison.OrdinalIgnoreCase))
            {
                Success = true;
                Close();
            }
            else { error.Text = "Code incorrect."; error.IsVisible = true; }
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Thickness(0, 14, 0, 0),
            Children = { cancel, ok },
        };

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children = { label, box, error, buttons },
        };
    }
}
