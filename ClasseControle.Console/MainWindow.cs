using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ClasseControle.Common;

namespace ClasseControle.Console;

/// <summary>
/// Console du professeur : ruban de commandes par onglets, barre latérale de
/// navigation, mur de vignettes des postes élèves avec menu contextuel.
/// Règle de ciblage : les commandes du ruban s'appliquent aux postes
/// sélectionnés, ou à toute la classe si aucun n'est sélectionné.
/// </summary>
internal sealed class MainWindow : Window
{
    // Zoom des vignettes : 100 % = 290 px de large.
    private const double ZoomBase = 290, ZoomMin = 150, ZoomMax = 700;

    private readonly ConsoleSettings _settings = ConsoleSettings.Load();
    private readonly Dictionary<string, StudentTile> _tiles = new();
    private readonly WrapPanel _grid = new() { Margin = new Thickness(14) };
    private readonly TextBlock _status = new() { Text = "Prêt.", Margin = new Thickness(12, 4), FontSize = 12, Foreground = Palette.Muted };
    private readonly TextBlock _banner = new() { Foreground = Brushes.White, FontSize = 14, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBox _roomCode = new() { Width = 130, FontSize = 12, Watermark = "Code de salle", MinHeight = 26, Height = 28, Padding = new Thickness(6, 4), Margin = new Thickness(0, 2) };
    private readonly TextBox _teacherName = new() { Width = 130, FontSize = 12, Watermark = "Professeur", MinHeight = 26, Height = 28, Padding = new Thickness(6, 4), Margin = new Thickness(0, 2) };
    private readonly Border _emptyHint;
    private readonly ScreenBroadcaster _broadcaster = new();
    private readonly Announcer _announcer = new();
    private readonly GroupManager _groupMgr;
    private readonly List<ConsoleChatWindow> _chats = new();
    private readonly TabControl _tabs = new();
    private RestrictionsPayload _restrictions = new();
    private SessionStartPayload? _lesson;
    private bool _thumbsLive = true;

    private Button _scanBtn = null!, _lessonBtn = null!, _showBtn = null!, _announceBtn = null!,
                   _webBtn = null!, _helpBtn = null!, _liveBtn = null!, _sideAnnounce = null!,
                   _netBtn = null!, _sideNet = null!, _muteBtn = null!, _sideApps = null!, _inputBtn = null!, _sideInput = null!;
    private bool _muteForClass, _inputLockForClass;
    private AppsPanel _appsPanel = null!;
    private Grid _split = null!;
    private bool _appsVisible;
    private const string DragFormat = "classecontrole-tile";
    /// <summary>Coupure d'Internet décidée pour toute la classe (réappliquée aux postes qui se reconnectent).</summary>
    private bool _internetCutForClass;
    private readonly Dictionary<int, Button> _sideTabs = new();
    private readonly Slider _zoom = new() { Minimum = ZoomMin, Maximum = ZoomMax, Width = 150, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, -6, 4, -6) };
    private readonly TextBlock _zoomText = new() { Width = 44, FontSize = 12, VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right, Foreground = Palette.Muted };
    private ScrollViewer _scroller = null!;
    private DispatcherTimer? _zoomSettle;

    private double TileWidth => Math.Clamp(_settings.TileWidth, ZoomMin, ZoomMax);

    // Résolution des miniatures adaptée à la taille affichée (net sans gaspiller le réseau).
    private StartScreenPayload ThumbProfile => new()
    {
        MaxWidth = (int)Math.Clamp(TileWidth * 1.25, 240, 900),
        Quality = TileWidth > 450 ? 50 : 40,
        Fps = Math.Clamp(_settings.ThumbFps, 1, 5),
    };

