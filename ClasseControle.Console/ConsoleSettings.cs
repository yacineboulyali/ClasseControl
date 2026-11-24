using System.Text.Json;
using ClasseControle.Common;

namespace ClasseControle.Console;

/// <summary>
/// Paramètres de la console, conservés d'une séance à l'autre dans
/// %AppData%\Classe_controle\console.json.
/// </summary>
internal sealed class ConsoleSettings
{
    public string RoomCode { get; set; } = "CLASSE";
    public string TeacherName { get; set; } = "Professeur";
    public string LessonTitle { get; set; } = "Séance";
    public string LockMessage { get; set; } = "Écran verrouillé par le professeur";
    /// <summary>Image de l'écran de verrouillage, déjà recompressée en JPEG (vide = aucune).</summary>
    public string LockImageFile { get; set; } = "";
    public int TeamSize { get; set; } = 3;

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
