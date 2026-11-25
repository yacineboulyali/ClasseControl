using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Windows.Devices.Radios;

namespace ClasseControle.Agent;

/// <summary>
/// Verrouillage du Wi-Fi et du Bluetooth demandé par le professeur.
/// Au verrouillage, l'état des radios (Wi-Fi, Bluetooth : allumé / éteint) et le réseau
/// Wi-Fi connecté sont mémorisés. Ensuite, toutes les 2 secondes :
///   • une radio allumée ou éteinte par l'élève (paramètres, centre de notifications, mode
///     avion…) est remise dans son état ;
///   • si l'élève passe sur un autre réseau Wi-Fi (partage de connexion du téléphone…),
///     le poste est reconnecté au réseau de la salle ;
///   • l'autorisation Windows « laisser les applications contrôler les radios » est maintenue.
/// Fonctionne dans la session de l'élève, sans droits administrateur.
/// </summary>
[SupportedOSPlatform("windows10.0.17763.0")]
internal sealed class NetLockEnforcer : IDisposable
{
    private const string ConsentKey = @"Software\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\radios";

    private readonly IAgentHost _host;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Dictionary<RadioKind, RadioState> _locked = new();
    private Timer? _timer;
    private string? _lockedSsid;
    private string? _previousConsent;
    private DateTime _lastNotice = DateTime.MinValue;

    public bool IsLocked { get; private set; }

    public NetLockEnforcer(IAgentHost host) => _host = host;

    public async Task SetLockedAsync(bool locked)
    {
        await _gate.WaitAsync();
        try
        {
            if (locked == IsLocked) return;
            IsLocked = locked;
            if (locked)
            {
                _previousConsent = ReadConsent();
                WriteConsent("Allow");
                _locked.Clear();
                try
                {
                    await Radio.RequestAccessAsync();
                    foreach (var r in await Radio.GetRadiosAsync())
                        if (r.Kind is RadioKind.WiFi or RadioKind.Bluetooth) _locked[r.Kind] = r.State;
                }
                catch { }
                _lockedSsid = CurrentSsid();
                _timer = new Timer(_ => _ = EnforceAsync(), null, 2000, 2000);
            }
            else
            {
                _timer?.Dispose();
                _timer = null;
                if (_previousConsent != null) WriteConsent(_previousConsent);
            }
        }
        finally { _gate.Release(); }
    }

    private async Task EnforceAsync()
    {
        if (!await _gate.WaitAsync(0)) return;
        try
        {
            if (!IsLocked) return;
            if (ReadConsent() != "Allow") WriteConsent("Allow");
            var changed = new List<string>();
            try
            {
                foreach (var r in await Radio.GetRadiosAsync())
                {
                    if (!_locked.TryGetValue(r.Kind, out var want) || r.State == want) continue;
                    if (r.State == RadioState.Disabled) continue; // radio coupée matériellement : rien à faire
                    var res = await r.SetStateAsync(want);
                    if (res == RadioAccessStatus.Allowed)
                        changed.Add((r.Kind == RadioKind.WiFi ? "Wi-Fi" : "Bluetooth") + (want == RadioState.On ? " rallumé" : " éteint"));
                }
            }
            catch { }

            // Retour sur le réseau Wi-Fi de la salle si l'élève en a changé.
            if (_lockedSsid != null && (!_locked.TryGetValue(RadioKind.WiFi, out var wifi) || wifi == RadioState.On))
            {
                var now = CurrentSsid();
                if (now != null && !string.Equals(now, _lockedSsid, StringComparison.Ordinal))
                {
                    Netsh($"wlan connect name=\"{_lockedSsid.Replace("\"", "")}\"");
                    changed.Add($"reconnecté au réseau « {_lockedSsid} »");
                }
            }

            if (changed.Count > 0 && DateTime.UtcNow - _lastNotice > TimeSpan.FromSeconds(8))
            {
                _lastNotice = DateTime.UtcNow;
                _host.ShowMessage("Réseau verrouillé",
                    "Le professeur a verrouillé le Wi-Fi et le Bluetooth de ce poste.\n" + string.Join(", ", changed) + ".", 5);
            }
        }
        finally { _gate.Release(); }
    }

    /// <summary>Nom (SSID) du réseau Wi-Fi connecté, ou null.</summary>
    private static string? CurrentSsid()
    {
        string output = Netsh("wlan show interfaces");
        foreach (var raw in output.Split('\n'))
        {
            var line = raw.Trim();
            // « SSID : nom » (le libellé est le même dans toutes les langues ; on écarte « BSSID »).
            if (!line.StartsWith("SSID", StringComparison.OrdinalIgnoreCase)) continue;
            int colon = line.IndexOf(':');
            if (colon < 0) continue;
            var ssid = line[(colon + 1)..].Trim();
            return ssid.Length > 0 ? ssid : null;
        }
        return null;
    }

    private static string Netsh(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("netsh.exe", args)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
            });
            if (p == null) return "";
            string o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(5000);
            return o;
        }
        catch { return ""; }
    }

    private static string? ReadConsent()
    {
        try { using var k = Registry.CurrentUser.OpenSubKey(ConsentKey); return k?.GetValue("Value") as string; }
        catch { return null; }
    }

    private static void WriteConsent(string value)
    {
        try { using var k = Registry.CurrentUser.CreateSubKey(ConsentKey); k.SetValue("Value", value); }
        catch { }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        if (IsLocked && _previousConsent != null) WriteConsent(_previousConsent);
    }
}
