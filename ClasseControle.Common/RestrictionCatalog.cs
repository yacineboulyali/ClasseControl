namespace ClasseControle.Common;

/// <summary>
/// Catégories prêtes à l'emploi pour les restrictions. Dans les listes envoyées à l'agent,
/// une catégorie s'écrit « @nom » (ex. « @jeux-windows ») et l'agent la développe.
/// </summary>
public static class RestrictionCatalog
{
    public sealed record Category(string Token, string Label, string Description, string[] Items);

    // ---------- Applications ----------

    /// <summary>
    /// Jeux préinstallés de Windows 10/11 (applications du Store). Ils s'exécutent depuis
    /// C:\Program Files\WindowsApps\&lt;paquet&gt;\ : on les reconnaît au nom de leur paquet.
    /// </summary>
    public static readonly Category WindowsGames = new("@jeux-windows", "Jeux préinstallés de Windows",
        "Solitaire, Candy Crush, Bubble Witch, Minecraft, Mahjong, Démineur, Sudoku, Asphalt, March of Empires…",
        new[]
        {
            "MicrosoftSolitaireCollection", "king.com", "CandyCrush", "BubbleWitch", "FarmHeroes",
            "MinecraftUWP", "MicrosoftMahjong", "Minesweeper", "MicrosoftJigsaw", "MicrosoftSudoku",
            "Gameloft", "Asphalt", "MarchofEmpires", "DisneyMagicKingdoms", "RoyalRevolt", "CaesarsSlots",
            "HiddenCity", "Zynga", "SimpleSolitaire", "Spades", "Hearts", "FreeCell",
        });

    public static readonly Category GamePlatforms = new("@plateformes-jeux", "Plateformes et lanceurs de jeux",
        "Steam, Epic Games, Roblox, Minecraft Launcher, Battle.net, Riot, EA, Ubisoft, GOG…",
        new[]
        {
            "steam", "steamwebhelper", "epicgameslauncher", "robloxplayerbeta", "robloxplayerlauncher", "robloxstudiobeta",
            "minecraftlauncher", "minecraft.windows", "battle.net", "riotclientservices", "leagueclient", "valorant",
            "fortniteclient-win64-shipping", "origin", "eadesktop", "ubisoftconnect", "upc", "galaxyclient",
        });

    public static readonly Category Messaging = new("@messageries", "Messageries",
        "Discord, WhatsApp, Telegram, Messenger, Signal",
        new[] { "discord", "whatsapp", "telegram", "messenger", "signal" });

    public static readonly Category[] AppCategories = { WindowsGames, GamePlatforms, Messaging };

    // ---------- Sites (mots-clés cherchés dans le titre de l'onglet) ----------

    public static readonly Category SocialSites = new("@reseaux-sociaux", "Réseaux sociaux",
        "Facebook, Instagram, TikTok, Snapchat, Twitter/X, Pinterest, Reddit",
        new[] { "facebook", "instagram", "tiktok", "snapchat", "twitter", "pinterest", "reddit" });

    public static readonly Category VideoSites = new("@video", "Vidéo et streaming",
        "YouTube, Netflix, Twitch, Dailymotion, Prime Video, Shahid",
        new[] { "youtube", "netflix", "twitch", "dailymotion", "prime video", "shahid" });

    public static readonly Category GameSites = new("@jeux-en-ligne", "Jeux en ligne",
        "Poki, Friv, CrazyGames, Miniclip, Y8, jeux .io, Roblox…",
        new[] { "poki", "friv", "crazygames", "crazy games", "miniclip", "y8 games", "agar.io", "slither.io", "krunker", "roblox", "jeux gratuits" });

    public static readonly Category[] SiteCategories = { SocialSites, VideoSites, GameSites };

    /// <summary>Remplace les catégories « @… » par leur contenu ; les autres entrées sont gardées.</summary>
    public static List<string> Expand(IEnumerable<string> items, IEnumerable<Category> catalog)
    {
        var map = catalog.ToDictionary(c => c.Token, StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var raw in items)
        {
            var i = raw.Trim();
            if (i.Length == 0) continue;
            if (map.TryGetValue(i, out var cat)) result.AddRange(cat.Items);
            else if (!i.StartsWith('@')) result.Add(i);
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Texte lisible pour l'élève : « Jeux préinstallés de Windows, discord ».</summary>
    public static string Describe(IEnumerable<string> items, IEnumerable<Category> catalog)
    {
        var map = catalog.ToDictionary(c => c.Token, StringComparer.OrdinalIgnoreCase);
        return string.Join(", ", items.Select(i => map.TryGetValue(i.Trim(), out var c) ? c.Label : i.Trim()).Where(s => s.Length > 0));
    }

    /// <summary>Motifs des paquets de jeux préinstallés à désinstaller (Get-AppxPackage -Name).</summary>
    public static readonly string[] UwpGamePackagePatterns =
    {
        "*SolitaireCollection*", "king.com.*", "*CandyCrush*", "*BubbleWitch*", "*FarmHeroes*", "*MinecraftUWP*",
        "*MicrosoftMahjong*", "*Minesweeper*", "*MicrosoftJigsaw*", "*MicrosoftSudoku*", "*Gameloft*", "*Asphalt*",
        "*MarchofEmpires*", "*DisneyMagicKingdoms*", "*RoyalRevolt*", "*CaesarsSlots*", "*HiddenCity*", "*Zynga*",
    };
}
