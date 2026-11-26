using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ClasseControle.Common;

/// <summary>
/// Règles du filtrage web par NOM DE DOMAINE (et non plus seulement par titre d'onglet).
///
/// Une entrée de liste peut être :
///  • un domaine (« youtube.com », « *.tiktok.com », « https://fr.wikipedia.org/wiki/… ») :
///    le domaine ET tous ses sous-domaines sont concernés (m.youtube.com, www.youtube.com…) ;
///  • un mot-clé sans point (« youtube », « prime video ») : tout nom de domaine qui le
///    contient est concerné (espaces ignorés) ;
///  • une catégorie « @… » : développée avec ses domaines (voir <see cref="RestrictionCatalog"/>).
///
/// Ce filtrage fonctionne aussi en HTTPS sans déchiffrer le trafic : le navigateur
/// annonce le nom du site au proxy (requête CONNECT) avant d'ouvrir la connexion chiffrée.
/// </summary>
public sealed class WebFilterRules
{
    private readonly WebMode _mode;
    private readonly List<string> _blockedDomains = new(), _blockedWords = new();
    private readonly List<string> _allowedDomains = new(), _allowedWords = new();

    /// <summary>
    /// Domaines techniques toujours autorisés en mode « sites approuvés » : vérification
    /// des certificats (sinon aucun site HTTPS ne s'ouvre correctement) et mises à jour.
    /// </summary>
    public static readonly string[] Essentials =
    {
        "digicert.com", "pki.goog", "lencr.org", "sectigo.com", "usertrust.com", "globalsign.com",
        "globalsign.net", "entrust.net", "godaddy.com", "amazontrust.com", "identrust.com", "verisign.com",
        "symcd.com", "comodoca.com", "msocsp.com", "windowsupdate.com",
    };

