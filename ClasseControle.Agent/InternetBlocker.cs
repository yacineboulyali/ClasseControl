using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Principal;
using Microsoft.Win32;

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
    private const string SettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const string BackupKey = @"Software\Classe_controle\ProxyBackup";

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
        RestoreProxy();
        IsBlocked = false;
    }

    /// <summary>Au démarrage de l'agent : rétablit Internet si une coupure était restée active.</summary>
    public static void RecoverAtStartup()
    {
        try
        {
            using var backup = Registry.CurrentUser.OpenSubKey(BackupKey);
            if (backup != null) RestoreProxy();
        }
        catch { }
        if (IsElevated) Netsh($"advfirewall firewall delete rule name=\"{RuleName}\"");
    }

    // ---------- Méthode « proxy » (sans droits administrateur) ----------
    private static void BlockWithProxy()
    {
        using var settings = Registry.CurrentUser.CreateSubKey(SettingsKey);
        using (var backup = Registry.CurrentUser.CreateSubKey(BackupKey))
        {
            // Sauvegarde unique des réglages de l'élève, restaurés à la levée.
            if (backup.GetValue("Saved") == null)
            {
                backup.SetValue("ProxyEnable", settings.GetValue("ProxyEnable", 0) ?? 0, RegistryValueKind.DWord);
                backup.SetValue("ProxyServer", settings.GetValue("ProxyServer", "") ?? "");
                backup.SetValue("ProxyOverride", settings.GetValue("ProxyOverride", "") ?? "");
                backup.SetValue("AutoConfigURL", settings.GetValue("AutoConfigURL", "") ?? "");
                backup.SetValue("Saved", 1, RegistryValueKind.DWord);
            }
        }
        settings.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
        settings.SetValue("ProxyServer", "127.0.0.1:9");  // port « discard » : aucune réponse
        settings.SetValue("ProxyOverride", "<local>;10.*;192.168.*;169.254.*;" + string.Join(";", Enumerable.Range(16, 16).Select(i => $"172.{i}.*")));
        settings.DeleteValue("AutoConfigURL", throwOnMissingValue: false);
        NotifySettingsChanged();
    }

    private static void RestoreProxy()
    {
        try
        {
            using var backup = Registry.CurrentUser.OpenSubKey(BackupKey);
            if (backup == null) return;
            using var settings = Registry.CurrentUser.CreateSubKey(SettingsKey);
            settings.SetValue("ProxyEnable", backup.GetValue("ProxyEnable", 0) ?? 0, RegistryValueKind.DWord);
            Restore(settings, backup, "ProxyServer");
            Restore(settings, backup, "ProxyOverride");
            Restore(settings, backup, "AutoConfigURL");
            backup.Close();
            Registry.CurrentUser.DeleteSubKeyTree(BackupKey, throwOnMissingSubKey: false);
            NotifySettingsChanged();
        }
        catch { }
    }

    private static void Restore(RegistryKey settings, RegistryKey backup, string name)
    {
        string v = backup.GetValue(name, "") as string ?? "";
        if (v.Length > 0) settings.SetValue(name, v);
        else settings.DeleteValue(name, throwOnMissingValue: false);
    }

    [DllImport("wininet.dll", SetLastError = true)]
    private static extern bool InternetSetOption(IntPtr hInternet, int option, IntPtr buffer, int length);

    /// <summary>Prévient Windows et les navigateurs ouverts que les réglages proxy ont changé.</summary>
    private static void NotifySettingsChanged()
    {
        const int INTERNET_OPTION_SETTINGS_CHANGED = 39, INTERNET_OPTION_REFRESH = 37;
        try
        {
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
        }
        catch { }
    }

    private static bool Netsh(string args)
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
