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

public static class AgentInfo
{
    public const string Version = "1.9.1";
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
    BroadcastStart = 60,  // début de diffusion de l'écran du professeur
    BroadcastFrame = 61,  // une image de l'écran du professeur (JPEG)
    BroadcastStop = 62,   // fin de diffusion
    LowerHand = 65,       // le professeur baisse la main levée de l'élève
    AudioStart = 66,      // début d'annonce audio (micro du professeur)
    AudioChunk = 67,      // morceau de son PCM
    AudioStop = 68,       // fin d'annonce
    Reward = 71,          // récompense (étoiles) donnée / retirée à l'élève
    StudentDesktop = 72,  // action sur le bureau élève (réduire / fermer les fenêtres)
    InternetBlock = 73,   // couper / rétablir l'accès à Internet (le réseau local reste joignable)
    SetMute = 74,         // couper / rétablir le son des applications de l'élève
    AppsWatch = 77,       // démarrer / arrêter l'envoi de la liste des applications ouvertes
    InputLock = 78,       // bloquer / débloquer le clavier et la souris de l'élève (écran visible)
    RemoveGames = 79,     // désinstaller les jeux préinstallés de Windows (pour l'utilisateur du poste)
    Survey = 75,          // sondage rapide envoyé aux élèves
    SurveyClose = 76,     // fin du sondage
    SetRestrictions = 70, // liste d'applications / sites interdits
    SessionStart = 80,    // début de séance : l'élève doit s'identifier
    SessionEnd = 81,      // fin de séance
    StudentLoginAck = 82, // réponse à l'identification de l'élève
    GroupOpen = 90,       // ouvrir l'espace d'équipe (affectation à un groupe)
    GroupDoc = 91,        // document partagé du groupe (dans les deux sens)
    GroupChat = 92,       // message de discussion du groupe (dans les deux sens)
    GroupClose = 93,      // fermer l'espace d'équipe

    // --- Élève -> Prof (suite) ---
    HandRaise = 110,      // l'élève lève / baisse la main
    StudentLogin = 112,   // l'élève s'identifie (nom complet) pour la séance
    Chat = 120,           // discussion professeur ↔ élève (dans les deux sens)
    SurveyAnswer = 121,   // réponse de l'élève à un sondage
    AppsList = 122,       // applications ouvertes sur le poste élève
    AgentNotice = 123,    // compte rendu d'une action (ex. jeux désinstallés)

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
    public string AgentVersion { get; set; } = AgentInfo.Version;
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
    /// <summary>Nom saisi par l'élève en début de séance (vide sinon).</summary>
    public string StudentName { get; set; } = "";
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
    /// <summary>Image de fond facultative (JPEG), choisie par le professeur.</summary>
    public byte[]? ImageJpeg { get; set; }
    /// <summary>Écran noir : écran entièrement noir, sans message (attention de la classe).</summary>
    public bool Blank { get; set; }
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


/// <summary>Début de diffusion de l'écran du professeur vers les élèves.</summary>
public sealed class BroadcastStartPayload
{
    public string Title { get; set; } = "Diffusion de l'écran du professeur";
    /// <summary>Plein écran au premier plan + clavier/souris de l'élève neutralisés.</summary>
    public bool Exclusive { get; set; } = true;
}

/// <summary>
/// Restrictions appliquées sur le poste élève. Les applications sont des noms
/// de processus (sans .exe) ; les sites sont des mots-clés recherchés dans le
/// titre de l'onglet actif du navigateur (ex : "youtube", "facebook").
/// </summary>
public enum WebMode
{
    Unrestricted = 0, // pas de restriction web
    BlockList = 1,    // sites interdits (liste noire)
    AllowList = 2,    // seulement les sites approuvés (liste blanche)
    BlockAll = 3,     // aucun navigateur
}

public sealed class RestrictionsPayload
{
    public bool Enabled { get; set; }
    public List<string> BlockedApps { get; set; } = new();
    public List<string> BlockedSites { get; set; } = new();
    public WebMode Web { get; set; } = WebMode.BlockList;
    public List<string> AllowedSites { get; set; } = new();

    public RestrictionsPayload Clone() => new()
    {
        Enabled = Enabled, Web = Web,
        BlockedApps = BlockedApps.ToList(), BlockedSites = BlockedSites.ToList(), AllowedSites = AllowedSites.ToList(),
    };

