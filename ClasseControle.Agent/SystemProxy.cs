using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32;

namespace ClasseControle.Agent;

/// <summary>
/// Réglages proxy de Windows pour l'utilisateur (ceux que suivent Edge, Chrome, Firefox
/// en mode « proxy système », Office, Teams, le Store…). Utilisé par « Couper Internet »
/// (proxy mort) et par le filtrage web (proxy de filtrage local).
///
/// Les réglages d'origine de l'élève sont sauvegardés UNE fois, puis restaurés à la levée,
/// à l'arrêt de l'agent, ou au démarrage suivant si l'agent a été arrêté brutalement.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class SystemProxy
{
    private const string SettingsKey = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
    private const string BackupKey = @"Software\Classe_controle\ProxyBackup";
    private static readonly object Gate = new();

    /// <summary>Le réseau de la salle ne passe jamais par le proxy.</summary>
    public static readonly string LocalBypass =
        "<local>;localhost;127.*;10.*;192.168.*;169.254.*;" + string.Join(";", Enumerable.Range(16, 16).Select(i => $"172.{i}.*"));

    public static bool HasBackup
    {
        get
        {
            try { using var b = Registry.CurrentUser.OpenSubKey(BackupKey); return b != null; }
            catch { return false; }
        }
    }

    /// <summary>Dirige tout le trafic web de l'utilisateur vers <paramref name="server"/> (« hôte:port »).</summary>
    public static void Apply(string server)
    {
        lock (Gate)
        {
            using var settings = Registry.CurrentUser.CreateSubKey(SettingsKey);
            using (var backup = Registry.CurrentUser.CreateSubKey(BackupKey))
            {
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
            settings.SetValue("ProxyServer", server);
            settings.SetValue("ProxyOverride", LocalBypass);
            // Un script de configuration automatique (PAC) passerait avant le proxy : on le retire.
            settings.DeleteValue("AutoConfigURL", throwOnMissingValue: false);
        }
        NotifyChanged();
    }

    /// <summary>Vrai si les réglages actuels pointent bien vers <paramref name="server"/>.</summary>
    public static bool IsApplied(string server)
    {
        try
        {
            using var s = Registry.CurrentUser.OpenSubKey(SettingsKey);
            if (s == null) return false;
            return Convert.ToInt32(s.GetValue("ProxyEnable", 0)) == 1
                   && string.Equals(s.GetValue("ProxyServer", "") as string, server, StringComparison.OrdinalIgnoreCase)
                   && string.IsNullOrEmpty(s.GetValue("AutoConfigURL", "") as string);
        }
        catch { return false; }
    }

    /// <summary>Remet les réglages d'origine de l'élève (sans effet s'il n'y a pas de sauvegarde).</summary>
    public static void Restore()
    {
        lock (Gate)
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
            }
            catch { }
        }
        NotifyChanged();
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
    public static void NotifyChanged()
    {
        const int INTERNET_OPTION_SETTINGS_CHANGED = 39, INTERNET_OPTION_REFRESH = 37;
        try
        {
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
        }
        catch { }
    }
}