    /// <summary>
    /// Domaines « compagnons » ajoutés automatiquement quand un site est approuvé :
    /// sans eux, la page s'ouvre mais sans images, vidéos ni mise en forme.
    /// </summary>
    public static readonly Dictionary<string, string[]> Companions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["wikipedia"] = new[] { "wikimedia.org", "wikipedia.org", "wikidata.org", "wiktionary.org" },
        ["youtube"] = new[] { "youtube.com", "ytimg.com", "googlevideo.com", "ggpht.com", "youtube-nocookie.com", "gstatic.com" },
        ["google"] = new[] { "google.com", "gstatic.com", "googleapis.com", "googleusercontent.com", "google.co.ma" },
        ["gmail"] = new[] { "google.com", "gstatic.com", "googleapis.com", "googleusercontent.com" },
        ["classroom"] = new[] { "google.com", "gstatic.com", "googleapis.com", "googleusercontent.com" },
        ["office"] = new[] { "office.com", "office.net", "microsoft.com", "microsoftonline.com", "live.com", "msauth.net", "msftauth.net", "sharepoint.com", "officeapps.live.com" },
        ["microsoft"] = new[] { "microsoft.com", "microsoftonline.com", "live.com", "msauth.net", "msftauth.net", "office.net" },
        ["teams"] = new[] { "teams.microsoft.com", "microsoft.com", "microsoftonline.com", "skype.com", "office.net", "msauth.net", "msftauth.net" },
        ["github"] = new[] { "github.com", "githubusercontent.com", "githubassets.com" },
        ["khanacademy"] = new[] { "khanacademy.org", "kastatic.org", "kasandbox.org" },
        ["scratch"] = new[] { "scratch.mit.edu", "mit.edu" },
        ["moodle"] = new[] { "moodle.org", "moodle.com", "moodlecloud.com" },
    };

    public WebFilterRules(WebMode mode, IEnumerable<string>? blocked, IEnumerable<string>? allowed)
    {
        _mode = mode;
        Split(RestrictionCatalog.ExpandDomains(blocked ?? Enumerable.Empty<string>()), _blockedDomains, _blockedWords);
        var allow = RestrictionCatalog.ExpandDomains(allowed ?? Enumerable.Empty<string>()).ToList();
        foreach (var a in allow.ToList())
        {
            string key = TitleKeyword(a);
            if (Companions.TryGetValue(key, out var extra)) allow.AddRange(extra);
        }
        allow.AddRange(Essentials);
        Split(allow, _allowedDomains, _allowedWords);
    }

    public WebMode Mode => _mode;

    /// <summary>Le filtrage par domaine a-t-il quelque chose à faire dans ce mode ?</summary>
    public bool IsActive => _mode switch
    {
        WebMode.BlockList => _blockedDomains.Count + _blockedWords.Count > 0,
        WebMode.AllowList or WebMode.BlockAll => true,
        _ => false,
    };

    /// <summary>Vrai si le site <paramref name="host"/> peut être ouvert.</summary>
    public bool IsAllowed(string host)
    {
        host = NormalizeHost(host);
        if (host.Length == 0) return false;
        if (IsLocal(host)) return true;   // réseau de la salle, serveur Moodle local, console…
        return _mode switch
        {
            WebMode.Unrestricted => true,
            WebMode.BlockList => !Matches(host, _blockedDomains, _blockedWords),
            WebMode.AllowList => Matches(host, _allowedDomains, _allowedWords),
            WebMode.BlockAll => false,
            _ => true,
        };
    }

    private static bool Matches(string host, List<string> domains, List<string> words)
    {
        foreach (var d in domains)
            if (host == d || host.EndsWith("." + d, StringComparison.Ordinal)) return true;
        foreach (var w in words)
            if (host.Contains(w, StringComparison.Ordinal)) return true;
        return false;
    }

    private static void Split(IEnumerable<string> entries, List<string> domains, List<string> words)
    {
        foreach (var raw in entries)
        {
            string e = NormalizeEntry(raw);
            if (e.Length == 0) continue;
            var target = e.Contains('.') ? domains : words;
            if (!target.Contains(e)) target.Add(e);
        }
    }

    /// <summary>
    /// « https://www.YouTube.com/watch?v=1 » → « youtube.com » ; « *.tiktok.com » → « tiktok.com » ;
    /// « Prime Video » → « primevideo ».
    /// </summary>
    public static string NormalizeEntry(string raw)
    {
        string e = (raw ?? "").Trim().ToLowerInvariant();
        int scheme = e.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) e = e[(scheme + 3)..];
        int cut = e.IndexOfAny(new[] { '/', '?', '#' });
        if (cut >= 0) e = e[..cut];
        int at = e.LastIndexOf('@');
        if (at >= 0) e = e[(at + 1)..];
        if (e.StartsWith('[')) return "";               // adresse IPv6 littérale : ignorée
        int colon = e.IndexOf(':');
        if (colon >= 0) e = e[..colon];
        e = e.TrimStart('*', '.').TrimEnd('.');
        if (e.StartsWith("www.", StringComparison.Ordinal)) e = e[4..];
        return e.Replace(" ", "");
    }

    public static string NormalizeHost(string host)
    {
        string h = (host ?? "").Trim().ToLowerInvariant().TrimEnd('.');
        if (h.StartsWith('[') && h.EndsWith(']')) h = h[1..^1];
        return h;
    }

    /// <summary>
    /// Mot-clé à chercher dans le titre de l'onglet pour une entrée de liste :
    /// « fr.wikipedia.org » → « wikipedia », « youtube » → « youtube ».
    /// </summary>
    public static string TitleKeyword(string entry)
    {
        string e = NormalizeEntry(entry);
        if (!e.Contains('.')) return e;
        var parts = e.Split('.', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 1) return parts[0];
        // Retire l'extension (.com, .org, .co.ma…) et garde le libellé principal.
        int main = parts.Length - 2;
        if (main > 0 && parts[main].Length <= 3 && parts[^1].Length == 2) main--;   // co.ma, gov.ma, ac.ma…
        return parts[main];
    }

    /// <summary>Adresses du réseau local (jamais filtrées) : localhost, IP privées, noms sans point.</summary>
    public static bool IsLocal(string host)
    {
        if (host == "localhost" || host.EndsWith(".local", StringComparison.Ordinal) || host.EndsWith(".lan", StringComparison.Ordinal)) return true;
        if (!host.Contains('.') && !host.Contains(':')) return true;   // nom NetBIOS du poste du prof, d'un serveur…
        if (!IPAddress.TryParse(host, out var ip)) return false;
        if (IPAddress.IsLoopback(ip)) return true;
        if (ip.AddressFamily == AddressFamily.InterNetworkV6)
            return ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal || (ip.GetAddressBytes()[0] & 0xFE) == 0xFC;
        var b = ip.GetAddressBytes();
        return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
    }
}

