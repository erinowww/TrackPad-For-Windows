using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrackPadForWindows;

internal sealed class ActionConfig
{
    // run | keys | media | menu | submenu
    public string? Type { get; set; }
    public string? Label { get; set; }
    // Segoe Fluent Icons / MDL2 glyph kodu (ör. "E722"). Boşsa etiketin ilk harfi gösterilir.
    public string? Icon { get; set; }
    public string? Target { get; set; }
    public string? Args { get; set; }
    public string? Keys { get; set; }
    public List<ActionConfig>? Items { get; set; }

    [JsonIgnore]
    public string Kind => (Type ?? (Items != null ? "submenu" : Keys != null ? "keys" : "run")).ToLowerInvariant();

    public ActionConfig Clone() =>
        JsonSerializer.Deserialize<ActionConfig>(JsonSerializer.Serialize(this, AppConfig.Options), AppConfig.Options)!;
}

internal sealed class TapGesture
{
    public int Fingers { get; set; }
    public ActionConfig Action { get; set; } = new();
}

internal sealed class SwipeGesture
{
    public int Fingers { get; set; }
    // up | down | left | right
    public string Direction { get; set; } = "down";
    public ActionConfig Action { get; set; } = new();
}

internal sealed class AppConfig
{
    public const int CurrentVersion = 3;

    public int Version { get; set; }
    public int MaxTapMs { get; set; } = 350;
    public double MaxMove { get; set; } = 0.05;
    // Kaydırma sayılması için parmakların ortalama kayması (yüzeyin oranı)
    public double SwipeDistance { get; set; } = 0.12;
    public List<TapGesture> Taps { get; set; } = new();
    public List<SwipeGesture> Swipes { get; set; } = new();
    // Yuvarlak menünün dilimleri; ilki üstte, saat yönünde devam eder.
    public List<ActionConfig> Menu { get; set; } = new();

    internal static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static string Folder => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrackPadForWindows");

    // Programın eski adıyla (TouchMenu) kaydedilmiş ayarlar
    private static string LegacyFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TouchMenu", "menu.json");

    public static string FilePath => Path.Combine(Folder, "menu.json");

    public static AppConfig Load()
    {
        if (!File.Exists(FilePath) && File.Exists(LegacyFilePath))
        {
            Directory.CreateDirectory(Folder);
            File.Copy(LegacyFilePath, FilePath);
        }
        if (File.Exists(FilePath))
        {
            var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(FilePath), Options)
                         ?? throw new InvalidDataException("menu.json boş.");
            if (config.Version >= CurrentVersion) return config;

            // Eski sürümün dosyası: yedeğini alıp yeni varsayılana geç.
            File.Copy(FilePath, FilePath + ".eski", overwrite: true);
        }
        var def = CreateDefault();
        def.Save();
        return def;
    }

    public void Save()
    {
        Version = CurrentVersion;
        Directory.CreateDirectory(Folder);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
    }

    private static ActionConfig Run(string label, string icon, string target, string? args = null) =>
        new() { Type = "run", Label = label, Icon = icon, Target = target, Args = args };

    private static ActionConfig Key(string label, string icon, string keys) =>
        new() { Type = "keys", Label = label, Icon = icon, Keys = keys };

    public static ActionConfig Screenshot => Key("Ekran görüntüsü", "E722", "Win+Shift+S");
    public static ActionConfig Lock => Run("Kilitle", "E72E", "rundll32.exe", "user32.dll,LockWorkStation");
    public static ActionConfig Desktop => Key("Masaüstü", "E7F4", "Win+D");
    public static ActionConfig Media => new() { Type = "media", Label = "Medya", Icon = "E8D6" };

    // Ayarlar penceresinin solundaki parçalar.
    public static List<ActionConfig> Catalog() => new()
    {
        Screenshot,
        Lock,
        Desktop,
        Media,
        Key("Görev görünümü", "E7C4", "Win+Tab"),
        Key("Pano geçmişi", "E81C", "Win+V"),
        Key("Emoji", "E76E", "Win+OemPeriod"),
        Key("Kopyala", "E8C8", "Ctrl+C"),
        Key("Yapıştır", "E77F", "Ctrl+V"),
        Key("Geri al", "E7A7", "Ctrl+Z"),
        Key("Oynat / Duraklat", "E768", "MediaPlayPause"),
        Key("Sonraki parça", "E893", "MediaNextTrack"),
        Key("Önceki parça", "E892", "MediaPreviousTrack"),
        Key("Sesi aç", "E995", "VolumeUp"),
        Key("Sesi kıs", "E993", "VolumeDown"),
        Key("Sessiz", "E74F", "VolumeMute"),
        Run("Dosya Gezgini", "E8B7", "explorer.exe"),
        Run("Hesap Makinesi", "E8EF", "calc.exe"),
        Run("Not Defteri", "E70B", "notepad.exe"),
        Run("Görev Yöneticisi", "E9D9", "taskmgr.exe"),
        Run("Windows Ayarları", "E713", "ms-settings:"),
        Run("Tarayıcı", "E774", "https://www.google.com"),
    };

    public static AppConfig CreateDefault() => new()
    {
        Version = CurrentVersion,
        Taps =
        {
            new TapGesture { Fingers = 4, Action = new ActionConfig { Type = "menu" } },
        },
        Swipes =
        {
            new SwipeGesture { Fingers = 4, Direction = "down", Action = Desktop },
            new SwipeGesture { Fingers = 4, Direction = "up", Action = Key("Görev görünümü", "E7C4", "Win+Tab") },
            new SwipeGesture { Fingers = 4, Direction = "left", Action = Key("Sonraki masaüstü", "E76C", "Ctrl+Win+Right") },
            new SwipeGesture { Fingers = 4, Direction = "right", Action = Key("Önceki masaüstü", "E76B", "Ctrl+Win+Left") },
        },
        // Üst, sağ, alt, sol
        Menu = { Screenshot, Lock, Desktop, Media },
    };
}
