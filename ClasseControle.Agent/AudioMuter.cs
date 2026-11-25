using System.Diagnostics;
using System.Runtime.Versioning;
using NAudio.CoreAudioApi;

namespace ClasseControle.Agent;

/// <summary>
/// Coupe le son des applications de l'élève (navigateur, lecteurs, jeux…), sur toutes
/// les sorties audio, SANS couper l'agent : les annonces du professeur restent audibles.
/// Les applications qui démarrent ensuite sont coupées aussi (vérification chaque seconde),
/// et un élève qui réactive le son d'une application le voit recoupé.
/// Au rétablissement, seules les sessions coupées par l'agent sont réactivées.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class AudioMuter
{
    private static readonly object Gate = new();
    private static readonly HashSet<string> MutedByUs = new();
    private static readonly uint OwnPid = (uint)Environment.ProcessId;
    private static Timer? _timer;

    public static bool IsMuted { get; private set; }

    public static void SetMuted(bool muted)
    {
        lock (Gate)
        {
            IsMuted = muted;
            if (muted)
            {
                _timer ??= new Timer(_ => { try { Enforce(); } catch { } }, null, Timeout.Infinite, Timeout.Infinite);
                _timer.Change(0, 1000);
            }
            else
            {
                _timer?.Change(Timeout.Infinite, Timeout.Infinite);
                try { Restore(); } catch { }
            }
        }
    }

    private static void Enforce()
    {
        lock (Gate)
        {
            if (!IsMuted) return;
            ForEachSession((id, pid, s) =>
            {
                if (pid == OwnPid) return;
                if (!s.SimpleAudioVolume.Mute)
                {
                    s.SimpleAudioVolume.Mute = true;
                    MutedByUs.Add(id);
                }
            });
        }
    }

    private static void Restore()
    {
        ForEachSession((id, _, s) =>
        {
            if (MutedByUs.Remove(id)) s.SimpleAudioVolume.Mute = false;
        });
        MutedByUs.Clear();
    }

    private static void ForEachSession(Action<string, uint, AudioSessionControl> action)
    {
        using var enumerator = new MMDeviceEnumerator();
        foreach (var device in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
        {
            try
            {
                var manager = device.AudioSessionManager;
                manager.RefreshSessions();
                var sessions = manager.Sessions;
                for (int i = 0; i < sessions.Count; i++)
                {
                    try
                    {
                        var s = sessions[i];
                        action(device.ID + "|" + s.GetSessionInstanceIdentifier, s.GetProcessID, s);
                    }
                    catch { }
                }
            }
            catch { }
            finally { device.Dispose(); }
        }
    }
}
