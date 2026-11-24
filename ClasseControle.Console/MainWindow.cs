using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ClasseControle.Common;

namespace ClasseControle.Console;

internal sealed class MainWindow : Window
{
    private sealed class Card
    {
        public required AgentConnection Conn;
        public required Border Border;
        public required CheckBox Select;
        public required TextBlock Status;
        public required Button LockBtn;
        public required Image ThumbImg;
        public Avalonia.Media.Imaging.Bitmap? Thumb;
    }

    // Profil des miniatures live (basse résolution pour tenir plusieurs postes).
    private static readonly StartScreenPayload ThumbProfile =
        new() { MaxWidth = 320, Quality = 35, Fps = 2, AllowControl = false };

    private readonly TextBox _roomCode = new() { Text = "CLASSE", Width = 160 };
    private readonly TextBox _teacherName = new() { Text = "Professeur", Width = 180 };
    private readonly WrapPanel _grid = new() { Margin = new Thickness(12) };
    private readonly TextBlock _status = new() { Text = "Prêt.", Margin = new Thickness(12, 6, 12, 6) };
    private readonly Dictionary<string, Card> _cards = new();
    private readonly CheckBox _thumbsToggle = new() { Content = "Miniatures en direct", IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
    private Button _scanBtn = null!;

    public MainWindow()
    {
        Title = "Classe_controle — Console professeur";
        Width = 1100;
        Height = 720;
        MinWidth = 820;
        MinHeight = 520;

        Content = BuildLayout();
    }

    private Control BuildLayout()
    {
        _scanBtn = PrimaryButton("🔍  Rechercher les postes");
        _scanBtn.Click += async (_, _) => await ScanAsync();

        var connBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new TextBlock { Text = "Code de salle :", VerticalAlignment = VerticalAlignment.Center },
                _roomCode,
                new TextBlock { Text = "Professeur :", VerticalAlignment = VerticalAlignment.Center },
                _teacherName,
                _scanBtn,
            },
        };

