using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;

namespace ClasseControle.Common;

/// <summary>
/// Encodage/décodage des messages sur le fil TCP.
/// Format : [4 octets longueur big-endian][octets JSON UTF-8 de l'Envelope].
/// </summary>
public static class Wire
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>Construit une enveloppe à partir d'un type et d'un payload objet.</summary>
    public static Envelope Build<T>(MessageType type, T payload)
    {
        var json = JsonSerializer.SerializeToElement(payload, JsonOpts);
        return new Envelope { Type = type, Payload = json };
    }

    /// <summary>Désérialise le payload d'une enveloppe vers le type attendu.</summary>
    public static T? Read<T>(Envelope env)
    {
        if (env.Payload is null) return default;
        return env.Payload.Value.Deserialize<T>(JsonOpts);
    }

    public static async Task WriteAsync(Stream stream, Envelope env, CancellationToken ct = default)
    {
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(env, JsonOpts);
        if (body.Length > Net.MaxMessageSize)
            throw new InvalidOperationException($"Message trop volumineux: {body.Length} octets");

        byte[] header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, body.Length);
        await stream.WriteAsync(header, ct);
        await stream.WriteAsync(body, ct);
        await stream.FlushAsync(ct);
    }

    /// <summary>Envoie directement un (type, payload) sur le flux.</summary>
    public static Task SendAsync<T>(Stream stream, MessageType type, T payload, CancellationToken ct = default)
        => WriteAsync(stream, Build(type, payload), ct);

    /// <summary>Lit une enveloppe complète, ou null si le flux est fermé proprement.</summary>
    public static async Task<Envelope?> ReadAsync(Stream stream, CancellationToken ct = default)
    {
        byte[] header = new byte[4];
        if (!await ReadExactAsync(stream, header, ct)) return null;

        int len = BinaryPrimitives.ReadInt32BigEndian(header);
        if (len < 0 || len > Net.MaxMessageSize)
            throw new InvalidDataException($"Longueur de message invalide: {len}");

        byte[] body = new byte[len];
        if (!await ReadExactAsync(stream, body, ct))
            throw new EndOfStreamException("Flux fermé pendant la lecture du corps du message");

        return JsonSerializer.Deserialize<Envelope>(body, JsonOpts);
    }

    private static async Task<bool> ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int offset = 0;
        while (offset < buffer.Length)
        {
            int read = await stream.ReadAsync(buffer.AsMemory(offset), ct);
            if (read == 0)
                return offset == 0 ? false : throw new EndOfStreamException("Flux interrompu");
            offset += read;
        }
        return true;
    }
}
