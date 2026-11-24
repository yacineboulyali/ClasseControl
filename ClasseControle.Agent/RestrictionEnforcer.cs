using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using ClasseControle.Common;

namespace ClasseControle.Agent;

/// <summary>
/// Applique les restrictions envoyées par le professeur :
///  • applications interdites → le processus est fermé dès qu'il apparaît ;
///  • sites interdits → si l'onglet actif d'un navigateur a un titre contenant
///    un mot-clé interdit, l'onglet est fermé (Ctrl+W) et l'élève est prévenu.
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
    private DateTime _lastSiteClose = DateTime.MinValue;
    private DateTime _lastAppNotice = DateTime.MinValue;

    public RestrictionEnforcer(IAgentHost host)
    {
        _host = host;
        _timer = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public bool IsActive
    {
        get { lock (_lock) return _current.Enabled; }
    }

    public void Apply(RestrictionsPayload p)
    {
        var clean = new RestrictionsPayload
        {
            Enabled = p.Enabled,
            BlockedApps = Normalize(p.BlockedApps, stripExe: true).Where(a => !Protected.Contains(a)).ToList(),
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
        _timer.Change(active ? 0 : Timeout.Infinite, 1000);

        if (active)
            _host.ShowMessage("Restrictions actives",
                "Le professeur a restreint l'utilisation de ce poste." +
                (clean.BlockedApps.Count > 0 ? "\n\nApplications interdites : " + string.Join(", ", clean.BlockedApps) : "") +
                clean.Web switch
                {
                    WebMode.BlockList when clean.BlockedSites.Count > 0 => "\nSites interdits : " + string.Join(", ", clean.BlockedSites),
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
        try { EnforceApps(r.BlockedApps); } catch { }
        try
        {
            switch (r.Web)
            {
                case WebMode.BlockList: EnforceSites(r.BlockedSites); break;
                case WebMode.AllowList: EnforceAllowList(r.AllowedSites); break;
                case WebMode.BlockAll: EnforceApps(Browsers.ToList(), "Navigateur interdit"); break;
            }
        }
        catch { }
    }

    private void EnforceApps(List<string> apps, string title = "Application interdite")
    {
        var killed = new List<string>();
        foreach (var name in apps)
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try { p.Kill(entireProcessTree: true); killed.Add(name); } catch { }
                finally { p.Dispose(); }
            }
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

    public void Dispose() => _timer.Dispose();
}
