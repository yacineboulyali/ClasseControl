using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia;
using Avalonia.Media;

namespace ClasseControle.Console;

/// <summary>Boîte de saisie d'une ligne de texte.</summary>
internal sealed class InputDialog : Window
{
    private readonly TextBox _box;
    public string? Result { get; private set; }

    public InputDialog(string title, string prompt, string placeholder = "", string initial = "")
    {
        Title = title;
        Width = 440;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        _box = new TextBox { Watermark = placeholder, Text = initial };
        var ok = new Button { Content = "Valider", Padding = new Thickness(16, 6, 16, 6) };
        var cancel = new Button { Content = "Annuler", Padding = new Thickness(16, 6, 16, 6) };
        ok.Click += (_, _) => { Result = _box.Text; Close(); };
        cancel.Click += (_, _) => { Result = null; Close(); };

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock { Text = prompt, Margin = new Thickness(0,0,0,10), TextWrapping = TextWrapping.Wrap },
                _box,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8, Margin = new Thickness(0,14,0,0),
                    Children = { cancel, ok },
                },
            },
        };
    }
}

/// <summary>Composition d'un message (titre + corps) à envoyer aux élèves.</summary>
internal sealed class ComposeMessageDialog : Window
{
    private readonly TextBox _title;
    private readonly TextBox _text;
    public (string title, string text)? Result { get; private set; }

    public ComposeMessageDialog()
    {
        Title = "Envoyer un message";
        Width = 480;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        _title = new TextBox { Text = "Message du professeur" };
        _text = new TextBox
        {
            Watermark = "Votre message…",
            AcceptsReturn = true,
            Height = 120,
            TextWrapping = TextWrapping.Wrap,
        };
        var ok = new Button { Content = "Envoyer", Padding = new Thickness(16, 6, 16, 6) };
        var cancel = new Button { Content = "Annuler", Padding = new Thickness(16, 6, 16, 6) };
        ok.Click += (_, _) => { Result = (_title.Text ?? "", _text.Text ?? ""); Close(); };
        cancel.Click += (_, _) => { Result = null; Close(); };

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Children =
            {
                new TextBlock { Text = "Titre", Margin = new Thickness(0,0,0,4) },
                _title,
                new TextBlock { Text = "Message", Margin = new Thickness(0,12,0,4) },
                _text,
                new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Right,
                    Spacing = 8, Margin = new Thickness(0,14,0,0),
                    Children = { cancel, ok },
                },
            },
        };
    }
}
