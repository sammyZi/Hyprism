using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Hyprism.Theming;

namespace Hyprism.Core;

public sealed class ModuleState
{
    public bool Enabled { get; set; }
    public JsonObject Settings { get; set; } = [];
}

public sealed class AppConfig
{
    public bool StartWithWindows { get; set; } = true;
    /// <summary>"System", "Light" or "Dark" for Hyprism's own window.</summary>
    public string AppTheme { get; set; } = "System";
    public Palette Palette { get; set; } = StarterThemes.CatppuccinMocha;
    /// <summary>Recolor every module whenever the wallpaper changes.</summary>
    public bool FollowWallpaper { get; set; } = true;
    public string? Wallpaper { get; set; }
    public string? WallpaperFolder { get; set; }
    public int RotateMinutes { get; set; }
    public string Transition { get; set; } = "grow";
    public string? ActiveProfile { get; set; }
    public string? LightProfile { get; set; }
    public string? DarkProfile { get; set; }
    public bool AutoPerformanceMode { get; set; } = true;
    /// <summary>Performance mode turned effects off and hasn't turned them back on (e.g. Hyprism closed meanwhile).</summary>
    public bool EffectsSuspended { get; set; }
    /// <summary>GitHub "owner/repo" whose Releases carry Hyprism-Setup-*.exe. Empty disables update checks.</summary>
    public string? UpdateRepo { get; set; } = "sammyZi/Hyprism";
    public bool AutoCheckUpdates { get; set; } = true;
    public DateTime LastUpdateCheck { get; set; }
    public bool SafeMode { get; set; } = true;
    public Dictionary<string, string> Hotkeys { get; set; } = new()
    {
        ["switchProfile"] = "Win+Alt+P",
        ["nextWallpaper"] = "Win+Alt+W",
        ["toggleTiling"] = "Win+Alt+T",
        ["toggleBlur"] = "Win+Alt+B",
        ["performance"] = "Win+Alt+G",
    };
    public Dictionary<string, ModuleState> Modules { get; set; } = [];

    public ModuleState For(string moduleId) =>
        Modules.TryGetValue(moduleId, out var s) ? s : Modules[moduleId] = new ModuleState();
}

/// <summary>%AppData%\Hyprism\config.json and friends.</summary>
public static class Store
{
    public static readonly string Root = Path.Combine(Sys.RoamingAppData, "Hyprism");
    public static readonly string ConfigPath = Path.Combine(Root, "config.json");
    public static readonly string ProfilesDir = Path.Combine(Root, "profiles");
    public static readonly string WallpapersDir = Path.Combine(Root, "wallpapers");

    public static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static AppConfig Config { get; private set; } = new();

    public static void Load()
    {
        Directory.CreateDirectory(Root);
        if (!File.Exists(ConfigPath)) { Config = new(); return; }
        try { Config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(ConfigPath), Json) ?? new(); }
        catch (JsonException)
        {
            // Don't silently lose a hand-edited config: keep it aside and start fresh.
            File.Copy(ConfigPath, ConfigPath + ".broken", true);
            Config = new();
        }
    }

    static readonly object SaveLock = new();

    public static void Save()
    {
        lock (SaveLock)
        {
            Directory.CreateDirectory(Root);
            var tmp = ConfigPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(Config, Json));
            File.Move(tmp, ConfigPath, overwrite: true); // atomic replace: a crash mid-write can't corrupt config.json
        }
    }
}