/// <summary>
/// Proxy HTTP/HTTPS local (127.0.0.1) qui applique <see cref="WebFilterRules"/>.
/// Le poste élève est configuré pour faire passer les navigateurs par ce proxy :
///  • HTTPS : la requête CONNECT donne le nom du site ; autorisé → tunnel transparent
///    (le trafic reste chiffré de bout en bout), interdit → refus 403 ;
///  • HTTP : la requête est relayée, ou remplacée par une page « Site bloqué ».
/// Aucun déchiffrement, aucun certificat à installer.
/// </summary>
public sealed class WebFilterProxy : IDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private volatile WebFilterRules _rules;

    /// <summary>Un site a été refusé (nom de domaine).</summary>
    public event Action<string>? Blocked;

    public int Port { get; }

    public WebFilterProxy(WebFilterRules rules, int port = 0)
    {
        _rules = rules;
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start(256);
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _ = Task.Run(AcceptLoop);
    }

    public WebFilterRules Rules
    {
        get => _rules;
        set => _rules = value;
    }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(_cts.Token); }
            catch { if (_cts.IsCancellationRequested) return; continue; }
            _ = Task.Run(() => Handle(client));
        }
    }

    private async Task Handle(TcpClient client)
    {
        using var _c = client;
        client.NoDelay = true;
        var cs = client.GetStream();
        TcpClient? upstream = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            var (head, extra) = await ReadHead(cs, timeout.Token);
            if (head == null) return;

            int eol = head.IndexOf("\r\n", StringComparison.Ordinal);
            string requestLine = eol >= 0 ? head[..eol] : head;
            var parts = requestLine.Split(' ');
            if (parts.Length < 3) { await Reply(cs, 400, "Bad Request", ""); return; }
            string method = parts[0].ToUpperInvariant(), target = parts[1], version = parts[2];

            string host; int port;
            if (method == "CONNECT")
            {
                (host, port) = SplitHostPort(target, 443);
            }
            else
            {
                if (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttp)
                {
                    // Requête directe sur le proxy (pas une requête de proxy) : on la refuse.
                    await Reply(cs, 400, "Bad Request", "");
                    return;
                }
                host = uri.IdnHost; port = uri.Port;
                target = uri.PathAndQuery;
            }

            if (!_rules.IsAllowed(host))
            {
                try { Blocked?.Invoke(WebFilterRules.NormalizeHost(host)); } catch { }
                await Reply(cs, 403, "Forbidden", method == "CONNECT" ? "" : BlockPage(host));
                return;
            }

            upstream = new TcpClient { NoDelay = true };
            try { await upstream.ConnectAsync(host, port, timeout.Token); }
            catch { await Reply(cs, 502, "Bad Gateway", ""); return; }
            var us = upstream.GetStream();

            if (method == "CONNECT")
            {
                await WriteAscii(cs, "HTTP/1.1 200 Connection established\r\n\r\n", _cts.Token);
                if (extra.Length > 0) await us.WriteAsync(extra, _cts.Token);
            }
            else
            {
                // Requête relayée en « origin-form », une requête par connexion (Connection: close).
                var sb = new StringBuilder();
                sb.Append(method).Append(' ').Append(target).Append(' ').Append(version).Append("\r\n");
                foreach (var line in head[(eol + 2)..].Split("\r\n"))
                {
                    if (line.Length == 0) continue;
                    int c = line.IndexOf(':');
                    string name = c > 0 ? line[..c].Trim() : "";
                    if (name.Equals("Proxy-Connection", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("Connection", StringComparison.OrdinalIgnoreCase)
                        || name.Equals("Keep-Alive", StringComparison.OrdinalIgnoreCase)) continue;
                    sb.Append(line).Append("\r\n");
                }
                sb.Append("Connection: close\r\n\r\n");
                await WriteAscii(us, sb.ToString(), _cts.Token);
                if (extra.Length > 0) await us.WriteAsync(extra, _cts.Token);
            }

            // Échange dans les deux sens jusqu'à la fermeture d'un côté.
            var up = Pump(cs, us);
            var down = Pump(us, cs);
            await Task.WhenAny(up, down);
        }
        catch { }
        finally { upstream?.Dispose(); }
    }

    private async Task Pump(NetworkStream from, NetworkStream to)
    {
        var buf = new byte[32 * 1024];
        try
        {
            int n;
            while ((n = await from.ReadAsync(buf, _cts.Token)) > 0)
                await to.WriteAsync(buf.AsMemory(0, n), _cts.Token);
        }
        catch { }
        try { to.Socket.Shutdown(SocketShutdown.Send); } catch { }
    }

    /// <summary>Lit l'en-tête de la requête (jusqu'à la ligne vide) ; renvoie aussi les octets lus au-delà.</summary>
    private static async Task<(string? head, byte[] extra)> ReadHead(NetworkStream s, CancellationToken ct)
    {
        var buf = new byte[16 * 1024];
        int len = 0;
        while (len < buf.Length)
        {
            int n = await s.ReadAsync(buf.AsMemory(len), ct);
            if (n <= 0) return (null, Array.Empty<byte>());
            len += n;
            int end = IndexOf(buf, len, "\r\n\r\n"u8);
            if (end >= 0)
            {
                string head = Encoding.Latin1.GetString(buf, 0, end);
                return (head, buf.AsSpan(end + 4, len - end - 4).ToArray());
            }
        }
        return (null, Array.Empty<byte>());
    }

    private static int IndexOf(byte[] buf, int len, ReadOnlySpan<byte> pattern) =>
        buf.AsSpan(0, len).IndexOf(pattern);

    private static (string host, int port) SplitHostPort(string target, int defaultPort)
    {
        if (target.StartsWith('['))
        {
            int close = target.IndexOf(']');
            string h = close > 0 ? target[1..close] : target;
            int p = close > 0 && close + 2 <= target.Length && int.TryParse(target[(close + 2)..], out var pp) ? pp : defaultPort;
            return (h, p);
        }
        int colon = target.LastIndexOf(':');
        if (colon > 0 && int.TryParse(target[(colon + 1)..], out var port)) return (target[..colon], port);
        return (target, defaultPort);
    }

    private static Task WriteAscii(NetworkStream s, string text, CancellationToken ct) =>
        s.WriteAsync(Encoding.UTF8.GetBytes(text), ct).AsTask();

    private async Task Reply(NetworkStream s, int code, string reason, string html)
    {
        var body = Encoding.UTF8.GetBytes(html);
        string head = $"HTTP/1.1 {code} {reason}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n";
        try
        {
            await WriteAscii(s, head, _cts.Token);
            if (body.Length > 0) await s.WriteAsync(body, _cts.Token);
        }
        catch { }
    }

    private static string BlockPage(string host) =>
        "<!doctype html><html lang=\"fr\"><head><meta charset=\"utf-8\"><title>Site bloqué</title>" +
        "<style>body{font-family:Segoe UI,Arial,sans-serif;background:#f4f5f7;color:#222;display:flex;" +
        "align-items:center;justify-content:center;height:100vh;margin:0}div{background:#fff;padding:40px 48px;" +
        "border-radius:12px;box-shadow:0 4px 18px #0002;max-width:520px;text-align:center}h1{color:#c0392b;margin-top:0}</style>" +
        "</head><body><div><h1>⛔ Site bloqué</h1><p>Le site <b>" + WebUtility.HtmlEncode(host) +
        "</b> n'est pas autorisé pendant le cours.</p><p>Classe_controle</p></div></body></html>";

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
    }
}
