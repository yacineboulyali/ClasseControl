using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClasseControle.Common;

/// <summary>
/// Constantes réseau partagées entre la console (prof) et l'agent (élève).
/// </summary>
public static class Net
{
    /// <summary>Port UDP utilisé pour la découverte des postes (broadcast).</summary>
    public const int DiscoveryPort = 5401;

    /// <summary>Port TCP utilisé pour le canal de contrôle.</summary>
    public const int ControlPort = 5402;

    /// <summary>Taille maximale d'un message TCP (garde-fou, 64 Mo).</summary>
    public const int MaxMessageSize = 64 * 1024 * 1024;

    /// <summary>Version du protocole. À incrémenter si le format change.</summary>
    public const int ProtocolVersion = 1;
}

/// <summary>Types de messages échangés sur le canal de contrôle TCP.</summary>
public enum MessageType
{
    // --- Prof -> Élève ---
    Hello = 0,            // présentation + authentification (code de salle)
    Lock = 10,            // verrouiller l'écran de l'élève
    Unlock = 11,          // déverrouiller
    ShowMessage = 12,     // afficher un message/consigne
    StartScreen = 20,     // démarrer le flux d'écran (vue/contrôle)
    StopScreen = 21,      // arrêter le flux d'écran
    RemoteInput = 22,     // injecter une entrée souris/clavier
    FilePush = 30,        // envoyer un fichier à l'élève
    LaunchApp = 40,       // lancer une application / ouvrir une URL
    CloseApp = 41,        // fermer une application par nom de processus
    Shutdown = 50,        // éteindre le poste
    Restart = 51,         // redémarrer le poste
    Logoff = 52,          // fermer la session Windows

    // --- Élève -> Prof ---
    HelloAck = 1,         // réponse à Hello (accepté/refusé)
    ScreenFrame = 100,    // une image d'écran (JPEG)
    Ack = 101,            // accusé de réception générique
    Error = 102,          // erreur côté élève
}

/// <summary>
/// Enveloppe générique d'un message. Le champ <see cref="Payload"/> contient
/// le contenu spécifique sérialisé en JSON selon <see cref="Type"/>.
/// </summary>
public sealed class Envelope
{
    public MessageType Type { get; set; }
    public int Version { get; set; } = Net.ProtocolVersion;

    /// <summary>Charge utile JSON brute, décodée selon le type.</summary>
    public JsonElement? Payload { get; set; }
}

// ---------- Charges utiles (payloads) ----------

/// <summary>Réponse de découverte envoyée par un agent élève.</summary>
public sealed class DiscoveryReply
{
    public string MachineName { get; set; } = "";
    public string UserName { get; set; } = "";
    public string IpAddress { get; set; } = "";
    public int ControlPort { get; set; } = Net.ControlPort;
    public string AgentVersion { get; set; } = "1.0.0";
}

public sealed class HelloPayload
{
    public string RoomCode { get; set; } = "";
    public string TeacherName { get; set; } = "";
}

public sealed class HelloAckPayload
{
    public bool Accepted { get; set; }
    public string Reason { get; set; } = "";
    public string MachineName { get; set; } = "";
    public string UserName { get; set; } = "";
}

public sealed class ShowMessagePayload
{
    public string Title { get; set; } = "Message du professeur";
    public string Text { get; set; } = "";
    public int DurationSeconds { get; set; } = 0; // 0 = jusqu'à fermeture manuelle
}

public sealed class LockPayload
{
    public string Message { get; set; } = "Écran verrouillé par le professeur";
}

public sealed class StartScreenPayload
{
    public int Quality { get; set; } = 50;     // qualité JPEG 1..100
    public int MaxWidth { get; set; } = 1280;  // redimensionnement côté élève
    public int Fps { get; set; } = 4;          // images par seconde
    public bool AllowControl { get; set; } = false;
}

public sealed class ScreenFramePayload
{
    public int Width { get; set; }
    public int Height { get; set; }
    public int ScreenWidth { get; set; }   // résolution réelle de l'écran élève
    public int ScreenHeight { get; set; }
    public byte[] Jpeg { get; set; } = Array.Empty<byte>();
}

public enum InputKind { MouseMove, MouseDown, MouseUp, MouseWheel, KeyDown, KeyUp }

public sealed class RemoteInputPayload
{
    public InputKind Kind { get; set; }
    // Coordonnées normalisées 0..1 (indépendantes de la résolution)
    public double X { get; set; }
    public double Y { get; set; }
    public int Button { get; set; }   // 0=gauche, 1=droit, 2=milieu
    public int Wheel { get; set; }
    public int KeyCode { get; set; }  // code touche virtuelle Windows (VK)
}

public sealed class FilePushPayload
{
    public string FileName { get; set; } = "";
    public byte[] Content { get; set; } = Array.Empty<byte>();
    public bool OpenAfter { get; set; }
}

public sealed class LaunchAppPayload
{
    public string Path { get; set; } = "";       // exe, chemin, ou URL
    public string Arguments { get; set; } = "";
}

public sealed class CloseAppPayload
{
    public string ProcessName { get; set; } = ""; // sans ".exe"
}

public sealed class ErrorPayload
{
    public string Message { get; set; } = "";
}

public sealed class PowerPayload
{
    public int DelaySeconds { get; set; } = 5;
    public string Message { get; set; } = "";
}
