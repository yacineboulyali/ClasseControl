using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using ClasseControle.Common;

namespace ClasseControle.Console;

/// <summary>
/// Découverte des postes élèves par broadcast UDP. Envoie une sonde sur toutes
/// les interfaces et collecte les réponses des agents pendant un court instant.
/// </summary>
internal static class Discovery
{
    public const string DiscoveryMagic = "CLASSECTRL_DISCOVER_V1";

    public static async Task<List<DiscoveryReply>> ScanAsync(string roomCode, int timeoutMs = 1500)
    {
        var found = new Dictionary<string, DiscoveryReply>(); // clé = ip:port
        using var udp = new UdpClient();
        udp.EnableBroadcast = true;
        udp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        udp.Client.Bind(new IPEndPoint(IPAddress.Any, 0));

        byte[] probe = Encoding.UTF8.GetBytes($"{DiscoveryMagic}|{roomCode}");

        // Envoi sur le broadcast global + broadcasts dirigés de chaque interface.
        var targets = new List<IPEndPoint> { new(IPAddress.Broadcast, Net.DiscoveryPort) };
        targets.AddRange(DirectedBroadcasts().Select(ip => new IPEndPoint(ip, Net.DiscoveryPort)));
        foreach (var ep in targets)
        {
            try { await udp.SendAsync(probe, probe.Length, ep); } catch { }
        }

        // Collecte des réponses jusqu'au timeout.
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            while (!cts.IsCancellationRequested)
            {
                var res = await udp.ReceiveAsync(cts.Token);
                try
                {
                    var reply = JsonSerializer.Deserialize<DiscoveryReply>(res.Buffer);
                    if (reply == null) continue;
                    if (string.IsNullOrWhiteSpace(reply.IpAddress) || reply.IpAddress == "?")
                        reply.IpAddress = res.RemoteEndPoint.Address.ToString();
                    found[$"{reply.IpAddress}:{reply.ControlPort}"] = reply;
                }
                catch { }
            }
        }
        catch (OperationCanceledException) { }

        return found.Values.OrderBy(r => r.MachineName).ToList();
    }

    private static IEnumerable<IPAddress> DirectedBroadcasts()
    {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up) continue;
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var ip = ua.Address.GetAddressBytes();
                var mask = ua.IPv4Mask?.GetAddressBytes();
                if (mask == null || mask.Length != 4) continue;
                var bc = new byte[4];
                for (int i = 0; i < 4; i++) bc[i] = (byte)(ip[i] | (mask[i] ^ 0xFF));
                yield return new IPAddress(bc);
            }
        }
    }
}