    public MainWindow()
    {
        Title = "Classe_controle — Console professeur";
        Width = 1280;
        Height = 800;
        MinWidth = 900;
        MinHeight = 560;
        Background = Brushes.White;

        _groupMgr = new GroupManager(() => _tiles.Values.Select(t => t.Conn));
        _roomCode.Text = _settings.RoomCode;
        _teacherName.Text = _settings.TeacherName;
        _restrictions.AllowedSites = _settings.AllowedSites.ToList();

        _emptyHint = new Border
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    Glyph.Make(Glyph.Monitor, 56, Palette.Muted),
                    new TextBlock { Text = "Aucun poste pour l'instant", FontSize = 18, FontWeight = FontWeight.SemiBold, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = "Saisissez le code de salle puis cliquez sur « Rechercher » dans le ruban.", Foreground = Palette.Muted, HorizontalAlignment = HorizontalAlignment.Center },
                },
            },
        };

        Content = BuildLayout();
        UpdateBanner();

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.A && e.KeyModifiers.HasFlag(KeyModifiers.Control) && FocusManager?.GetFocusedElement() is not TextBox)
            { SelectWhere(_ => true); e.Handled = true; }
            if (e.Key == Key.Escape) SelectWhere(_ => false);
        };
        Closing += async (_, _) =>
        {
            SaveIdentity();
            if (_broadcaster.IsRunning) await _broadcaster.StopAsync();
            if (_announcer.IsLive) await _announcer.StopAsync();
        };
        Opened += async (_, _) => { if (!string.IsNullOrWhiteSpace(_roomCode.Text)) await ScanAsync(); };
    }

    // =====================================================================
    //  Mise en page
    // =====================================================================
    private Control BuildLayout()
    {
        BuildTabs();

        var bannerPill = new Border
        {
            Background = Palette.Navy,
            CornerRadius = new CornerRadius(0, 0, 10, 10),
            Padding = new Thickness(18, 8),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 0, 16, 0),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 10,
                Children = { _banner, Glyph.Make(Glyph.People, 18, Brushes.White) },
            },
        };
        var ribbon = new Border
        {
            Background = Palette.RibbonBg,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new Grid { Children = { _tabs, bannerPill } },
        };

        var sidebar = BuildSidebar();

        var scroller = _scroller = new ScrollViewer { Content = _grid, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var workspace = new Border
        {
            Background = Palette.Workspace,
            Child = new Grid { Children = { _emptyHint, scroller } },
        };
        // Ctrl + molette : zoom des vignettes.
        workspace.AddHandler(PointerWheelChangedEvent, (_, e) =>
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;
            SetZoom(TileWidth * (e.Delta.Y > 0 ? 1.1 : 1 / 1.1));
            e.Handled = true;
        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        // Clic dans le vide : désélectionne tout (les commandes reviennent à toute la classe).
        workspace.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(workspace).Properties.IsLeftButtonPressed) SelectWhere(_ => false);
        };

        // Zone principale partagée : vignettes en haut, applications ouvertes en bas (masquable).
        _appsPanel = new AppsPanel(
            () => _tiles.Values.Select(t => t.Conn).ToList(),
            async (c, proc) => await ApplyAsync(new[] { c }, "Fermeture d'application", x => x.CloseApp(proc), proc),
            c => ViewTargets(new[] { c })) { IsVisible = false };
        _appsPanel.CloseRequested += () => Run(() => ShowAppsAsync(false));
        var splitter = new GridSplitter
        {
            Height = 6, Background = Palette.Line, IsVisible = false,
            HorizontalAlignment = HorizontalAlignment.Stretch, ResizeDirection = GridResizeDirection.Rows,
        };
        _split = new Grid { RowDefinitions = new RowDefinitions("*,0,0") };
        _split.Children.Add(workspace);
        Grid.SetRow(splitter, 1);
        _split.Children.Add(splitter);
        Grid.SetRow(_appsPanel, 2);
        _split.Children.Add(_appsPanel);

        // Glisser-déposer : lâcher une vignette dans le vide la place à la fin.
        DragDrop.SetAllowDrop(_grid, true);
        _grid.AddHandler(DragDrop.DragOverEvent, (_, e) => e.DragEffects = e.Data.Contains(DragFormat) ? DragDropEffects.Move : DragDropEffects.None);
        _grid.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            if (e.Handled || e.Data.Get(DragFormat) is not string key || !_tiles.TryGetValue(key, out var src)) return;
            _grid.Children.Remove(src.Root);
            _grid.Children.Add(src.Root);
            SaveTileOrder();
        });

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        body.Children.Add(sidebar);
        Grid.SetColumn(_split, 1);
        body.Children.Add(_split);

        var statusBar = new Border
        {
            Background = Palette.TabsBg,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = BuildStatusBar(),
        };

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        root.Children.Add(ribbon);
        Grid.SetRow(body, 1);
        root.Children.Add(body);
        Grid.SetRow(statusBar, 2);
        root.Children.Add(statusBar);
        return root;
    }

    private void AddTab(string header, Control content)
    {
        _tabs.Items.Add(new TabItem
        {
            Header = new TextBlock { Text = header, FontSize = 13 },
            Content = content,
            Padding = new Thickness(12, 4),
            MinHeight = 30,
        });
    }

    private void BuildTabs()
    {
        _tabs.Padding = new Thickness(6, 0);
        _tabs.Background = Brushes.Transparent;

        // ---------------- Classe ----------------
        _scanBtn = Ribbon.Big(Glyph.Search, "Rechercher", () => Run(ScanAsync));
        var room = Ribbon.Group("Salle",
            Ribbon.Column(_roomCode, _teacherName),
            _scanBtn);

        _lessonBtn = Ribbon.Big(Glyph.Education, "Démarrer la séance", () => Run(ToggleLessonAsync));
        var lesson = Ribbon.Group("Séance",
            _lessonBtn,
            Ribbon.Big(Glyph.List, "Présence", OpenAttendance));

        _showBtn = Ribbon.Big(Glyph.Projector, "Montrer", menu: Ribbon.Menu(
            ("Montrer mon écran…", () => Run(ShowMyScreenAsync)),
            ("Présenter l'écran de l'élève sélectionné…", () => Run(ExhibitSelectedAsync)),
            ("-", () => { }),
            ("Arrêter la diffusion", () => Run(StopBroadcastAsync))));
        var student = Ribbon.Group("Élève",
            Ribbon.Big(Glyph.View, "Voir l'élève", () => ViewTargets(Targets())),
            Ribbon.Big(Glyph.Control, "Contrôler", () => ViewTargets(Targets(), control: true)),
            Ribbon.Big(Glyph.Folder, "Transfert de fichiers", () => Run(() => SendFileAsync(Targets()))),
            Ribbon.Big(Glyph.Lock, "Verrouiller", () => Run(() => LockAsync(Targets()))),
            Ribbon.Big(Glyph.Unlock, "Déverrouiller", () => Run(() => UnlockAsync(Targets()))),
            Ribbon.Big(Glyph.Blank, "Écran noir", () => Run(() => BlankAsync(Targets()))),
            (_inputBtn = Ribbon.Big(Glyph.InputLock, "Bloquer clavier/souris", () => Run(() => ToggleInputLockAsync(Targets())))),
            _showBtn,
            Ribbon.Big(Glyph.Launch, "Lancement rapide", menu: QuickLaunchMenu(() => Targets())),
            Ribbon.Big(Glyph.Desktop, "Bureau élève", menu: Ribbon.Menu(
                ("Réduire toutes les fenêtres", () => Run(() => DesktopAsync(Targets(), DesktopAction.MinimizeAll))),
                ("Fermer toutes les applications…", () => Run(() => DesktopAsync(Targets(), DesktopAction.CloseAllApps))))));

        _webBtn = Ribbon.Big(Glyph.Globe, "Sans restriction", menu: Ribbon.Menu(
            ("Sans restriction", () => Run(() => SetWebModeAsync(WebMode.Unrestricted))),
            ("Sites interdits (liste)", () => Run(() => SetWebModeAsync(WebMode.BlockList))),
            ("Sites approuvés uniquement", () => Run(() => SetWebModeAsync(WebMode.AllowList))),
            ("Bloquer tout le web", () => Run(() => SetWebModeAsync(WebMode.BlockAll))),
            ("-", () => { }),
            ("Modifier les listes…", () => Run(EditRestrictionsAsync)),
            ("Ouvrir un site sur les postes…", () => Run(OpenWebsiteAsync))));
        _netBtn = Ribbon.Big(Glyph.WebOff, "Couper Internet", () => Run(() => ToggleInternetAsync(Targets())));
        var web = Ribbon.Group("Accès web", _webBtn, _netBtn);
        _muteBtn = Ribbon.Big(Glyph.SoundOff, "Couper le son", () => Run(() => ToggleMuteAsync(Targets())));
        var sound = Ribbon.Group("Son", _muteBtn);

        _announceBtn = Ribbon.Big(Glyph.Mic, "Annonce", () => Run(ToggleAnnounceAsync));
        _helpBtn = Ribbon.Big(Glyph.Help, "Demandes d'aide", menu: HelpMenu());
        var communicate = Ribbon.Group("Communiquer",
            Ribbon.Big(Glyph.Chat, "Discussion", () => OpenChat(Targets())),
            _announceBtn,
            Ribbon.Big(Glyph.Message, "Message", menu: MessageMenu(() => Targets())),
            _helpBtn);

        _liveBtn = Ribbon.Big(Glyph.View, "En direct", ToggleLive);
        SetState(_liveBtn, "on");
        var thumbs = Ribbon.Group("Miniatures",
            Ribbon.Big(Glyph.Refresh, "Actualisation", menu: Ribbon.Menu(
                ("1 image / seconde", () => SetFps(1)),
                ("2 images / seconde", () => SetFps(2)),
                ("4 images / seconde", () => SetFps(4)))),
            Ribbon.Column(
                Ribbon.Small(Glyph.ZoomIn, "Zoom avant", () => SetZoom(TileWidth * 1.15)),
                Ribbon.Small(Glyph.ZoomOut, "Zoom arrière", () => SetZoom(TileWidth / 1.15)),
                Ribbon.Small(Glyph.Fit, "Ajuster à la fenêtre", FitToWindow)),
            _liveBtn);

        AddTab("Classe", Ribbon.TabContent(room, lesson, student, web, sound, communicate, thumbs));

        // ---------------- Groupes ----------------
        AddTab("Groupes", Ribbon.TabContent(
            Ribbon.Group("Équipes",
                Ribbon.Big(Glyph.People, "Équipes…", OpenGroups),
                Ribbon.Big(Glyph.Shuffle, "Répartir", OpenGroups)),
            Ribbon.Group("Espaces d'équipe",
                Ribbon.Big(Glyph.Play, "Ouvrir", () => Run(async () => { await _groupMgr.OpenAsync(); SetStatus($"{_groupMgr.Groups.Count} espace(s) d'équipe ouvert(s)."); })),
                Ribbon.Big(Glyph.Stop, "Fermer", () => Run(async () => { await _groupMgr.CloseAsync(); SetStatus("Espaces d'équipe fermés."); })),
                Ribbon.Big(Glyph.Save, "Exporter les travaux", () =>
                {
                    try { Ui.OpenFolder(_groupMgr.Export()); } catch (Exception ex) { SetStatus("Export impossible : " + ex.Message); }
                }))));

        // ---------------- Sélection ----------------
        AddTab("Sélection", Ribbon.TabContent(
            Ribbon.Group("Sélectionner",
                Ribbon.Big(Glyph.SelectAll, "Tout", () => SelectWhere(_ => true)),
                Ribbon.Big(Glyph.ClearSel, "Aucun", () => SelectWhere(_ => false)),
                Ribbon.Big(Glyph.Switch, "Inverser", () => SelectWhere(t => !t.IsSelected))),
            Ribbon.Group("Par état",
                Ribbon.Big(Glyph.Hand, "Mains levées", () => SelectWhere(t => t.Conn.IsHandRaised)),
                Ribbon.Big(Glyph.Contact, "Non identifiés", () => SelectWhere(t => t.Conn.IsConnected && t.Conn.StudentName.Length == 0)),
                Ribbon.Big(Glyph.Lock, "Verrouillés", () => SelectWhere(t => t.Conn.IsLocked || t.Conn.IsBlanked)))));

        // ---------------- Retour & bien-être ----------------
        AddTab("Retour & bien-être", Ribbon.TabContent(
            Ribbon.Group("Sondages",
                Ribbon.Big(Glyph.Survey, "Sondage rapide", OpenSurvey)),
            Ribbon.Group("Récompenses",
                Ribbon.Big(Glyph.StarFill, "Donner une étoile", () => Run(() => RewardAsync(Targets(), +1))),
                Ribbon.Big(Glyph.Star, "Retirer une étoile", () => Run(() => RewardAsync(Targets(), -1))),
                Ribbon.Big(Glyph.Refresh, "Tout remettre à zéro", () => Run(() => RewardAsync(Targets(), 0))))));

        // ---------------- Journal ----------------
        AddTab("Journal", Ribbon.TabContent(
            Ribbon.Group("Suivi",
                Ribbon.Big(Glyph.History, "Journal des actions", () => new JournalWindow().Show(this)),
                Ribbon.Big(Glyph.List, "Présence", OpenAttendance),
                Ribbon.Big(Glyph.Folder, "Ouvrir le dossier", () => Ui.OpenFolder(ActionLog.Folder)))));

        // ---------------- Administrer ----------------
        AddTab("Administrer", Ribbon.TabContent(
            Ribbon.Group("Applications",
                Ribbon.Big(Glyph.Play, "Lancer une application…", () => Run(() => LaunchAppAsync(Targets()))),
                Ribbon.Big(Glyph.Cancel, "Fermer une application…", () => Run(CloseAppAsync)),
                Ribbon.Big(Glyph.Blocked, "Restrictions…", () => Run(EditRestrictionsAsync))),
            Ribbon.Group("Alimentation",
                Ribbon.Big(Glyph.Power, "Éteindre", () => Run(() => PowerAsync(Targets(), "éteindre", c => c.Shutdown(10, "Arrêt demandé par le professeur")))),
                Ribbon.Big(Glyph.Refresh, "Redémarrer", () => Run(() => PowerAsync(Targets(), "redémarrer", c => c.Restart(10, "Redémarrage demandé par le professeur")))),
                Ribbon.Big(Glyph.SignOut, "Fermer la session", () => Run(() => PowerAsync(Targets(), "fermer la session de", c => c.Logoff())))),
            Ribbon.Group("Console",
                Ribbon.Big(Glyph.Settings, "Paramètres", () => Run(OpenSettingsAsync)))));

        // ---------------- Affichage ----------------
        AddTab("Affichage", Ribbon.TabContent(
            Ribbon.Group("Vignettes",
                Ribbon.Big(Glyph.ZoomIn, "Zoom avant", () => SetZoom(TileWidth * 1.15)),
                Ribbon.Big(Glyph.ZoomOut, "Zoom arrière", () => SetZoom(TileWidth / 1.15)),
                Ribbon.Big(Glyph.Fit, "Ajuster à la fenêtre", FitToWindow),
                Ribbon.Big(Glyph.Refresh, "Taille normale", () => SetZoom(ZoomBase)),
                Ribbon.Big(Glyph.Sort, "Trier par nom", SortTiles))));
    }

    private Control BuildSidebar()
    {
        // Bouton de la barre latérale : icône + indicateur vertical (vue active) ; info-bulle à droite.
        Button Item(string glyph, string tip, Action a, int tab = -1)
        {
            var bar = new Border
            {
                Width = 3, CornerRadius = new CornerRadius(2), Background = Palette.Accent,
                HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(1, 9, 0, 9),
            };
            bar.Classes.Add("bar");
            var b = new Button
            {
                Content = new Grid { Width = 44, Height = 40, Children = { bar, Glyph.Make(glyph, 22) } },
                Padding = new Thickness(0),
                Margin = new Thickness(6, 2),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch,
            };
            b.Classes.Add("side");
            ToolTip.SetTip(b, tip);
            ToolTip.SetPlacement(b, PlacementMode.Right);
            b.Click += (_, _) => a();
            if (tab >= 0) _sideTabs[tab] = b;
            return b;
        }

        _sideAnnounce = Item(Glyph.Mic, "Annonce audio (micro)", () => Run(ToggleAnnounceAsync));
        var top = new StackPanel
        {
            Margin = new Thickness(0, 8),
            Spacing = 2,
            Children =
            {
                Item(Glyph.Grid, "Classe — vignettes des postes", () => _tabs.SelectedIndex = 0, tab: 0),
                Item(Glyph.People, "Groupes et équipes", () => _tabs.SelectedIndex = 1, tab: 1),
                Item(Glyph.SelectAll, "Sélection", () => _tabs.SelectedIndex = 2, tab: 2),
                Item(Glyph.Survey, "Retour & bien-être (sondages, étoiles)", () => _tabs.SelectedIndex = 3, tab: 3),
                Item(Glyph.History, "Journal", () => _tabs.SelectedIndex = 4, tab: 4),
                Item(Glyph.Admin, "Administrer", () => _tabs.SelectedIndex = 5, tab: 5),
                new Border { Height = 1, Background = Palette.Line, Margin = new Thickness(12, 8) },
                Item(Glyph.Projector, "Montrer mon écran", () => Run(ShowMyScreenAsync)),
                _sideAnnounce,
                Item(Glyph.Globe, "Accès web et restrictions", () => Run(EditRestrictionsAsync)),
                (_sideNet = Item(Glyph.WebOff, "Couper / rétablir Internet", () => Run(() => ToggleInternetAsync(Targets())))),
                Item(Glyph.SoundOff, "Couper / rétablir le son", () => Run(() => ToggleMuteAsync(Targets()))),
                (_sideInput = Item(Glyph.InputLock, "Bloquer / débloquer clavier et souris", () => Run(() => ToggleInputLockAsync(Targets())))),
                (_sideApps = Item(Glyph.Apps, "Applications ouvertes sur les postes (partage l'écran)", () => Run(() => ShowAppsAsync(!_appsVisible)))),
                Item(Glyph.Launch, "Ouvrir un site sur les postes", () => Run(OpenWebsiteAsync)),
                Item(Glyph.List, "Liste de présence", OpenAttendance),
            },
        };
        var bottom = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 8),
            Children = { Item(Glyph.Settings, "Paramètres (écran de verrouillage, séance)", () => Run(OpenSettingsAsync)) },
        };
        _tabs.SelectionChanged += (_, _) => SyncSidebar();
        SyncSidebar();
        return new Border
        {
            Background = Brushes.White,
            BorderBrush = Palette.Line,
            BorderThickness = new Thickness(0, 0, 1, 0),
            Child = new Grid { Children = { top, bottom } },
        };
    }

    private void SyncSidebar()
    {
        foreach (var (tab, b) in _sideTabs) b.Classes.Set("on", tab == _tabs.SelectedIndex);
    }

    /// <summary>Barre d'état : messages à gauche, zoom des vignettes à droite (− curseur + % ajuster).</summary>
    private Control BuildStatusBar()
    {
        Button ZoomBtn(string glyph, string tip, Action a)
        {
            var b = new Button { Content = Glyph.Make(glyph, 16), Padding = new Thickness(5), Width = 28, Height = 28 };
            b.Classes.Add("zoom");
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => a();
            return b;
        }
        _zoom.Value = TileWidth;
        _zoom.PropertyChanged += (_, e) =>
        {
            if (e.Property == RangeBase.ValueProperty) SetZoom(_zoom.Value, fromSlider: true);
        };
        UpdateZoomText();
        var zoomBar = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            Margin = new Thickness(0, 0, 10, 0),
            Children =
            {
                ZoomBtn(Glyph.Minus, "Zoom arrière (Ctrl + molette)", () => SetZoom(TileWidth / 1.15)),
                _zoom,
                ZoomBtn(Glyph.Plus, "Zoom avant (Ctrl + molette)", () => SetZoom(TileWidth * 1.15)),
                _zoomText,
                new Border { Width = 1, Background = Palette.Line, Margin = new Thickness(8, 6) },
                ZoomBtn(Glyph.Fit, "Ajuster : tous les postes visibles sans défiler", FitToWindow),
                ZoomBtn(Glyph.Refresh, "Taille normale (100 %)", () => SetZoom(ZoomBase)),
            },
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 34 };
        _status.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(_status);
        Grid.SetColumn(zoomBar, 1);
        grid.Children.Add(zoomBar);
        return grid;
    }

    private MenuFlyout QuickLaunchMenu(Func<IReadOnlyList<AgentConnection>> targets)
    {
        var f = new MenuFlyout();
        f.Opening += (_, _) =>
        {
            f.Items.Clear();
            foreach (var q in _settings.QuickLaunch)
            {
                var mi = new MenuItem { Header = q.Name };
                var item = q;
                mi.Click += (_, _) => Run(() => ApplyAsync(targets(), "Lancement rapide", c => c.LaunchApp(item.Target, ""), item.Name));
                f.Items.Add(mi);
            }
            f.Items.Add(new Separator());
            AddMenu(f, "Autre application…", () => Run(() => LaunchAppAsync(targets())));
            AddMenu(f, "Ouvrir un site web…", () => Run(OpenWebsiteAsync));
            AddMenu(f, "Gérer les favoris…", () => Run(EditQuickLaunchAsync));
        };
        f.Items.Add(new MenuItem { Header = "…" }); // contenu reconstruit à l'ouverture
        return f;
    }

    private MenuFlyout MessageMenu(Func<IReadOnlyList<AgentConnection>> targets)
    {
        string[] presets =
        {
            "Regardez le tableau, s'il vous plaît.",
            "Il reste 5 minutes.",
            "Enregistrez votre travail maintenant.",
            "Fermez les applications et rangez votre poste.",
        };
        var f = new MenuFlyout();
        AddMenu(f, "Nouveau message…", () => Run(() => SendMessageAsync(targets())));
        f.Items.Add(new Separator());
        foreach (var p in presets)
        {
            string text = p;
            AddMenu(f, text, () => Run(() => ApplyAsync(targets(), "Message", c => c.ShowMessage("Message du professeur", text, 15), text)));
        }
        return f;
    }

    private MenuFlyout HelpMenu()
    {
        var f = new MenuFlyout();
        f.Opening += (_, _) =>
        {
            f.Items.Clear();
            var hands = _tiles.Values.Where(t => t.Conn.IsHandRaised).ToList();
            if (hands.Count == 0) { f.Items.Add(new MenuItem { Header = "Aucune demande d'aide", IsEnabled = false }); return; }
            foreach (var t in hands)
            {
                var tile = t;
                AddMenu(f, $"✋ {t.Conn.DisplayName} ({t.Conn.Info.MachineName}) — voir l'écran", () =>
                {
                    ViewTargets(new[] { tile.Conn });
                    Run(() => LowerHandAsync(new[] { tile.Conn }));
                });
            }
            f.Items.Add(new Separator());
            AddMenu(f, "Baisser toutes les mains", () => Run(() => LowerHandAsync(hands.Select(h => h.Conn).ToList())));
        };
        f.Items.Add(new MenuItem { Header = "…" });
        return f;
    }

    private static void AddMenu(MenuFlyout f, string header, Action a)
    {
        var mi = new MenuItem { Header = header };
        mi.Click += (_, _) => a();
        f.Items.Add(mi);
    }

    /// <summary>Menu contextuel d'une vignette (clic droit), façon palette d'actions.</summary>
    private void ShowTileMenu(StudentTile tile, Control anchor)
    {
        // Clic droit sur une vignette sélectionnée → toute la sélection ; sinon ce poste seul.
        IReadOnlyList<AgentConnection> targets = tile.IsSelected
            ? _tiles.Values.Where(t => t.IsSelected && t.Conn.IsConnected).Select(t => t.Conn).ToList()
            : new[] { tile.Conn };
        var flyout = new Flyout { Placement = PlacementMode.Pointer };

        Button Cmd(string glyph, string label, Action a, bool big = true)
        {
            var b = big ? Ribbon.Big(glyph, label) : Ribbon.Small(glyph, label, () => { });
            b.Click += (_, _) => { flyout.Hide(); a(); };
            return b;
        }

        bool locked = tile.Conn.IsLocked || tile.Conn.IsBlanked;
        var top = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                locked ? Cmd(Glyph.Unlock, "Déverrouiller", () => Run(() => UnlockAsync(targets)), false)
                       : Cmd(Glyph.Lock, "Verrouiller", () => Run(() => LockAsync(targets)), false),
                Cmd(Glyph.Blank, "Écran noir", () => Run(() => BlankAsync(targets)), false),
                tile.Conn.IsInputLocked
                    ? Cmd(Glyph.InputUnlock, "Débloquer clavier/souris", () => Run(() => SetInputLockAsync(targets, false)), false)
                    : Cmd(Glyph.InputLock, "Bloquer clavier/souris", () => Run(() => SetInputLockAsync(targets, true)), false),
                tile.Conn.IsInternetBlocked
                    ? Cmd(Glyph.Globe, "Rétablir Internet", () => Run(() => SetInternetAsync(targets, false)), false)
                    : Cmd(Glyph.WebOff, "Couper Internet", () => Run(() => SetInternetAsync(targets, true)), false),
                tile.Conn.IsMuted
                    ? Cmd(Glyph.Sound, "Rétablir le son", () => Run(() => SetMuteAsync(targets, false)), false)
                    : Cmd(Glyph.SoundOff, "Couper le son", () => Run(() => SetMuteAsync(targets, true)), false),
            },
        };
        var main = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                Cmd(Glyph.View, "Voir l'élève", () => ViewTargets(targets)),
                Cmd(Glyph.Control, "Contrôler", () => ViewTargets(targets, control: true)),
                Cmd(Glyph.Folder, "Transfert de fichiers", () => Run(() => SendFileAsync(targets))),
                Cmd(Glyph.Chat, "Discussion", () => OpenChat(targets)),
                Cmd(Glyph.Message, "Message", () => Run(() => SendMessageAsync(targets))),
                Cmd(Glyph.Launch, "Lancer…", () => Run(() => LaunchAppAsync(targets))),
                Cmd(Glyph.Exhibit, "Présenter", () => Run(() => RelayStudentAsync(tile.Conn))),
            },
        };
        var rewards = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                Cmd(Glyph.StarFill, "Donner une étoile", () => Run(() => RewardAsync(targets, +1)), false),
                Cmd(Glyph.Star, "Retirer", () => Run(() => RewardAsync(targets, -1)), false),
                Cmd(Glyph.Refresh, "Tout retirer", () => Run(() => RewardAsync(targets, 0)), false),
            },
        };
        var manage = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children =
            {
                new TextBlock { Text = "Gérer", FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 8, 0) },
                IconCmd(Glyph.Hand, "Baisser la main", () => Run(() => LowerHandAsync(targets))),
                IconCmd(Glyph.Power, "Éteindre", () => Run(() => PowerAsync(targets, "éteindre", c => c.Shutdown(10, "Arrêt demandé par le professeur")))),
                IconCmd(Glyph.Refresh, "Redémarrer", () => Run(() => PowerAsync(targets, "redémarrer", c => c.Restart(10, "Redémarrage demandé par le professeur")))),
                IconCmd(Glyph.SignOut, "Fermer la session", () => Run(() => PowerAsync(targets, "fermer la session de", c => c.Logoff()))),
                IconCmd(Glyph.Desktop, "Réduire les fenêtres", () => Run(() => DesktopAsync(targets, DesktopAction.MinimizeAll))),
                IconCmd(Glyph.Delete, "Retirer de la liste", () => RemoveTiles(targets)),
            },
        };

        Button IconCmd(string glyph, string tip, Action a)
        {
            var b = new Button { Content = Glyph.Make(glyph, 18), Padding = new Thickness(6) };
            b.Classes.Add("zoom");
            ToolTip.SetTip(b, tip);
            b.Click += (_, _) => { flyout.Hide(); a(); };
            return b;
        }

        Border Sep() => new() { Height = 1, Background = Palette.Line, Margin = new Thickness(0, 4) };
        string who = targets.Count == 1 ? $"{targets[0].DisplayName} — {targets[0].Info.MachineName}" : $"{targets.Count} postes sélectionnés";
        flyout.Content = new StackPanel
        {
            Width = 700,
            Children =
            {
                new TextBlock { Text = who, FontWeight = FontWeight.SemiBold, Margin = new Thickness(6, 0, 0, 4) },
                top, Sep(), main, Sep(), rewards, Sep(), manage,
            },
        };
        flyout.ShowAt(anchor, showAtPointer: true);
    }

    // =====================================================================
    //  Découverte / connexion
    // =====================================================================
    private async Task ScanAsync()
    {
        _scanBtn.IsEnabled = false;
        SetStatus("Recherche des postes élèves…");
        try
        {
            string code = _roomCode.Text?.Trim() ?? "";
            string teacher = _teacherName.Text?.Trim() ?? "Professeur";
            SaveIdentity();
            var replies = await Discovery.ScanAsync(code);

            int nouveaux = 0;
            foreach (var r in replies)
            {
                string key = $"{r.IpAddress}:{r.ControlPort}";
                if (_tiles.TryGetValue(key, out var existing))
                {
                    // Poste déjà connu mais déconnecté : on retente la connexion.
                    if (!existing.Conn.IsConnected) await ConnectTileAsync(existing, code, teacher);
                    continue;
                }

                var conn = new AgentConnection(r);
                var tile = new StudentTile(conn, TileWidth) { LessonTitle = _lesson?.Title ?? "" };
                _tiles[key] = tile;
                _grid.Children.Insert(OrderedIndex(r.MachineName), tile.Root);
                WireTile(tile);
                if (await ConnectTileAsync(tile, code, teacher)) nouveaux++;
            }
            UpdateBanner();
            SetStatus($"{replies.Count} poste(s) trouvé(s), {_tiles.Values.Count(t => t.Conn.IsConnected)} connecté(s).");
            if (nouveaux > 0) ActionLog.Add("Recherche des postes", $"{nouveaux} nouveau(x) poste(s)", $"salle {code}");
        }
        catch (Exception ex) { SetStatus("Erreur de recherche : " + ex.Message); }
        finally { _scanBtn.IsEnabled = true; }
    }

    private async Task<bool> ConnectTileAsync(StudentTile tile, string code, string teacher)
    {
        var (ok, reason) = await tile.Conn.ConnectAsync(code, teacher);
        tile.SetStatus(ok ? "Connecté" : $"Échec : {reason}");
        if (ok) await OnConnectedAsync(tile.Conn);
        return ok;
    }

    private void WireTile(StudentTile tile)
    {
        var conn = tile.Conn;
        tile.Clicked += OnTileClicked;
        tile.DoubleClicked += t => ViewTargets(new[] { t.Conn });
        tile.ContextRequested += ShowTileMenu;
        tile.DragRequested += (t, e) => Run(() => DragTileAsync(t, e));
        DragDrop.SetAllowDrop(tile.Root, true);
        tile.Root.AddHandler(DragDrop.DragOverEvent, (_, e) =>
        {
            if (e.Data.Get(DragFormat) is not string key || key == tile.Conn.Key) { e.DragEffects = DragDropEffects.None; return; }
            e.DragEffects = DragDropEffects.Move;
            tile.SetDropHint(e.GetPosition(tile.Root).X < tile.Root.Bounds.Width / 2 ? -1 : 1);
            e.Handled = true;
        });
        tile.Root.AddHandler(DragDrop.DragLeaveEvent, (_, _) => tile.SetDropHint(0));
        tile.Root.AddHandler(DragDrop.DropEvent, (_, e) =>
        {
            tile.SetDropHint(0);
            if (e.Data.Get(DragFormat) is not string key || key == tile.Conn.Key || !_tiles.TryGetValue(key, out var src)) return;
            bool after = e.GetPosition(tile.Root).X >= tile.Root.Bounds.Width / 2;
            _grid.Children.Remove(src.Root);
            int idx = _grid.Children.IndexOf(tile.Root) + (after ? 1 : 0);
            _grid.Children.Insert(Math.Clamp(idx, 0, _grid.Children.Count), src.Root);
            SaveTileOrder();
            e.Handled = true;
            SetStatus($"{src.Conn.Info.MachineName} déplacé.");
        });
        conn.AppsChanged += _ => Dispatcher.UIThread.Post(() => { _appsPanel.Invalidate(); tile.UpdateActiveApp(); });

        conn.ConnectionChanged += (c, up) => Dispatcher.UIThread.Post(() =>
        {
            tile.SetStatus(up ? "Connecté" : "Déconnecté");
            if (!up)
            {
                Attendance.MarkDisconnected(c);
                ActionLog.Add("Déconnexion", c.Info.MachineName);
            }
            UpdateBanner();
        });
        conn.HandChanged += (c, raised) => Dispatcher.UIThread.Post(() => OnHandChanged(tile, raised));
        conn.StudentLogin += (c, l) => Dispatcher.UIThread.Post(async () => await OnStudentLoginAsync(tile, l));
        conn.GroupDocReceived += (c, d) => _ = _groupMgr.HandleDocAsync(c, d);
        conn.GroupChatReceived += (c, m) => _ = _groupMgr.HandleChatAsync(c, m);
        conn.ChatReceived += (c, m) => Dispatcher.UIThread.Post(() => OnIncomingChat(c, m));
    }

    /// <summary>Poste (re)connecté : présence, miniature, restrictions, diffusion, séance, équipe.</summary>
    private async Task OnConnectedAsync(AgentConnection c)
    {
        Attendance.MarkConnected(c);
        ActionLog.Add("Connexion", c.Info.MachineName, $"{c.Info.UserName} — {c.Info.IpAddress}");
        if (_thumbsLive)
            try { await c.AcquireStream("thumb", ThumbProfile); } catch { }
        if (_restrictions.Enabled)
            try { await c.SetRestrictions(_restrictions); } catch { }
        if (_internetCutForClass || c.IsInternetBlocked)
            try { await c.SetInternet(true); } catch { }
        if (_muteForClass || c.IsMuted)
            try { await c.SetMute(true); } catch { }
        if (_inputLockForClass || c.IsInputLocked)
            try { await c.SetInputLock(true); } catch { }
        if (_appsVisible)
            try { await c.WatchApps(true); } catch { }
        if (_broadcaster.IsRunning)
            try { await _broadcaster.AddAsync(c); } catch { }
        if (_lesson != null)
            try { await c.StartLesson(_lesson); } catch { }
        try { await _groupMgr.OnConnectedAsync(c); } catch { }
        // Rappelle à l'élève ses étoiles après une reconnexion.
        if (c.Stars > 0)
            try { await c.Send(MessageType.Reward, new RewardPayload { Stars = c.Stars, Delta = 0 }); } catch { }
    }

    private void SaveIdentity()
    {
        _settings.RoomCode = _roomCode.Text?.Trim() is { Length: > 0 } r ? r : "CLASSE";
        _settings.TeacherName = _teacherName.Text?.Trim() is { Length: > 0 } t ? t : "Professeur";
        _settings.Save();
    }

    // =====================================================================
    //  Sélection
    // =====================================================================
    private void OnTileClicked(StudentTile tile, bool multi)
    {
        if (multi) tile.SetSelected(!tile.IsSelected);
        else
        {
            bool onlyThis = tile.IsSelected && _tiles.Values.Count(t => t.IsSelected) == 1;
            foreach (var t in _tiles.Values) t.SetSelected(false);
            tile.SetSelected(!onlyThis);
        }
        UpdateBanner();
    }

    private void SelectWhere(Func<StudentTile, bool> predicate)
    {
        foreach (var t in _tiles.Values) t.SetSelected(predicate(t));
        UpdateBanner();
    }

    /// <summary>Postes visés par une commande du ruban : la sélection, sinon toute la classe.</summary>
    private IReadOnlyList<AgentConnection> Targets()
    {
        var selected = _tiles.Values.Where(t => t.IsSelected && t.Conn.IsConnected).Select(t => t.Conn).ToList();
        return selected.Count > 0 ? selected : _tiles.Values.Where(t => t.Conn.IsConnected).Select(t => t.Conn).ToList();
    }

    private bool IsWholeClass(IReadOnlyCollection<AgentConnection> targets) =>
        targets.Count == _tiles.Values.Count(t => t.Conn.IsConnected);

    private void UpdateBanner()
    {
        int connected = _tiles.Values.Count(t => t.Conn.IsConnected);
        int selected = _tiles.Values.Count(t => t.IsSelected);
        _banner.Text = $"Salle {_settings.RoomCode}  ·  {connected}/{_tiles.Count} poste(s)" +
                       (selected > 0 ? $"  ·  {selected} sélectionné(s)" : "");
        _emptyHint.IsVisible = _tiles.Count == 0;
        int hands = _tiles.Values.Count(t => t.Conn.IsHandRaised);
        SetBigLabel(_helpBtn, hands > 0 ? $"Demandes d'aide ({hands})" : "Demandes d'aide");
        SetState(_helpBtn, hands > 0 ? "warn" : null);
    }

    private void RefreshTiles(IEnumerable<AgentConnection> conns)
    {
        foreach (var c in conns)
            if (_tiles.TryGetValue(c.Key, out var t)) t.Refresh();
    }

    private void RemoveTiles(IReadOnlyList<AgentConnection> targets)
    {
        foreach (var c in targets)
        {
            if (!_tiles.Remove(c.Key, out var t)) continue;
            _grid.Children.Remove(t.Root);
            c.Dispose();
        }
        UpdateBanner();
    }

    private void SortTiles()
    {
        var ordered = _tiles.Values.OrderBy(t => t.Conn.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        _grid.Children.Clear();
        foreach (var t in ordered) _grid.Children.Add(t.Root);
        SaveTileOrder();
    }

    // ---------- Glisser-déposer des vignettes ----------
    private async Task DragTileAsync(StudentTile tile, PointerEventArgs e)
    {
        var data = new DataObject();
        data.Set(DragFormat, tile.Conn.Key);
        tile.Root.Opacity = 0.5;
        try { await DragDrop.DoDragDrop(e, data, DragDropEffects.Move); }
        finally
        {
            tile.Root.Opacity = 1;
            foreach (var t in _tiles.Values) t.SetDropHint(0);
        }
    }

    /// <summary>Mémorise l'ordre des vignettes (par nom de poste, stable même si l'adresse IP change).</summary>
    private void SaveTileOrder()
    {
        var shown = _grid.Children.Select(c => _tiles.Values.FirstOrDefault(t => t.Root == c)?.Conn.Info.MachineName)
            .Where(n => n != null).Cast<string>().ToList();
        // Les postes absents aujourd'hui gardent leur place relative à la fin.
        _settings.TileOrder = shown.Concat(_settings.TileOrder.Where(n => !shown.Contains(n, StringComparer.OrdinalIgnoreCase))).ToList();
        _settings.Save();
    }

    /// <summary>Position d'un nouveau poste dans la grille, d'après l'ordre mémorisé.</summary>
    private int OrderedIndex(string machine)
    {
        int rank = _settings.TileOrder.FindIndex(n => string.Equals(n, machine, StringComparison.OrdinalIgnoreCase));
        if (rank < 0) return _grid.Children.Count;
        for (int i = 0; i < _grid.Children.Count; i++)
        {
            var t = _tiles.Values.FirstOrDefault(x => x.Root == _grid.Children[i]);
            if (t == null) continue;
            int r = _settings.TileOrder.FindIndex(n => string.Equals(n, t.Conn.Info.MachineName, StringComparison.OrdinalIgnoreCase));
            if (r < 0 || r > rank) return i;
        }
        return _grid.Children.Count;
    }

    // ---------- Applications ouvertes (écran partagé) ----------
    private async Task ShowAppsAsync(bool show)
    {
        _appsVisible = show;
        _appsPanel.IsVisible = show;
        _split.Children.OfType<GridSplitter>().First().IsVisible = show;
        _split.RowDefinitions = show ? new RowDefinitions("*,6,*") : new RowDefinitions("*,0,0");
        SetState(_sideApps, show ? "on" : null);
        foreach (var c in _tiles.Values.Select(t => t.Conn).Where(c => c.IsConnected))
            try { await c.WatchApps(show); } catch { }
        if (!show)
        {
            _appsPanel.ClearCache();
            foreach (var t in _tiles.Values) t.UpdateActiveApp();
        }
        _appsPanel.Invalidate();
        SetStatus(show ? "Applications ouvertes : liste mise à jour toutes les 2 secondes." : "Panneau des applications masqué.");
    }

    // ---------- Clavier / souris ----------
    private Task ToggleInputLockAsync(IReadOnlyList<AgentConnection> targets)
    {
        bool allLocked = targets.Count > 0 && targets.All(t => t.IsInputLocked);
        return SetInputLockAsync(targets, !allLocked);
    }

    /// <summary>Bloque le clavier et la souris des élèves : ils voient leur écran mais ne peuvent rien faire.</summary>
    private async Task SetInputLockAsync(IReadOnlyList<AgentConnection> targets, bool locked)
    {
        if (targets.Count == 0) { SetStatus("Aucun poste connecté."); return; }
        foreach (var c in targets) { try { await c.SetInputLock(locked); } catch { } }
        if (IsWholeClass(targets)) _inputLockForClass = locked;
        Log(locked ? "Clavier/souris bloqués" : "Clavier/souris débloqués", targets);
        RefreshTiles(targets);
        int n = _tiles.Values.Count(t => t.Conn.IsConnected && t.Conn.IsInputLocked);
        SetBigLabel(_inputBtn, n > 0 ? $"Débloquer ({n})" : "Bloquer clavier/souris");
        SetState(_inputBtn, n > 0 ? "live" : null);
        SetState(_sideInput, n > 0 ? "live" : null);
        SetStatus(locked ? $"Clavier et souris bloqués sur {targets.Count} poste(s) — vous pouvez toujours les contrôler à distance."
                         : $"Clavier et souris débloqués sur {targets.Count} poste(s).");
    }

    // ---------- Son ----------
    private Task ToggleMuteAsync(IReadOnlyList<AgentConnection> targets)
    {
        bool allMuted = targets.Count > 0 && targets.All(t => t.IsMuted);
        return SetMuteAsync(targets, !allMuted);
    }

    private async Task SetMuteAsync(IReadOnlyList<AgentConnection> targets, bool muted)
    {
        if (targets.Count == 0) { SetStatus("Aucun poste connecté."); return; }
        foreach (var c in targets) { try { await c.SetMute(muted); } catch { } }
        if (IsWholeClass(targets)) _muteForClass = muted;
        Log(muted ? "Son coupé" : "Son rétabli", targets);
        RefreshTiles(targets);
        int n = _tiles.Values.Count(t => t.Conn.IsConnected && t.Conn.IsMuted);
        SetBigLabel(_muteBtn, n > 0 ? $"Rétablir le son ({n})" : "Couper le son");
        SetState(_muteBtn, n > 0 ? "live" : null);
        SetStatus(muted ? $"Son coupé sur {targets.Count} poste(s) (les annonces du professeur restent audibles)."
                        : $"Son rétabli sur {targets.Count} poste(s).");
    }

    // =====================================================================
    //  Commandes
    // =====================================================================
    private void Log(string action, IReadOnlyCollection<AgentConnection> targets, string details = "") =>
        ActionLog.Add(action, ActionLog.Describe(targets, IsWholeClass(targets)), details);

    private async Task ApplyAsync(IReadOnlyList<AgentConnection> targets, string label, Func<AgentConnection, Task> action, string details = "")
    {
        if (targets.Count == 0) { SetStatus("Aucun poste connecté."); return; }
        foreach (var c in targets) { try { await action(c); } catch { } }
        Log(label, targets, details);
        SetStatus($"{label} : envoyé à {targets.Count} poste(s).");
    }

    private async Task LockAsync(IReadOnlyList<AgentConnection> targets)
    {
        await ApplyAsync(targets, "Verrouillage", c => c.Lock(_settings.LockMessage, _settings.LockImage()));
        foreach (var c in targets) { c.IsLocked = true; c.IsBlanked = false; }
        RefreshTiles(targets);
    }

    private async Task BlankAsync(IReadOnlyList<AgentConnection> targets)
    {
        await ApplyAsync(targets, "Écran noir", c => c.Blank());
        foreach (var c in targets) { c.IsBlanked = true; c.IsLocked = false; }
        RefreshTiles(targets);
    }

    private async Task UnlockAsync(IReadOnlyList<AgentConnection> targets)
    {
        await ApplyAsync(targets, "Déverrouillage", c => c.Unlock());
        foreach (var c in targets) { c.IsLocked = false; c.IsBlanked = false; }
        RefreshTiles(targets);
    }

    private void ViewTargets(IReadOnlyList<AgentConnection> targets, bool control = false)
    {
        var list = targets.Where(t => t.IsConnected).Take(control ? 1 : 4).ToList();
        if (list.Count == 0) { SetStatus("Sélectionnez un poste connecté (clic sur une vignette)."); return; }
        if (control && targets.Count(t => t.IsConnected) > 1 && !targets.Any(t => _tiles.Values.Any(x => x.Conn == t && x.IsSelected)))
        {
            SetStatus("Sélectionnez le poste à contrôler (clic sur sa vignette), puis « Contrôler ».");
            return;
        }
        foreach (var c in list)
        {
            new RemoteViewWindow(c, control).Show();
            ActionLog.Add(control ? "Prise de contrôle" : "Vue de l'écran", c.Info.MachineName);
        }
    }

    private async Task SendMessageAsync(IReadOnlyList<AgentConnection> targets)
    {
        if (targets.Count == 0) { SetStatus("Aucun poste connecté."); return; }
        var dlg = new ComposeMessageDialog();
        await dlg.ShowDialog(this);
        if (dlg.Result is not { } r || string.IsNullOrWhiteSpace(r.text)) return;
        await ApplyAsync(targets, "Message", c => c.ShowMessage(r.title, r.text, 0), r.text);
    }

    private async Task SendFileAsync(IReadOnlyList<AgentConnection> targets)
    {
        if (targets.Count == 0) { SetStatus("Aucun poste connecté."); return; }
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
        await ApplyAsync(targets, "Envoi de fichier", c => c.PushFile(name, content, open: false), $"{name} ({content.Length / 1024} Ko)");
    }

    private async Task LaunchAppAsync(IReadOnlyList<AgentConnection> targets)
    {
        var dlg = new InputDialog("Lancer une application", "Chemin de l'application ou URL à ouvrir :",
            "ex : notepad  ou  https://ofppt.ma", "notepad");
        await dlg.ShowDialog(this);
        if (string.IsNullOrWhiteSpace(dlg.Result)) return;
        string target = dlg.Result.Trim();
        await ApplyAsync(targets, "Lancement", c => c.LaunchApp(target, ""), target);
    }

    private async Task CloseAppAsync()
    {
        var dlg = new InputDialog("Fermer une application", "Nom du processus à fermer (sans .exe) :", "ex : chrome", "");
        await dlg.ShowDialog(this);
        if (string.IsNullOrWhiteSpace(dlg.Result)) return;
        string proc = dlg.Result.Trim();
        await ApplyAsync(Targets(), "Fermeture d'application", c => c.CloseApp(proc), proc);
    }

    private async Task EditQuickLaunchAsync()
    {
        var dlg = new QuickLaunchDialog(_settings.QuickLaunch);
        await dlg.ShowDialog(this);
        if (dlg.Result == null) return;
        _settings.QuickLaunch = dlg.Result;
        _settings.Save();
        SetStatus($"{dlg.Result.Count} favori(s) de lancement rapide enregistré(s).");
    }

    private async Task DesktopAsync(IReadOnlyList<AgentConnection> targets, DesktopAction action)
    {
        if (action == DesktopAction.CloseAllApps)
        {
            var confirm = new ConfirmDialog("Fermer toutes les applications",
                $"Fermer toutes les applications ouvertes sur {targets.Count} poste(s) ?\nLe travail non enregistré peut être perdu.");
            await confirm.ShowDialog(this);
            if (!confirm.Confirmed) return;
        }
        await ApplyAsync(targets, action == DesktopAction.MinimizeAll ? "Réduction des fenêtres" : "Fermeture des applications",
            c => c.Desktop(action));
    }

    private async Task PowerAsync(IReadOnlyList<AgentConnection> targets, string verbe, Func<AgentConnection, Task> action)
    {
        if (targets.Count == 0) { SetStatus("Aucun poste connecté."); return; }
        var confirm = new ConfirmDialog("Confirmation", $"Voulez-vous vraiment {verbe} {targets.Count} poste(s) ?");
        await confirm.ShowDialog(this);
        if (!confirm.Confirmed) return;
        await ApplyAsync(targets, "Alimentation : " + verbe, action);
    }

    private async Task RewardAsync(IReadOnlyList<AgentConnection> targets, int delta)
    {
        if (targets.Count == 0) { SetStatus("Aucun poste connecté."); return; }
        foreach (var c in targets) { try { await c.Reward(delta); } catch { } }
        foreach (var c in targets) Attendance.SetStars(c, c.Stars);
        Log(delta > 0 ? "Étoile donnée" : delta < 0 ? "Étoile retirée" : "Étoiles remises à zéro", targets);
        RefreshTiles(targets);
        SetStatus(delta > 0 ? $"⭐ Étoile donnée à {targets.Count} élève(s)." : "Récompenses mises à jour.");
    }

    private async Task LowerHandAsync(IReadOnlyList<AgentConnection> targets)
    {
        foreach (var c in targets.Where(c => c.IsHandRaised)) { try { await c.LowerHand(); } catch { } }
        ActionLog.Add("Main baissée", string.Join(", ", targets.Select(t => t.Info.MachineName)));
    }

    private void OnHandChanged(StudentTile tile, bool raised)
    {
        tile.Refresh();
        if (raised)
        {
            Attendance.CountHand(tile.Conn);
            ActionLog.Add("Main levée", tile.Conn.Info.MachineName);
            // Remonte la vignette en tête pour qu'elle soit visible tout de suite.
            if (_grid.Children.Remove(tile.Root)) _grid.Children.Insert(0, tile.Root);
            Activate();
        }
        int n = _tiles.Values.Count(t => t.Conn.IsHandRaised);
        SetStatus(n > 0 ? $"✋ {n} élève(s) demande(nt) de l'aide." : "Aucune main levée.");
        UpdateBanner();
    }

    // ---------- Discussion ----------
    private ConsoleChatWindow OpenChat(IReadOnlyList<AgentConnection> targets)
    {
        var existing = _chats.FirstOrDefault(w => w.Members.Count == targets.Count && !w.Members.Except(targets).Any());
        if (existing != null) { existing.Activate(); return existing; }
        var w = new ConsoleChatWindow(targets, () => _teacherName.Text?.Trim() ?? "");
        w.Closed += (_, _) => _chats.Remove(w);
        _chats.Add(w);
        w.Show(this);
        return w;
    }

    private void OnIncomingChat(AgentConnection from, ChatPayload m)
    {
        // Une fenêtre qui contient déjà cet élève reçoit le message elle-même.
        if (_chats.Any(w => w.Members.Contains(from))) return;
        var w = OpenChat(new[] { from });
        w.AddIncoming(from, m);
        ActionLog.Add("Message d'élève", from.Info.MachineName, m.Text);
    }

    // ---------- Annonce ----------
    private async Task ToggleAnnounceAsync()
    {
        if (_announcer.IsLive)
        {
            int n = _announcer.TargetCount;
            await _announcer.StopAsync();
            SetState(_announceBtn, null);
            SetState(_sideAnnounce, null);
            SetBigLabel(_announceBtn, "Annonce");
            ActionLog.Add("Fin d'annonce", $"{n} poste(s)");
            SetStatus("Annonce terminée.");
            return;
        }
        var targets = Targets();
        if (targets.Count == 0) { SetStatus("Aucun poste connecté."); return; }
        try
        {
            await _announcer.StartAsync(targets, _teacherName.Text?.Trim() ?? "Le professeur");
            SetState(_announceBtn, "live");
            SetState(_sideAnnounce, "live");
            SetBigLabel(_announceBtn, "Arrêter l'annonce");
            Log("Annonce audio", targets);
            SetStatus($"🎙 Annonce en direct vers {targets.Count} poste(s) — recliquez pour arrêter.");
        }
        catch (Exception ex)
        {
            await _announcer.StopAsync();
            SetStatus("Annonce impossible : " + ex.Message);
        }
    }

    // ---------- Diffusion ----------
    private async Task ShowMyScreenAsync()
    {
        if (_broadcaster.IsRunning) { await StopBroadcastAsync(); return; }
        var targets = Targets();
        if (targets.Count == 0) { SetStatus("Aucun poste connecté."); return; }
        var dlg = new BroadcastDialog(targets.Count);
        await dlg.ShowDialog(this);
        if (dlg.Result is not { } options) return;

        await _broadcaster.StartAsync(targets, options);
        SetBroadcastUi(true);
        Log("Diffusion de l'écran", targets, options.Exclusive ? "plein écran, entrées bloquées" : "fenêtre");
        SetStatus($"📺 Diffusion en cours vers {targets.Count} poste(s).");
    }

    private async Task ExhibitSelectedAsync()
    {
        var sel = _tiles.Values.Where(t => t.IsSelected && t.Conn.IsConnected).ToList();
        if (sel.Count != 1) { SetStatus("Sélectionnez UN poste élève à présenter à la classe."); return; }
        await RelayStudentAsync(sel[0].Conn);
    }

    private async Task RelayStudentAsync(AgentConnection source)
    {
        if (!source.IsConnected) return;
        if (_broadcaster.IsRunning) { SetStatus("Une diffusion est déjà en cours : arrêtez-la d'abord (Montrer › Arrêter)."); return; }
        var targets = _tiles.Values.Where(t => t.Conn.IsConnected && t.Conn != source).Select(t => t.Conn).ToList();
        if (targets.Count == 0) { SetStatus("Aucun autre poste connecté."); return; }

        var dlg = new BroadcastDialog(targets.Count, $"L'écran de {source.DisplayName} ({source.Info.MachineName})");
        await dlg.ShowDialog(this);
        if (dlg.Result is not { } options) return;
        options.Title = $"Écran de {source.DisplayName}";

        await _broadcaster.StartRelayAsync(source, targets, options);
        SetBroadcastUi(true);
        ActionLog.Add("Présentation de l'écran d'un élève", $"{source.Info.MachineName} → {targets.Count} poste(s)", source.DisplayName);
        SetStatus($"📡 L'écran de {source.DisplayName} est présenté à {targets.Count} poste(s).");
    }

    private async Task StopBroadcastAsync()
    {
        if (!_broadcaster.IsRunning) { SetStatus("Aucune diffusion en cours."); return; }
        int n = _broadcaster.Targets.Count;
        await _broadcaster.StopAsync();
        SetBroadcastUi(false);
        ActionLog.Add("Fin de diffusion", $"{n} poste(s)");
        SetStatus("Diffusion arrêtée.");
    }

    private void SetBroadcastUi(bool live)
    {
        SetBigLabel(_showBtn, live ? "Diffusion en cours" : "Montrer");
        SetState(_showBtn, live ? "live" : null);
    }

    // ---------- Internet ----------
    /// <summary>Bouton du ruban : coupe Internet sur les cibles, ou le rétablit s'il est déjà coupé partout.</summary>
    private Task ToggleInternetAsync(IReadOnlyList<AgentConnection> targets)
    {
        bool allCut = targets.Count > 0 && targets.All(t => t.IsInternetBlocked);
        return SetInternetAsync(targets, !allCut);
    }

    private async Task SetInternetAsync(IReadOnlyList<AgentConnection> targets, bool blocked)
    {
        if (targets.Count == 0) { SetStatus("Aucun poste connecté."); return; }
        foreach (var c in targets) { try { await c.SetInternet(blocked); } catch { } }
        if (IsWholeClass(targets)) _internetCutForClass = blocked;
        Log(blocked ? "Internet coupé" : "Internet rétabli", targets);
        RefreshTiles(targets);
        UpdateInternetUi();
        SetStatus(blocked
            ? $"Internet coupé sur {targets.Count} poste(s) — le réseau de la salle reste accessible."
            : $"Internet rétabli sur {targets.Count} poste(s).");
    }

    private void UpdateInternetUi()
    {
        int cut = _tiles.Values.Count(t => t.Conn.IsConnected && t.Conn.IsInternetBlocked);
        SetBigLabel(_netBtn, cut > 0 ? $"Rétablir Internet ({cut})" : "Couper Internet");
        SetState(_netBtn, cut > 0 ? "live" : null);
        SetState(_sideNet, cut > 0 ? "live" : null);
    }

    // ---------- Accès web / restrictions ----------
    private static string WebLabel(WebMode m) => m switch
    {
        WebMode.Unrestricted => "Sans restriction",
        WebMode.BlockList => "Sites interdits",
        WebMode.AllowList => "Sites approuvés",
        WebMode.BlockAll => "Web bloqué",
        _ => "Web",
    };

    private async Task SetWebModeAsync(WebMode mode)
    {
        if (mode == WebMode.AllowList && _restrictions.AllowedSites.Count == 0)
        {
            SetStatus("Ajoutez d'abord des sites approuvés (Accès web › Modifier les listes).");
            await EditRestrictionsAsync();
            if (_restrictions.AllowedSites.Count == 0) return;
        }
        _restrictions.Web = mode;
        _restrictions.Enabled = mode != WebMode.Unrestricted || _restrictions.BlockedApps.Count > 0;
        var targets = Targets();
        await ApplyAsync(targets, "Accès web : " + WebLabel(mode), c => c.SetRestrictions(_restrictions));
        SetBigLabel(_webBtn, WebLabel(mode));
        SetState(_webBtn, mode != WebMode.Unrestricted ? "warn" : null);
    }

    private async Task EditRestrictionsAsync()
    {
        var targets = Targets();
        var dlg = new RestrictionsDialog(_restrictions, targets.Count);
        await dlg.ShowDialog(this);
        if (dlg.Result is not { } r) return;

        if (!r.Enabled) r.Web = WebMode.Unrestricted;
        r.Enabled = r.BlockedApps.Count > 0 || r.Web != WebMode.Unrestricted;
        _restrictions = r;
        _settings.AllowedSites = r.AllowedSites.ToList();
        _settings.Save();
        await ApplyAsync(targets, r.Enabled ? "Restrictions appliquées" : "Restrictions levées", c => c.SetRestrictions(r),
            r.Enabled ? $"apps : {string.Join(", ", r.BlockedApps)} | web : {WebLabel(r.Web)}" : "");
        SetBigLabel(_webBtn, WebLabel(r.Web));
        SetState(_webBtn, r.Web != WebMode.Unrestricted ? "warn" : null);
    }

    private async Task OpenWebsiteAsync()
    {
        var dlg = new InputDialog("Ouvrir un site web",
            "Adresse du site à ouvrir sur les postes (navigateur par défaut de chaque poste) :",
            "ex : ofppt.ma  ou  https://docs.google.com/…", "https://");
        await dlg.ShowDialog(this);
        string url = (dlg.Result ?? "").Trim();
        if (url.Length == 0 || url == "https://") return;
        if (!url.Contains("://")) url = "https://" + url;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
        {
            SetStatus("Adresse invalide : " + url);
            return;
        }
        await ApplyAsync(Targets(), "Ouverture de site", c => c.LaunchApp(uri.AbsoluteUri, ""), uri.AbsoluteUri);
    }

    // ---------- Séance ----------
    private async Task ToggleLessonAsync()
    {
        var targets = _tiles.Values.Where(t => t.Conn.IsConnected).Select(t => t.Conn).ToList();
        if (_lesson != null)
        {
            var confirm = new ConfirmDialog("Terminer la séance", "Terminer la séance ? Les élèves n'auront plus à s'identifier.");
            await confirm.ShowDialog(this);
            if (!confirm.Confirmed) return;
            foreach (var c in targets) { try { await c.EndLesson(); } catch { } }
            ActionLog.Add("Fin de séance", _lesson.Title, $"{Attendance.Rows.Count(r => r.StudentName.Length > 0)} élève(s) identifié(s)");
            _lesson = null;
            SetBigLabel(_lessonBtn, "Démarrer la séance");
            SetState(_lessonBtn, null);
            foreach (var t in _tiles.Values) { t.LessonTitle = ""; t.Refresh(); }
            SetStatus("Séance terminée.");
            return;
        }

        var dlg = new InputDialog("Démarrer la séance",
            "Titre de la séance. Chaque élève devra saisir son nom pour accéder à son poste :",
            "ex : Bureautique — TP Excel", _settings.LessonTitle);
        await dlg.ShowDialog(this);
        if (string.IsNullOrWhiteSpace(dlg.Result)) return;
        _settings.LessonTitle = dlg.Result.Trim();
        _settings.Save();

        _lesson = new SessionStartPayload
        {
            SessionId = Guid.NewGuid().ToString("N"),
            Title = _settings.LessonTitle,
            TeacherName = _teacherName.Text?.Trim() ?? "",
        };
        foreach (var t in _tiles.Values)
        {
            t.Conn.StudentName = "";
            t.LessonTitle = _lesson.Title;
            t.Refresh();
        }
        foreach (var c in targets) { try { await c.StartLesson(_lesson); } catch { } }
        SetBigLabel(_lessonBtn, "Terminer la séance");
        SetState(_lessonBtn, "ok");
        ActionLog.Add("Début de séance", ActionLog.Describe(targets, true), _lesson.Title);
        SetStatus($"🎓 Séance « {_lesson.Title} » démarrée : les élèves s'identifient…");
    }

    private async Task OnStudentLoginAsync(StudentTile tile, StudentLoginPayload l)
    {
        var c = tile.Conn;
        string name = (l.FullName ?? "").Trim();
        if (_lesson == null || l.SessionId != _lesson.SessionId)
        {
            await c.AckLogin(false, "Aucune séance en cours. Patientez.");
            return;
        }
        if (name.Length < 3 || name.Length > 80)
        {
            await c.AckLogin(false, "Nom invalide : saisissez votre prénom et votre nom.");
            return;
        }
        var dup = _tiles.Values.FirstOrDefault(o => o.Conn != c && o.Conn.IsConnected &&
            string.Equals(o.Conn.StudentName, name, StringComparison.OrdinalIgnoreCase));
        if (dup != null)
        {
            await c.AckLogin(false, $"« {name} » est déjà connecté sur {dup.Conn.Info.MachineName}. Prévenez le professeur.");
            ActionLog.Add("Identification refusée", c.Info.MachineName, $"{name} déjà sur {dup.Conn.Info.MachineName}");
            return;
        }

        c.StudentName = name;
        tile.Refresh();
        Attendance.SetStudentName(c, name);
        await c.AckLogin(true, "");
        ActionLog.Add("Élève identifié", c.Info.MachineName, name);
        int logged = _tiles.Values.Count(x => x.Conn.IsConnected && x.Conn.StudentName.Length > 0);
        int total = _tiles.Values.Count(x => x.Conn.IsConnected);
        SetStatus($"🎓 {logged}/{total} élève(s) identifié(s).");
    }

    // ---------- Fenêtres ----------
    private void OpenAttendance() => new AttendanceWindow(() => (_teacherName.Text?.Trim() ?? "", _roomCode.Text?.Trim() ?? "")).Show(this);

    private void OpenGroups() =>
        new GroupsWindow(_groupMgr, () => _tiles.Values.Where(t => t.Conn.IsConnected).Select(t => t.Conn).ToList(), _settings).Show(this);

    private void OpenSurvey()
    {
        var targets = Targets();
        if (targets.Count == 0) { SetStatus("Aucun poste connecté."); return; }
        new SurveyWindow(targets).Show(this);
    }

    private async Task OpenSettingsAsync()
    {
        var dlg = new SettingsDialog(_settings);
        await dlg.ShowDialog(this);
        if (dlg.Saved) SetStatus("Paramètres enregistrés.");
    }

    // ---------- Miniatures ----------
    private void ToggleLive()
    {
        _thumbsLive = !_thumbsLive;
        SetState(_liveBtn, _thumbsLive ? "on" : null);
        Run(ApplyThumbProfileAsync);
        SetStatus(_thumbsLive ? "Miniatures en direct activées." : "Miniatures en pause (réseau soulagé).");
    }

    private void SetFps(int fps)
    {
        _settings.ThumbFps = fps;
        _settings.Save();
        Run(ApplyThumbProfileAsync);
        SetStatus($"Miniatures : {fps} image(s) par seconde.");
    }

    private bool _settingZoom;

    /// <summary>Change la taille des vignettes ; la résolution des flux suit après une courte pause.</summary>
    private void SetZoom(double width, bool fromSlider = false)
    {
        if (_settingZoom) return;
        width = Math.Round(Math.Clamp(width, ZoomMin, ZoomMax));
        if (Math.Abs(width - _settings.TileWidth) < 0.5 && !fromSlider) return;
        _settingZoom = true;
        try
        {
            _settings.TileWidth = width;
            if (!fromSlider) _zoom.Value = width;
            foreach (var t in _tiles.Values) t.SetWidth(width);
            UpdateZoomText();
        }
        finally { _settingZoom = false; }

        // Évite de renégocier les flux à chaque cran du curseur.
        _zoomSettle ??= new DispatcherTimer(TimeSpan.FromMilliseconds(450), DispatcherPriority.Background, (_, _) =>
        {
            _zoomSettle!.Stop();
            _settings.Save();
            Run(ApplyThumbProfileAsync);
        });
        _zoomSettle.Stop();
        _zoomSettle.Start();
    }

    private void UpdateZoomText() => _zoomText.Text = $"{Math.Round(TileWidth / ZoomBase * 100)} %";

    /// <summary>Plus grande taille de vignette permettant d'afficher tous les postes sans défiler.</summary>
    private void FitToWindow()
    {
        int n = _tiles.Count;
        if (n == 0) return;
        double w = _scroller.Bounds.Width - 30, h = _scroller.Bounds.Height - 30;
        for (double tw = ZoomMax; tw >= ZoomMin; tw -= 5)
        {
            double cellW = tw + 32;                  // marges + remplissage de la vignette
            double cellH = Math.Round(tw * 0.6) + 22 + 24 + 32 + 4; // bandeau, pied, marges, bordure
            int cols = Math.Max(1, (int)(w / cellW));
            int rows = (int)Math.Ceiling(n / (double)cols);
            if (rows * cellH <= h) { SetZoom(tw); return; }
        }
        SetZoom(ZoomMin);
    }

    private async Task ApplyThumbProfileAsync()
    {
        foreach (var t in _tiles.Values.Where(t => t.Conn.IsConnected))
        {
            try
            {
                if (_thumbsLive) await t.Conn.AcquireStream("thumb", ThumbProfile);
                else await t.Conn.ReleaseStream("thumb");
            }
            catch { }
        }
    }

    // =====================================================================
    //  Utilitaires
    // =====================================================================
    private void Run(Func<Task> action) => Dispatcher.UIThread.Post(async () =>
    {
        try { await action(); }
        catch (Exception ex) { SetStatus("Erreur : " + ex.Message); }
    });

    private static void SetBigLabel(Button b, string label)
    {
        if (b.Content is StackPanel { Children.Count: > 1 } sp && sp.Children[1] is TextBlock tb)
            tb.Text = label + (b.Flyout != null ? " ▾" : "");
    }

    /// <summary>État visuel d'un bouton : null (normal), "on" (enclenché), "live" (en cours),
    /// "warn" (alerte) ou "ok" (séance) — les couleurs sont définies dans ConsoleStyles.axaml.</summary>
    private static void SetState(Button b, string? state)
    {
        foreach (var c in new[] { "on", "live", "warn", "ok" }) b.Classes.Set(c, c == state);
    }

    private void SetStatus(string text) => Dispatcher.UIThread.Post(() => _status.Text = text);
}