        var actionBar = new WrapPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 10, 0, 0),
        };
        AddAction(actionBar, "🔒  Tout verrouiller", () => ApplyAsync(c => c.Lock("Écran verrouillé par le professeur"), lockState: true));
        AddAction(actionBar, "🔓  Tout déverrouiller", () => ApplyAsync(c => c.Unlock(), lockState: false));
        AddAction(actionBar, "💬  Message à la classe", SendMessageAsync);
        AddAction(actionBar, "📁  Envoyer un fichier…", SendFileAsync);
        AddAction(actionBar, "🚀  Lancer une app…", LaunchAppAsync);
        AddAction(actionBar, "✖  Fermer une app…", CloseAppAsync);
        AddAction(actionBar, "⏻  Éteindre", () => PowerAsync("éteindre", c => c.Shutdown(10, "Arrêt demandé par le professeur")));
        AddAction(actionBar, "↻  Redémarrer", () => PowerAsync("redémarrer", c => c.Restart(10, "Redémarrage demandé par le professeur")));
        AddAction(actionBar, "🚪  Déconnecter la session", () => PowerAsync("déconnecter la session de", c => c.Logoff()));

        var selectBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            Margin = new Thickness(0, 6, 0, 0),
            Children =
            {
                _thumbsToggle,
                MakeLink("Tout sélectionner", () => SetAllSelected(true)),
                MakeLink("Tout désélectionner", () => SetAllSelected(false)),
            },
        };
        _thumbsToggle.IsCheckedChanged += async (_, _) => await ToggleThumbnails();

        var header = new StackPanel
        {
            Margin = new Thickness(16, 14, 16, 0),
            Children = { connBar, actionBar, selectBar },
        };

        var scroller = new ScrollViewer { Content = _grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Star));
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Grid.SetRow(header, 0);
        Grid.SetRow(scroller, 1);
        var statusBar = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(22, 27, 34)),
            Child = _status,
        };
        Grid.SetRow(statusBar, 2);
        root.Children.Add(header);
        root.Children.Add(scroller);
        root.Children.Add(statusBar);
        return root;
    }

    // ---------- Découverte / connexion ----------
    private async Task ScanAsync()
    {
        _scanBtn.IsEnabled = false;
        SetStatus("Recherche des postes élèves…");
        try
        {
            string code = _roomCode.Text?.Trim() ?? "";
            string teacher = _teacherName.Text?.Trim() ?? "Professeur";
            var replies = await Discovery.ScanAsync(code);

            int nouveaux = 0;
            foreach (var r in replies)
            {
                string key = $"{r.IpAddress}:{r.ControlPort}";
                if (_cards.ContainsKey(key)) continue;

                var conn = new AgentConnection(r);
                var card = CreateCard(conn);
                _cards[key] = card;
                _grid.Children.Add(card.Border);

                var (ok, reason) = await conn.ConnectAsync(code, teacher);
                UpdateCardStatus(card, ok ? "Connecté" : $"Échec : {reason}", ok);
                conn.ConnectionChanged += (c, up) =>
                    Dispatcher.UIThread.Post(async () =>
                    {
                        UpdateCardStatus(card, up ? "Connecté" : "Déconnecté", up);
                        if (up && _thumbsToggle.IsChecked == true)
                            try { await c.AcquireStream("thumb", ThumbProfile); } catch { }
                    });
                if (ok)
                {
                    nouveaux++;
                    if (_thumbsToggle.IsChecked == true)
                        try { await conn.AcquireStream("thumb", ThumbProfile); } catch { }
                }
            }
            SetStatus($"{replies.Count} poste(s) trouvé(s), {_cards.Count(c => c.Value.Conn.IsConnected)} connecté(s).");
        }
        catch (Exception ex) { SetStatus("Erreur de recherche : " + ex.Message); }
        finally { _scanBtn.IsEnabled = true; }
    }

    private Card CreateCard(AgentConnection conn)
    {
        var name = new TextBlock { Text = conn.Info.MachineName, FontWeight = FontWeight.Bold, FontSize = 15 };
        var user = new TextBlock { Text = "👤 " + conn.Info.UserName, Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158)), FontSize = 12 };
        var ip = new TextBlock { Text = conn.Info.IpAddress, Foreground = new SolidColorBrush(Color.FromRgb(110, 118, 129)), FontSize = 11 };
        var status = new TextBlock { Text = "Connexion…", FontSize = 12, Margin = new Thickness(0, 4, 0, 0) };
        var select = new CheckBox { Content = "Sélectionner" };

        var thumb = new Image
        {
            Stretch = Stretch.Uniform,
            Height = 124,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var thumbBorder = new Border
        {
            Height = 124,
            Margin = new Thickness(0, 8, 0, 0),
            Background = new SolidColorBrush(Color.FromRgb(13, 17, 23)),
            CornerRadius = new CornerRadius(6),
            Child = thumb,
        };

        var view = SmallButton("👁 Voir / Contrôler");
        view.Click += (_, _) => { if (conn.IsConnected) new RemoteViewWindow(conn).Show(); };
        var lockBtn = SmallButton("🔒 Verrouiller");
        var card = new Card { Conn = conn, Border = null!, Select = select, Status = status, LockBtn = lockBtn, ThumbImg = thumb };

        // Mise à jour de la miniature à chaque image reçue.
        conn.FrameReceived += f => Dispatcher.UIThread.Post(() =>
        {
            try
            {
                using var ms = new MemoryStream(f.Jpeg);
                var bmp = new Avalonia.Media.Imaging.Bitmap(ms);
                thumb.Source = bmp;
                card.Thumb?.Dispose();
                card.Thumb = bmp;
            }
            catch { }
        });
        lockBtn.Click += async (_, _) =>
        {
            if (!conn.IsConnected) return;
            if (conn.IsLocked) { await conn.Unlock(); conn.IsLocked = false; }
            else { await conn.Lock("Écran verrouillé par le professeur"); conn.IsLocked = true; }
            lockBtn.Content = conn.IsLocked ? "🔓 Déverrouiller" : "🔒 Verrouiller";
        };
        var msgBtn = SmallButton("💬 Message");
        msgBtn.Click += async (_, _) => await MessageOneAsync(conn);

        var buttons = new StackPanel { Spacing = 6, Margin = new Thickness(0, 10, 0, 0),
            Children = { view, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { lockBtn, msgBtn } } } };

        var panel = new StackPanel { Children = { name, user, ip, status, thumbBorder, select, buttons } };
        var border = new Border
        {
            Width = 240,
            Margin = new Thickness(8),
            Padding = new Thickness(14),
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            BorderBrush = new SolidColorBrush(Color.FromRgb(48, 54, 61)),
            Background = new SolidColorBrush(Color.FromRgb(22, 27, 34)),
            Child = panel,
        };
        card.Border = border;
        return card;
    }

    private void UpdateCardStatus(Card card, string text, bool connected)
    {
        card.Status.Text = (connected ? "🟢 " : "🔴 ") + text;
        card.Border.BorderBrush = new SolidColorBrush(connected
            ? Color.FromRgb(46, 160, 67) : Color.FromRgb(90, 50, 50));
    }

    // ---------- Actions ----------
    private IEnumerable<AgentConnection> Targets()
    {
        var selected = _cards.Values.Where(c => c.Select.IsChecked == true && c.Conn.IsConnected).Select(c => c.Conn).ToList();
        if (selected.Count > 0) return selected;
        return _cards.Values.Where(c => c.Conn.IsConnected).Select(c => c.Conn);
    }

    private async Task ApplyAsync(Func<AgentConnection, Task> action, bool? lockState = null)
    {
        var targets = Targets().ToList();
        foreach (var c in targets)
        {
            try { await action(c); if (lockState.HasValue) c.IsLocked = lockState.Value; } catch { }
        }
        if (lockState.HasValue)
            foreach (var card in _cards.Values)
                card.LockBtn.Content = card.Conn.IsLocked ? "🔓 Déverrouiller" : "🔒 Verrouiller";
        SetStatus($"Commande envoyée à {targets.Count} poste(s).");
    }

    private async Task SendMessageAsync()
    {
        var dlg = new ComposeMessageDialog();
        await dlg.ShowDialog(this);
        if (dlg.Result is not { } r || string.IsNullOrWhiteSpace(r.text)) return;
        await ApplyAsync(c => c.ShowMessage(r.title, r.text, 0));
    }

    private async Task MessageOneAsync(AgentConnection conn)
    {
        var dlg = new ComposeMessageDialog();
        await dlg.ShowDialog(this);
        if (dlg.Result is { } r && !string.IsNullOrWhiteSpace(r.text))
            await conn.ShowMessage(r.title, r.text, 0);
    }


    private async Task SendFileAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Choisir un fichier à envoyer", AllowMultiple = false,
        });
        var file = files.FirstOrDefault();
        if (file == null) return;
        byte[] content;
        try
        {
            await using var s = await file.OpenReadAsync();
            using var ms = new MemoryStream();
            await s.CopyToAsync(ms);
            content = ms.ToArray();
        }
        catch (Exception ex) { SetStatus("Lecture du fichier impossible : " + ex.Message); return; }

        string name = file.Name;
        var targets = Targets().ToList();
        foreach (var c in targets)
        {
            try { await c.PushFile(name, content, open: false); } catch { }
        }
        SetStatus($"Fichier « {name} » envoyé à {targets.Count} poste(s).");
    }

    private async Task LaunchAppAsync()
    {
        var dlg = new InputDialog("Lancer une application", "Chemin de l'application ou URL à ouvrir :",
            "ex : notepad  ou  https://ofppt.ma", "notepad");
        await dlg.ShowDialog(this);
        if (string.IsNullOrWhiteSpace(dlg.Result)) return;
        await ApplyAsync(c => c.LaunchApp(dlg.Result!.Trim(), ""));
    }

    private async Task CloseAppAsync()
    {
        var dlg = new InputDialog("Fermer une application", "Nom du processus à fermer (sans .exe) :",
            "ex : chrome", "");
        await dlg.ShowDialog(this);
        if (string.IsNullOrWhiteSpace(dlg.Result)) return;
        await ApplyAsync(c => c.CloseApp(dlg.Result!.Trim()));
    }

    private async Task PowerAsync(string verbe, Func<AgentConnection, Task> action)
    {
        var targets = Targets().ToList();
        if (targets.Count == 0) { SetStatus("Aucun poste connecté."); return; }

        var confirm = new ConfirmDialog("Confirmation",
            $"Voulez-vous vraiment {verbe} {targets.Count} poste(s) ?");
        await confirm.ShowDialog(this);
        if (!confirm.Confirmed) return;

        foreach (var c in targets) { try { await action(c); } catch { } }
        SetStatus($"Commande « {verbe} » envoyée à {targets.Count} poste(s).");
    }

    private void SetAllSelected(bool value)
    {
        foreach (var card in _cards.Values) card.Select.IsChecked = value;
    }

    private async Task ToggleThumbnails()
    {
        bool on = _thumbsToggle.IsChecked == true;
        foreach (var card in _cards.Values.Where(c => c.Conn.IsConnected))
        {
            try
            {
                if (on) await card.Conn.AcquireStream("thumb", ThumbProfile);
                else await card.Conn.ReleaseStream("thumb");
            }
            catch { }
        }
        SetStatus(on ? "Miniatures activées." : "Miniatures désactivées.");
    }

    // ---------- Helpers UI ----------
    private Button MakeLink(string text, Action onClick)
    {
        var b = new Button
        {
            Content = text,
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(88, 166, 255)),
            Padding = new Thickness(4, 2, 4, 2),
            VerticalAlignment = VerticalAlignment.Center,
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    private void AddAction(Panel bar, string text, Func<Task> handler)
    {
        var b = SmallButton(text);
        b.Margin = new Thickness(0, 0, 8, 8);
        b.Click += async (_, _) => { try { await handler(); } catch (Exception ex) { SetStatus("Erreur : " + ex.Message); } };
        bar.Children.Add(b);
    }

    private static Button PrimaryButton(string text) => new()
    {
        Content = text,
        Padding = new Thickness(16, 8, 16, 8),
        Background = new SolidColorBrush(Color.FromRgb(37, 99, 235)),
        Foreground = Brushes.White,
        FontWeight = FontWeight.SemiBold,
    };

    private static Button SmallButton(string text) => new()
    {
        Content = text,
        Padding = new Thickness(10, 6, 10, 6),
    };

    private void SetStatus(string text) => Dispatcher.UIThread.Post(() => _status.Text = text);
}
