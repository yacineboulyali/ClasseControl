using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using ClasseControle.Common;

namespace ClasseControle.Agent;

/// <summary>
/// Applique les restrictions envoyées par le professeur :
///  • applications interdites → le processus est fermé dès qu'il apparaît ;
///  • sites interdits → filtrage par NOM DE DOMAINE via un proxy local
///    (<see cref="WebFilterGuard"/>, HTTP et HTTPS), complété par le contrôle du titre
///    de l'onglet actif : un titre contenant un mot-clé interdit ferme l'onglet (Ctrl+W).
/// Fonctionne sans droits administrateur (pas de modification du fichier hosts).
/// Les restrictions restent actives jusqu'à leur levée par le professeur ou
/// jusqu'à la fermeture de l'agent.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class RestrictionEnforcer : IDisposable
{
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "opera", "opera_gx", "brave", "vivaldi", "iexplore", "chromium", "yandex",
    };

    // Processus qu'on ne ferme jamais, même si le professeur les liste par erreur.
    private static readonly HashSet<string> Protected = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "svchost", "csrss", "winlogon", "wininit", "lsass", "services", "smss", "dwm",
        "system", "idle", "fontdrvhost", "sihost", "ctfmon", "conhost", "ClasseControle.Agent",
    };

    private readonly IAgentHost _host;
    private readonly object _lock = new();
    private readonly Timer _timer;
    private RestrictionsPayload _current = new();
    // Listes développées (catégories « @… » remplacées par leur contenu).
    private HashSet<string> _appNames = new(StringComparer.OrdinalIgnoreCase);
    private List<string> _storeGameTokens = new();
    private List<string> _sites = new();
    private DateTime _lastSiteClose = DateTime.MinValue;
    private DateTime _lastAppNotice = DateTime.MinValue;

    /// <summary>Filtrage web par domaine (proxy local).</summary>
    public WebFilterGuard Web { get; } = new();

    public RestrictionEnforcer(IAgentHost host)
    {
        _host = host;
        Web.Notify += (title, text) => _host.ShowMessage(title, text, 5);
        _timer = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool IsActive
    {
        get { lock (_lock) return _current.Enabled; }
    }

    public void Apply(RestrictionsPayload p)
    {
        var appsInput = (p.BlockedApps ?? new()).ToList();
        bool storeGames = appsInput.Any(a => string.Equals(a.Trim(), RestrictionCatalog.WindowsGames.Token, StringComparison.OrdinalIgnoreCase));
        var names = Normalize(RestrictionCatalog.Expand(appsInput.Where(a => !string.Equals(a.Trim(), RestrictionCatalog.WindowsGames.Token, StringComparison.OrdinalIgnoreCase)),
                                                        RestrictionCatalog.AppCategories), stripExe: true)
                    .Where(a => !Protected.Contains(a));
        // Titres d'onglets : « youtube.com » est cherché comme « youtube ».
        var sites = Normalize(RestrictionCatalog.Expand(p.BlockedSites ?? new(), RestrictionCatalog.SiteCategories)
                                  .Select(WebFilterRules.TitleKeyword), stripExe: false);
        lock (_lock)
        {
            _appNames = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
            _storeGameTokens = storeGames ? RestrictionCatalog.WindowsGames.Items.ToList() : new();
            _sites = sites;
        }
        var clean = new RestrictionsPayload
        {
            Enabled = p.Enabled,
            BlockedApps = Normalize(appsInput, stripExe: true).Where(a => !Protected.Contains(a)).ToList(),
            BlockedSites = Normalize(p.BlockedSites, stripExe: false),
            AllowedSites = Normalize(p.AllowedSites, stripExe: false),
            Web = p.Web,
        };
        bool webActive = clean.Web switch
        {
            WebMode.BlockList => clean.BlockedSites.Count > 0,
            WebMode.AllowList or WebMode.BlockAll => true,
            _ => false,
        };
        bool active = clean.Enabled && (clean.BlockedApps.Count > 0 || webActive);
        lock (_lock) _current = clean;
        try { Web.Apply(clean); } catch { }
        _timer.Change(active ? 0 : Timeout.Infinite, 1000);

        if (active)
            _host.ShowMessage("Restrictions actives",
                "Le professeur a restreint l'utilisation de ce poste." +
                (clean.BlockedApps.Count > 0 ? "\n\nApplications interdites : " + RestrictionCatalog.Describe(clean.BlockedApps, RestrictionCatalog.AppCategories) : "") +
                clean.Web switch
                {
                    WebMode.BlockList when clean.BlockedSites.Count > 0 => "\nSites interdits : " + RestrictionCatalog.Describe(clean.BlockedSites, RestrictionCatalog.SiteCategories),
                    WebMode.AllowList => "\nSeuls ces sites sont autorisés : " + (clean.AllowedSites.Count > 0 ? string.Join(", ", clean.AllowedSites) : "aucun"),
                    WebMode.BlockAll => "\nLes navigateurs internet sont interdits.",
                    _ => "",
                },
                8);
    }

    private static List<string> Normalize(IEnumerable<string>? items, bool stripExe) =>
        (items ?? Enumerable.Empty<string>())
            .Select(s => s.Trim())
            .Select(s => stripExe && s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s)
            .Where(s => s.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private void Tick()
    {
        RestrictionsPayload r;
        lock (_lock) r = _current;
        if (!r.Enabled) return;
        HashSet<string> names; List<string> tokens, sites;
        lock (_lock) { names = _appNames; tokens = _storeGameTokens; sites = _sites; }
        try { EnforceApps(names, tokens); } catch { }
        try
        {
            switch (r.Web)
            {
                case WebMode.BlockList: EnforceSites(sites); break;
                // Le proxy filtre déjà par domaine ; le contrôle par titre (moins fiable : titres
                // traduits, accentués…) ne sert que si le proxy n'a pas pu démarrer.
                case WebMode.AllowList when !Web.IsActive:
                    EnforceAllowList(r.AllowedSites.Select(WebFilterRules.TitleKeyword).Where(k => k.Length > 0).ToList()); break;
                case WebMode.BlockAll: EnforceApps(Browsers, new List<string>(), "Navigateur interdit"); break;
            }
        }
        catch { }
    }

    // Chemins des processus déjà examinés (pid → chemin), vidé régulièrement.
    private readonly Dictionary<int, string?> _paths = new();
    private DateTime _pathsReset = DateTime.UtcNow;

    /// <summary>
    /// Ferme les applications interdites : par nom de processus, et pour les jeux du Store
    /// (Solitaire, Candy Crush…) par le nom du paquet dans leur dossier « WindowsApps ».
    /// </summary>
    private void EnforceApps(ISet<string> names, List<string> storeTokens, string title = "Application interdite")
    {
        if (names.Count == 0 && storeTokens.Count == 0) return;
        if (DateTime.UtcNow - _pathsReset > TimeSpan.FromSeconds(30)) { _paths.Clear(); _pathsReset = DateTime.UtcNow; }
        int session = Process.GetCurrentProcess().SessionId;
        var killed = new List<string>();
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                if (p.SessionId != session) continue;
                bool hit = names.Contains(p.ProcessName);
                if (!hit && storeTokens.Count > 0)
                {
                    if (!_paths.TryGetValue(p.Id, out var path)) _paths[p.Id] = path = Native.ProcessPath(p.Id);
                    hit = path != null && path.Contains("\\WindowsApps\\", StringComparison.OrdinalIgnoreCase)
                          && storeTokens.Any(t => path.Contains(t, StringComparison.OrdinalIgnoreCase));
                }
                if (!hit || Protected.Contains(p.ProcessName)) continue;
                string label = string.IsNullOrWhiteSpace(p.MainWindowTitle) ? p.ProcessName : p.MainWindowTitle;
                p.Kill(entireProcessTree: true);
                killed.Add(label);
            }
            catch { }
            finally { p.Dispose(); }
        }
        if (killed.Count > 0 && DateTime.UtcNow - _lastAppNotice > TimeSpan.FromSeconds(10))
        {
            _lastAppNotice = DateTime.UtcNow;
            _host.ShowMessage(title,
                $"« {string.Join(", ", killed.Distinct())} » a été fermée : interdite pendant le cours.", 5);
        }
    }

    /// <summary>Titre de l'onglet actif si la fenêtre au premier plan est un navigateur, sinon null.</summary>
    private string? ActiveBrowserTitle()
    {
        // Laisse au navigateur le temps de fermer l'onglet avant de réessayer.
        if (DateTime.UtcNow - _lastSiteClose < TimeSpan.FromMilliseconds(1500)) return null;
        IntPtr hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return null;
        Native.GetWindowThreadProcessId(hwnd, out uint pid);
        try
        {
            using var p = Process.GetProcessById((int)pid);
            if (!Browsers.Contains(p.ProcessName)) return null;
        }
        catch { return null; }
        var sb = new StringBuilder(512);
        Native.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private void CloseActiveTab(string title, string message)
    {
        _lastSiteClose = DateTime.UtcNow;
        const ushort VK_CONTROL = 0x11, VK_W = 0x57;
        InputInjector.Chord(VK_CONTROL, VK_W);
        _host.ShowMessage(title, message, 5);
    }

    private void EnforceSites(List<string> sites)
    {
        if (sites.Count == 0) return;
        string? title = ActiveBrowserTitle();
        if (title == null) return;
        string? hit = sites.FirstOrDefault(s => title.Contains(s, StringComparison.OrdinalIgnoreCase));
        if (hit != null)
            CloseActiveTab("Site interdit", $"Ce site (« {hit} ») est interdit pendant le cours. L'onglet a été fermé.");
    }

    // Titres neutres (nouvel onglet, page en chargement) tolérés en mode « sites approuvés ».
    private static readonly string[] NeutralTitles =
    {
        "nouvel onglet", "new tab", "nouvelle page", "page de démarrage", "start page", "démarrer", "accueil",
        "untitled", "sans titre", "chargement", "loading", "about:blank",
    };
    private string _lastRejected = "";

    private void EnforceAllowList(List<string> allowed)
    {
        string? title = ActiveBrowserTitle();
        if (title == null) return;
        string t = title.Trim();
        // Retire le nom du navigateur en fin de titre (« Page - Google Chrome »).
        int dash = t.LastIndexOf(" - ", StringComparison.Ordinal);
        string page = dash > 0 ? t[..dash] : "";
        if (page.Length == 0 || NeutralTitles.Any(n => page.Contains(n, StringComparison.OrdinalIgnoreCase))) { _lastRejected = ""; return; }
        if (allowed.Any(a => t.Contains(a, StringComparison.OrdinalIgnoreCase))) { _lastRejected = ""; return; }

        // Deux constats consécutifs avant d'agir : évite de fermer une page encore en chargement.
        if (_lastRejected != t) { _lastRejected = t; return; }
        _lastRejected = "";
        CloseActiveTab("Site non autorisé",
            "Pendant ce cours, seuls ces sites sont autorisés : " + (allowed.Count > 0 ? string.Join(", ", allowed) : "aucun") + ".");
    }

    public void Dispose()
    {
        _timer.Dispose();
        Web.Dispose();
    }
}
