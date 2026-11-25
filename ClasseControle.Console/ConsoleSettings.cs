using System.Text.Json;
using ClasseControle.Common;

namespace ClasseControle.Console;

/// <summary>
/// Paramètres de la console, conservés d'une séance à l'autre dans
/// %AppData%\Classe_controle\console.json.
/// </summary>
internal sealed class QuickItem
{
    public string Name { get; set; } = "";
    public string Target { get; set; } = "";
}

internal sealed class ConsoleSettings
{
    public string RoomCode { get; set; } = "CLASSE";
    public string TeacherName { get; set; } = "Professeur";
    public string LessonTitle { get; set; } = "Séance";
    public string LockMessage { get; set; } = "Écran verrouillé par le professeur";
    /// <summary>Image de l'écran de verrouillage, déjà recompressée en JPEG (vide = aucune).</summary>
    public string LockImageFile { get; set; } = "";
    public int TeamSize { get; set; } = 3;
    /// <summary>Largeur des vignettes en pixels (zoom de la console, 100 % = 290).</summary>
    public double TileWidth { get; set; } = 290;
    public int ThumbFps { get; set; } = 2;
    public List<QuickItem> QuickLaunch { get; set; } = new()
    {
        new() { Name = "Bloc-notes", Target = "notepad" },
        new() { Name = "Calculatrice", Target = "calc" },
        new() { Name = "Paint", Target = "mspaint" },
        new() { Name = "Word", Target = "winword" },
        new() { Name = "Excel", Target = "excel" },
        new() { Name = "PowerPoint", Target = "powerpnt" },
        new() { Name = "Navigateur (Edge)", Target = "msedge" },
    };
    public List<string> AllowedSites { get; set; } = new();
    /// <summary>Ordre des vignettes choisi par glisser-déposer (noms des postes).</summary>
    public List<string> TileOrder { get; set; } = new();
    /// <summary>Restrictions propres à chaque poste (par nom), réappliquées à chaque connexion.</summary>
    public Dictionary<string, RestrictionsPayload> RestrictionsByMachine { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    private static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Classe_controle");
    private static string FilePath => Path.Combine(Dir, "console.json");
    public static string LockImagePath => Path.Combine(Dir, "lock-image.jpg");

    private byte[]? _lockImageCache;

    public static ConsoleSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<ConsoleSettings>(File.ReadAllText(FilePath)) ?? new();
        }
        catch { }
        return new ConsoleSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { }
    }

    /// <summary>Image de verrouillage prête à envoyer, ou null.</summary>
    public byte[]? LockImage()
    {
        if (LockImageFile.Length == 0) return null;
        try { return _lockImageCache ??= File.ReadAllBytes(LockImageFile); }
        catch { return null; }
    }

    /// <summary>Recompresse et enregistre une nouvelle image de verrouillage.</summary>
    public void SetLockImage(byte[]? original)
    {
        _lockImageCache = null;
        if (original == null)
        {
            LockImageFile = "";
            try { File.Delete(LockImagePath); } catch { }
            return;
        }
        Directory.CreateDirectory(Dir);
        File.WriteAllBytes(LockImagePath, ImageUtil.ToJpeg(original, 1920, 80));
        LockImageFile = LockImagePath;
    }
}