    /// <summary>Restrictions réellement actives (applications interdites ou web filtré).</summary>
    [JsonIgnore]
    public bool IsActive => Enabled && (BlockedApps.Count > 0 || Web == WebMode.AllowList || Web == WebMode.BlockAll
                                        || (Web == WebMode.BlockList && BlockedSites.Count > 0));
}

public sealed class HandRaisePayload
{
    public bool Raised { get; set; }
    public string Note { get; set; } = "";
}

/// <summary>Début de séance : chaque élève doit saisir son nom pour accéder au poste.</summary>
public sealed class SessionStartPayload
{
    public string SessionId { get; set; } = "";
    public string Title { get; set; } = "Séance";
    public string TeacherName { get; set; } = "";
}

public sealed class StudentLoginPayload
{
    public string SessionId { get; set; } = "";
    public string FullName { get; set; } = "";
}

public sealed class StudentLoginAckPayload
{
    public bool Accepted { get; set; }
    public string Message { get; set; } = "";
}

/// <summary>Affectation à un groupe de travail avec son espace partagé.</summary>
public sealed class GroupOpenPayload
{
    public string GroupId { get; set; } = "";
    public string GroupName { get; set; } = "";
    public List<string> Members { get; set; } = new();
    public string Text { get; set; } = "";
    public int Revision { get; set; }
    public List<GroupChatPayload> History { get; set; } = new();
}

/// <summary>
/// Document partagé du groupe.
/// Élève → prof : modifications <see cref="Ops"/> écrites sur la révision <see cref="Revision"/>,
///                série numérotée <see cref="Seq"/>.
/// Prof → élève : modifications appliquées (<see cref="Ops"/>, vides pour l'auteur), révision
///                obtenue, texte complet faisant foi (<see cref="Text"/>) et, pour l'auteur
///                seulement, l'accusé <see cref="AckSeq"/>.
/// </summary>
public sealed class GroupDocPayload
{
    public string GroupId { get; set; } = "";
    public string Text { get; set; } = "";
    public int Revision { get; set; }
    public int Seq { get; set; }
    public int AckSeq { get; set; }
    public List<SpliceDto> Ops { get; set; } = new();
}

public sealed class GroupChatPayload
{
    public string GroupId { get; set; } = "";
    public string Author { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime Time { get; set; }
}

public sealed class GroupClosePayload
{
    public string GroupId { get; set; } = "";
}

public sealed class ChatPayload
{
    public string Author { get; set; } = "";
    public string Text { get; set; } = "";
    public DateTime Time { get; set; }
}

public sealed class AudioStartPayload
{
    public int SampleRate { get; set; } = 16000;
    public int Channels { get; set; } = 1;
    public string Speaker { get; set; } = "";
}

public sealed class AudioChunkPayload
{
    /// <summary>Échantillons PCM 16 bits little-endian.</summary>
    public byte[] Pcm { get; set; } = Array.Empty<byte>();
}

public sealed class RewardPayload
{
    /// <summary>Total d'étoiles de l'élève après l'opération.</summary>
    public int Stars { get; set; }
    /// <summary>+1 étoile donnée, -1 retirée, 0 remise à zéro.</summary>
    public int Delta { get; set; }
}

public enum DesktopAction { MinimizeAll = 0, CloseAllApps = 1 }

public sealed class StudentDesktopPayload
{
    public DesktopAction Action { get; set; }
}

public sealed class SurveyPayload
{
    public string Id { get; set; } = "";
    public string Question { get; set; } = "";
    public List<string> Options { get; set; } = new();
}

public sealed class SurveyAnswerPayload
{
    public string Id { get; set; } = "";
    public int Choice { get; set; }
}

public sealed class SurveyClosePayload
{
    public string Id { get; set; } = "";
}

public sealed class InternetBlockPayload
{
    public bool Blocked { get; set; }
}

public sealed class MutePayload
{
    public bool Muted { get; set; }
}

public sealed class AppsWatchPayload
{
    public bool Enabled { get; set; }
}

/// <summary>Une application ouverte (fenêtre visible) sur le poste élève.</summary>
public sealed class AppInfo
{
    public string ProcessName { get; set; } = "";
    /// <summary>Nom lisible (description du fichier, ex. « Microsoft Word »).</summary>
    public string Name { get; set; } = "";
    /// <summary>Titre de la fenêtre (ex. « Rapport.docx - Word »).</summary>
    public string Title { get; set; } = "";
    /// <summary>Application au premier plan : celle sur laquelle l'élève travaille.</summary>
    public bool IsActive { get; set; }
    /// <summary>Icône PNG 32×32, envoyée une seule fois par application (null ensuite).</summary>
    public byte[]? Icon { get; set; }
}

public sealed class AppsListPayload
{
    public List<AppInfo> Apps { get; set; } = new();
}

public sealed class InputLockPayload
{
    public bool Locked { get; set; }
}

public sealed class AgentNoticePayload
{
    public string Text { get; set; } = "";
}
