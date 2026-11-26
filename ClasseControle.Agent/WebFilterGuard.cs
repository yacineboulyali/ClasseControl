using System.Diagnostics;
using System.Runtime.Versioning;
using ClasseControle.Common;

namespace ClasseControle.Agent;

/// <summary>
/// Filtrage web avancé du poste élève, par NOM DE DOMAINE :
///
///  1. un proxy de filtrage local (<see cref="WebFilterProxy"/>, 127.0.0.1) décide site par site,
///     en HTTP comme en HTTPS, sans déchiffrer le trafic ;
///  2. les réglages proxy de Windows de l'élève pointent vers ce proxy ; ils sont vérifiés
///     toutes les 2 secondes et rétablis si l'élève les modifie ;
///  3. si l'agent a les droits administrateur : règles du pare-feu qui interdisent aux
///     navigateurs de joindre Internet directement (ports 80/443, TCP et QUIC) — ils ne
///     peuvent plus contourner le proxy (extension VPN, réglage proxy du navigateur…) ;
///  4. côté installateur (ClasseControle.exe --browser-policies on) : stratégies Chrome / Edge / Brave / Firefox qui forcent
///     l'usage du proxy système et empêchent l'élève de le changer dans le navigateur.
///
/// Le blocage par titre d'onglet (RestrictionEnforcer) reste actif en complément.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WebFilterGuard : IDisposable
{
    private const string FirewallRule = "Classe_controle - Filtrage web";

    // Adresses IPv4/IPv6 publiques (le réseau de la salle reste joignable directement).
    private const string PublicIps =
        "1.0.0.0-9.255.255.255,11.0.0.0-126.255.255.255,128.0.0.0-169.253.255.255," +
        "169.255.0.0-172.15.255.255,172.32.0.0-192.167.255.255,192.169.0.0-223.255.255.255," +
        "2000::-3fff:ffff:ffff:ffff:ffff:ffff:ffff:ffff";

    private static readonly string[] BrowserExeNames =
    {
        "chrome", "msedge", "firefox", "opera", "opera_gx", "brave", "vivaldi", "iexplore", "chromium", "yandex", "browser",
    };

    private readonly object _lock = new();
    private readonly Timer _watchdog;
    private WebFilterProxy? _proxy;
    private bool _firewallOn;
    private readonly Dictionary<string, DateTime> _lastReported = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastStudentNotice = DateTime.MinValue, _lastTamperNotice = DateTime.MinValue;

    /// <summary>Un site a été bloqué (domaine) : à signaler au professeur.</summary>
    public event Action<string>? SiteBlocked;

    /// <summary>Message à afficher à l'élève (titre, texte).</summary>
    public event Action<string, string>? Notify;

    public WebFilterGuard()
    {
        _watchdog = new Timer(_ => Watch(), null, Timeout.Infinite, Timeout.Infinite);
        InternetBlocker.Unblocked += () => { if (IsActive) Watch(); };
    }

    /// <summary>Le proxy de filtrage tourne et le poste est dirigé vers lui.</summary>
    public bool IsActive
    {
        get { lock (_lock) return _proxy != null; }
    }

    private string Server => "127.0.0.1:" + _proxy!.Port;

    /// <summary>Applique les restrictions web reçues du professeur (démarre, met à jour ou arrête le filtrage).</summary>
    public void Apply(RestrictionsPayload r)
    {
        var rules = new WebFilterRules(r.Web, r.BlockedSites, r.AllowedSites);
        bool wanted = r.Enabled && rules.IsActive;
        lock (_lock)
        {
            if (!wanted) { StopLocked(); return; }
            if (_proxy != null) { _proxy.Rules = rules; return; }
            try
            {
                _proxy = new WebFilterProxy(rules);
                _proxy.Blocked += OnBlocked;
            }
            catch
            {
                _proxy = null;   // port indisponible : le blocage par titre d'onglet prend le relais
                return;
            }
            if (!InternetBlocker.BlockedByProxy) SystemProxy.Apply(Server);
            if (InternetBlocker.IsElevated) _firewallOn = AddFirewallRules();
            _watchdog.Change(2000, 2000);
        }
    }

    private void StopLocked()
    {
        _watchdog.Change(Timeout.Infinite, Timeout.Infinite);
        if (_proxy == null) return;
        string server = Server;
        _proxy.Blocked -= OnBlocked;
        _proxy.Dispose();
        _proxy = null;
        if (_firewallOn) { RemoveFirewallRules(); _firewallOn = false; }
        // Ne pas toucher à une coupure d'Internet en cours (elle gère elle-même la restauration).
        if (!InternetBlocker.BlockedByProxy && SystemProxy.IsApplied(server)) SystemProxy.Restore();
    }

    /// <summary>Vérifie que le poste passe toujours par le proxy de filtrage ; sinon, le rétablit.</summary>
    private void Watch()
    {
        bool tampered = false;
        lock (_lock)
        {
            if (_proxy == null || InternetBlocker.BlockedByProxy) return;
            if (!SystemProxy.IsApplied(Server))
            {
                SystemProxy.Apply(Server);
                tampered = true;
            }
        }
        if (tampered && DateTime.UtcNow - _lastTamperNotice > TimeSpan.FromSeconds(30))
        {
            _lastTamperNotice = DateTime.UtcNow;
            Notify?.Invoke("Filtrage web", "Les réglages réseau ont été rétablis : le filtrage web du professeur est actif.");
        }
    }

    private void OnBlocked(string host)
    {
        var now = DateTime.UtcNow;
        bool report;
        lock (_lastReported)
        {
            // Un même site n'est signalé au professeur qu'une fois toutes les 2 minutes.
            report = !_lastReported.TryGetValue(host, out var last) || now - last > TimeSpan.FromMinutes(2);
            if (report) _lastReported[host] = now;
            if (_lastReported.Count > 500) _lastReported.Clear();
        }
        if (report) SiteBlocked?.Invoke(host);
        if (now - _lastStudentNotice > TimeSpan.FromSeconds(15))
        {
            _lastStudentNotice = now;
            Notify?.Invoke("Site bloqué", $"« {host} » n'est pas autorisé pendant le cours.");
        }
    }

    // ---------- Pare-feu (droits administrateur) ----------

    /// <summary>Chemins des navigateurs installés ou ouverts sur le poste.</summary>
    private static IEnumerable<string> BrowserPaths()
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        string pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string[] candidates =
        {
            @"Google\Chrome\Application\chrome.exe", @"Microsoft\Edge\Application\msedge.exe",
            @"Mozilla Firefox\firefox.exe", @"BraveSoftware\Brave-Browser\Application\brave.exe",
            @"Vivaldi\Application\vivaldi.exe", @"Opera\opera.exe", @"Internet Explorer\iexplore.exe",
            @"Chromium\Application\chrome.exe",
        };
        foreach (var root in new[] { pf, pf86, local })
            foreach (var c in candidates)
            {
                if (string.IsNullOrEmpty(root)) continue;
                var p = Path.Combine(root, c);
                if (File.Exists(p)) found.Add(p);
            }
        foreach (var c in new[] { @"Programs\Opera\opera.exe", @"Programs\Opera GX\opera.exe", @"Yandex\YandexBrowser\Application\browser.exe" })
        {
            var p = Path.Combine(local, c);
            if (File.Exists(p)) found.Add(p);
        }
        foreach (var proc in Process.GetProcesses())
        {
            try
            {
                if (BrowserExeNames.Contains(proc.ProcessName, StringComparer.OrdinalIgnoreCase))
                {
                    var path = Native.ProcessPath(proc.Id);
                    if (!string.IsNullOrEmpty(path)) found.Add(path);
                }
            }
            catch { }
            finally { proc.Dispose(); }
        }
        return found;
    }

    private static bool AddFirewallRules()
    {
        RemoveFirewallRules();
        bool any = false;
        foreach (var exe in BrowserPaths())
        {
            any |= InternetBlocker.Netsh($"advfirewall firewall add rule name=\"{FirewallRule}\" dir=out action=block enable=yes profile=any program=\"{exe}\" protocol=TCP remoteport=80,443 remoteip={PublicIps}");
            any |= InternetBlocker.Netsh($"advfirewall firewall add rule name=\"{FirewallRule}\" dir=out action=block enable=yes profile=any program=\"{exe}\" protocol=UDP remoteport=443 remoteip={PublicIps}");
        }
        return any;
    }

    private static void RemoveFirewallRules() =>
        InternetBlocker.Netsh($"advfirewall firewall delete rule name=\"{FirewallRule}\"");

    /// <summary>Au démarrage : retire les règles restées en place après un arrêt brutal de l'agent.</summary>
    public static void RecoverAtStartup()
    {
        if (InternetBlocker.IsElevated) RemoveFirewallRules();
    }

    public void Dispose()
    {
        lock (_lock) StopLocked();
        _watchdog.Dispose();
    }
}
