using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

namespace ClasseControle.Console;

/// <summary>Paramètres : écran de verrouillage personnalisé (texte + image), séance.</summary>
internal sealed class SettingsDialog : Window
{
    private readonly ConsoleSettings _settings;
    private readonly TextBox _lockText;
    private readonly TextBox _lessonTitle;
    private readonly Image _preview;
    private readonly TextBlock _previewText;
    private readonly TextBlock _error;
    private byte[]? _newImage;
    private bool _removeImage;
    public bool Saved { get; private set; }

    public SettingsDialog(ConsoleSettings settings)
    {
        _settings = settings;
        Title = "Paramètres";
        Width = 560;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        CanResize = false;

        _lockText = new TextBox { Text = settings.LockMessage, AcceptsReturn = true, Height = 70, TextWrapping = TextWrapping.Wrap };
        _lessonTitle = new TextBox { Text = settings.LessonTitle, Watermark = "ex : Bureautique — TP Excel" };

        _preview = new Image { Stretch = Stretch.UniformToFill };
        _previewText = new TextBlock
        {
            Foreground = Brushes.White, FontSize = 14, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16),
        };
        var previewBox = new Border
        {
            Height = 180, CornerRadius = new CornerRadius(8), ClipToBounds = true,
            Background = new SolidColorBrush(Color.FromRgb(13, 17, 23)),
            Child = new Grid
            {
                Children =
                {
                    _preview,
                    new Border { Background = new SolidColorBrush(Color.FromArgb(120, 0, 0, 0)) },
                    _previewText,
                },
            },
        };
        _lockText.TextChanged += (_, _) => _previewText.Text = "🔒\n" + _lockText.Text;
        _previewText.Text = "🔒\n" + settings.LockMessage;
        ShowPreview(settings.LockImage());

        var pick = Ui.Btn("🖼 Choisir une image…", async () => await PickImageAsync());
        var remove = Ui.Btn("Retirer l'image", () => { _newImage = null; _removeImage = true; ShowPreview(null); });
        _error = Ui.Hint("");

        Content = new StackPanel
        {
            Margin = new Thickness(20),
            Spacing = 4,
            Children =
            {
                new TextBlock { Text = "Écran de verrouillage", FontWeight = FontWeight.Bold, FontSize = 16 },
                new TextBlock { Text = "Texte affiché aux élèves :", Margin = new Thickness(0, 8, 0, 0) },
                _lockText,
                new TextBlock { Text = "Aperçu :", Margin = new Thickness(0, 8, 0, 0) },
                previewBox,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 8, 0, 0), Children = { pick, remove } },
                _error,
                new TextBlock { Text = "Séance", FontWeight = FontWeight.Bold, FontSize = 16, Margin = new Thickness(0, 16, 0, 0) },
                new TextBlock { Text = "Titre affiché sur l'écran d'entrée des élèves :" },
                _lessonTitle,
                Ui.ButtonRow(Ui.Btn("Annuler", Close), Ui.Btn("💾 Enregistrer", Save)),
            },
        };
    }

    private void ShowPreview(byte[]? jpeg)
    {
        try
        {
            if (jpeg == null) { _preview.Source = null; return; }
            using var ms = new MemoryStream(jpeg);
            _preview.Source = new Bitmap(ms);
        }
        catch { _preview.Source = null; }
    }

    private async Task PickImageAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Image de l'écran de verrouillage",
            AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Images") { Patterns = new[] { "*.jpg", "*.jpeg", "*.png", "*.bmp" } } },
        });
        var f = files.FirstOrDefault();
        if (f == null) return;
        try
        {
            await using var s = await f.OpenReadAsync();
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms);
            _newImage = ms.ToArray();
            _removeImage = false;
            ShowPreview(_newImage);
            _error.Text = "";
        }
        catch (Exception ex) { _error.Text = "Image illisible : " + ex.Message; }
    }

    private void Save()
    {
        try
        {
            _settings.LockMessage = string.IsNullOrWhiteSpace(_lockText.Text) ? "Écran verrouillé par le professeur" : _lockText.Text.Trim();
            _settings.LessonTitle = string.IsNullOrWhiteSpace(_lessonTitle.Text) ? "Séance" : _lessonTitle.Text.Trim();
            if (_newImage != null) _settings.SetLockImage(_newImage);
            else if (_removeImage) _settings.SetLockImage(null);
            _settings.Save();
            Saved = true;
            Close();
        }
        catch (Exception ex) { _error.Text = "Enregistrement impossible : " + ex.Message; }
    }
}
