using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace ClasseControle.Agent;

/// <summary>
/// Coupe l'accès à Internet du poste élève tout en laissant le réseau local
/// (et donc la console du professeur) joignable.
///
///  • Si l'agent a les droits administrateur : règle sortante du Pare-feu
///    Windows qui bloque toutes les adresses publiques (IPv4 et IPv6) —
///    efficace pour TOUTES les applications.
///  • Sinon : proxy système pointé vers une adresse morte (navigateurs, Office,
///    Teams, Store… respectent ce réglage), sans toucher au réseau local.
///
/// L'état est mémorisé : si l'agent est arrêté brutalement pendant une coupure,
/// Internet est rétabli à son prochain démarrage.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class InternetBlocker
{
    private const string RuleName = "Classe_controle - Internet coupé";

    // Toutes les adresses IPv4 publiques (on exclut 10/8, 127/8, 169.254/16, 172.16/12, 192.168/16, multicast).
    private const string PublicIpv4 =
        "1.0.0.0-9.255.255.255,11.0.0.0-126.255.255.255,128.0.0.0-169.253.255.255," +
        "169.255.0.0-172.15.255.255,172.32.0.0-192.167.255.255,192.169.0.0-223.255.255.255";
    private const string PublicIpv6 = "2000::-3fff:ffff:ffff:ffff:ffff:ffff:ffff:ffff";

    public static bool IsBlocked { get; private set; }

    /// <summary>Méthode utilisée pour la dernière coupure (affichage).</summary>
    public static string Method { get; private set; } = "";

    public static bool IsElevated
    {
        get
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }
    }

    public static void Block()
    {
        if (IsBlocked) return;
        if (IsElevated && Netsh($"advfirewall firewall add rule name=\"{RuleName}\" dir=out action=block enable=yes profile=any remoteip={PublicIpv4},{PublicIpv6}"))
            Method = "pare-feu";
        else
        {
            BlockWithProxy();
            Method = "proxy";
        }
        IsBlocked = true;
    }

    public static void Unblock()
    {
        // On nettoie les deux méthodes : sûr même si l'état a été perdu.
        Netsh($"advfirewall firewall delete rule name=\"{RuleName}\"");
        IsBlocked = false;   // avant RestoreProxy : le filtrage web reprend alors la main tout de suite
        RestoreProxy();
    }

    /// <summary>Au démarrage de l'agent : rétablit Internet si une coupure était restée active.</summary>
    public static void RecoverAtStartup()
    {
        try
        {
            if (SystemProxy.HasBackup) SystemProxy.Restore();
        }
        catch { }
        if (IsElevated) Netsh($"advfirewall firewall delete rule name=\"{RuleName}\"");
    }

    // ---------- Méthode « proxy » (sans droits administrateur) ----------
    /// <summary>Adresse morte : port « discard », aucune réponse.</summary>
    public const string DeadProxy = "127.0.0.1:9";

    private static void BlockWithProxy() => SystemProxy.Apply(DeadProxy);

    /// <summary>Levée de la coupure : le filtrage web, s'il est actif, reprend la main sur le proxy.</summary>
    private static void RestoreProxy()
    {
        SystemProxy.Restore();
        try { Unblocked?.Invoke(); } catch { }
    }

    /// <summary>Internet vient d'être rétabli (le filtrage web réapplique alors son proxy).</summary>
    public static event Action? Unblocked;

    /// <summary>Coupure en cours par la méthode « proxy » (le filtrage web ne doit pas l'écraser).</summary>
    public static bool BlockedByProxy => IsBlocked && Method == "proxy";

    internal static bool Netsh(string args) => NetshImpl(args);

    private static bool NetshImpl(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("netsh.exe", args)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
            });
            if (p == null) return false;
            p.WaitForExit(8000);
            return p.ExitCode == 0;
        }
        catch { return false; }
    }
}
