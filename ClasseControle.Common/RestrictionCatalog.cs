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

    // ---------- Sites : noms de domaine (filtrage web par domaine, voir WebFilter.cs) ----------

    /// <summary>
    /// Domaines de chaque catégorie de sites. Un domaine couvre aussi ses sous-domaines
    /// (« tiktok.com » bloque www.tiktok.com, m.tiktok.com…).
    /// </summary>
    public static readonly Dictionary<string, string[]> SiteCategoryDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        [SocialSites.Token] = new[]
        {
            "facebook.com", "fbcdn.net", "fb.com", "messenger.com", "instagram.com", "cdninstagram.com",
            "tiktok.com", "tiktokcdn.com", "tiktokv.com", "byteoversea.com", "snapchat.com", "sc-cdn.net",
            "twitter.com", "x.com", "twimg.com", "pinterest.com", "pinimg.com", "reddit.com", "redd.it",
            "redditmedia.com", "threads.net", "linkedin.com", "tumblr.com", "discord.com", "discord.gg",
            "discordapp.com", "web.whatsapp.com", "web.telegram.org",
        },
        [VideoSites.Token] = new[]
        {
            "youtube.com", "youtu.be", "ytimg.com", "googlevideo.com", "youtube-nocookie.com", "netflix.com",
            "nflxvideo.net", "nflximg.net", "twitch.tv", "ttvnw.net", "jtvnw.net", "dailymotion.com",
            "dmcdn.net", "primevideo.com", "shahid.mbc.net", "shahid.net", "disneyplus.com", "vimeo.com",
            "kick.com", "osn.com", "starzplay.com",
        },
        [GameSites.Token] = new[]
        {
            "poki.com", "poki.fr", "friv.com", "crazygames.com", "crazygames.fr", "miniclip.com", "y8.com",
            "agar.io", "slither.io", "krunker.io", "roblox.com", "rbxcdn.com", "jeux.fr", "jeu.fr",
            "jeuxgratuits.com", "coolmathgames.com", "kizi.com", "silvergames.com", "itch.io", "addictinggames.com",
            "armorgames.com", "kongregate.com", "gameflare.com", "1001jeux.fr",
        },
    };

    /// <summary>
    /// Comme <see cref="Expand"/> pour les sites, mais les catégories donnent leurs NOMS DE
    /// DOMAINE (en plus de leurs mots-clés) : utilisé par le filtrage web par domaine.
    /// </summary>
    public static List<string> ExpandDomains(IEnumerable<string> items)
    {
        var result = new List<string>();
        foreach (var raw in items)
        {
            var i = raw.Trim();
            if (i.Length == 0) continue;
            if (i.StartsWith('@'))
            {
                if (SiteCategoryDomains.TryGetValue(i, out var domains)) result.AddRange(domains);
                var cat = SiteCategories.FirstOrDefault(c => string.Equals(c.Token, i, StringComparison.OrdinalIgnoreCase));
                // Mots-clés d'un seul mot seulement : « prime video » ou « jeux gratuits » seraient trop larges en domaine.
                if (cat != null) result.AddRange(cat.Items.Where(w => !w.Contains(' ') && !w.Contains('.') && w.Length >= 5));
            }
            else result.Add(i);
        }
        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

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
