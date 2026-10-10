using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using Hyprism.Theming;

namespace Hyprism.Core;

/// <summary>Implemented by the module that can show wallpapers with animated transitions (Lively).</summary>
public interface IWallpaperHost
{
    bool IsActive { get; }
    Task ShowAsync(string path, string transition, CancellationToken ct = default);
}

/// <summary>Implemented by the tiling module so the global hotkey can toggle it.</summary>
public interface IToggleable
{
    Task ToggleAsync(CancellationToken ct = default);
}

public enum WallpaperTarget { Desktop, LockScreen, Both }

public sealed class Profile
{
    public string Name { get; set; } = "";
    public Palette Palette { get; set; } = StarterThemes.CatppuccinMocha;
    public string? Wallpaper { get; set; }
    public string Transition { get; set; } = "grow";
    public Dictionary<string, ModuleState> Modules { get; set; } = [];
    public DateTime Saved { get; set; } = DateTime.Now;
}

/// <summary>The glue: palette push, wallpapers, profiles ("rices"), effects and performance mode.</summary>
public static class Hub
{
    public static IReadOnlyList<IModule> Modules { get; private set; } = [];
    public static event Action<string>? Notify;
    public static event Action? Changed;

    public static void Init(IEnumerable<IModule> modules)
    {
        Modules = modules.ToList();
        SafeMode.Reverted += msg => Notify?.Invoke(msg);
    }

    public static IModule? Get(string id) => Modules.FirstOrDefault(m => m.Id == id);
    static IEnumerable<IModule> Enabled => Modules.Where(m => Store.Config.For(m.Id).Enabled);

    static async Task ForEachAsync(IEnumerable<IModule> modules, Func<IModule, Task> action, IProgress<string>? log)
    {
        foreach (var m in modules)
        {
            try { await action(m); }
            catch (Exception e) { log?.Report($"{m.Name}: {e.Message}"); }
        }
    }

    // ---------- colors ----------

    public static async Task ApplyPaletteAsync(Palette p, IProgress<string>? log = null)
    {
        await Sys.OffUiThread();
        Store.Config.Palette = p;
        Store.Save();
        await ForEachAsync(Enabled, m => m.ApplyPaletteAsync(p), log);
        try { WindowsLook.SetAccent(p.Accent); } catch (Exception e) { log?.Report($"Accent: {e.Message}"); }
        Changed?.Invoke();
    }

    // Decoding and k-means are CPU work: keep them off the UI thread.
    public static Task<Palette> PaletteFromWallpaperAsync(string path, bool? dark = null) =>
        Task.Run(() => PaletteExtractor.FromImageAsync(path, dark ?? (WindowsLook.AppsDark || Store.Config.Palette.IsDark)));

    // ---------- wallpapers ----------

    public static readonly string[] ImageExtensions = [".jpg", ".jpeg", ".png", ".bmp", ".webp", ".gif", ".mp4", ".webm", ".mkv"];
    static readonly string[] StillImages = [".jpg", ".jpeg", ".png", ".bmp", ".webp"];

    /// <summary>Lock screen only, or desktop (+ optionally lock screen). The lock screen accepts still images only.</summary>
    public static async Task SetWallpaperAsync(string path, WallpaperTarget target, IProgress<string>? log = null)
    {
        await Sys.OffUiThread();
        if (target != WallpaperTarget.Desktop)
        {
            if (!StillImages.Contains(Path.GetExtension(path).ToLowerInvariant()))
                throw new InvalidOperationException("The lock screen can only show still images (JPG, PNG, BMP, WebP).");
            await WindowsLook.SetLockScreenAsync(path);
        }
        if (target != WallpaperTarget.LockScreen) await SetWallpaperAsync(path, log);
        else Changed?.Invoke();
    }

    public static async Task SetWallpaperAsync(string path, IProgress<string>? log = null)
    {
        await Sys.OffUiThread();
        Store.Config.Wallpaper = path;
        Store.Save();
        var host = Modules.OfType<IWallpaperHost>().FirstOrDefault(h => h.IsActive);
        if (host is not null) await host.ShowAsync(path, Store.Config.Transition);
        else if (StillImages.Contains(Path.GetExtension(path).ToLowerInvariant())) WindowsLook.SetWallpaper(path);
        else throw new InvalidOperationException("Video and animated wallpapers need the Live Wallpapers module enabled.");

        if (Store.Config.FollowWallpaper && StillImages.Contains(Path.GetExtension(path).ToLowerInvariant()))
            await ApplyPaletteAsync(await PaletteFromWallpaperAsync(path), log);
        Changed?.Invoke();
    }

    public static IReadOnlyList<string> WallpapersInFolder() =>
        Store.Config.WallpaperFolder is { } dir && Directory.Exists(dir)
            ? Directory.EnumerateFiles(dir).Where(f => ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant())).Order().ToList()
            : [];

    public static async Task NextWallpaperAsync()
    {
        await Sys.OffUiThread();
        var all = WallpapersInFolder();
        if (all.Count == 0) { Notify?.Invoke("Pick a wallpaper folder first (Wallpapers page)."); return; }
        var i = all.ToList().IndexOf(Store.Config.Wallpaper ?? "");
        await SetWallpaperAsync(all[(i + 1) % all.Count]);
    }

    // ---------- profiles ----------

    static string ProfilePath(string name) => Path.Combine(Store.ProfilesDir, string.Concat(name.Split(Path.GetInvalidFileNameChars())) + ".json");

    public static IReadOnlyList<Profile> Profiles()
    {
        if (!Directory.Exists(Store.ProfilesDir)) return [];
        return Directory.GetFiles(Store.ProfilesDir, "*.json")
            .Select(f => { try { return JsonSerializer.Deserialize<Profile>(File.ReadAllText(f), Store.Json); } catch { return null; } })
            .OfType<Profile>().OrderBy(p => p.Name).ToList();
    }

    /// <summary>Captures the current setup as a profile.</summary>
    public static Profile SaveProfile(string name)
    {
        var c = Store.Config;
        var p = new Profile
        {
            Name = name, Palette = c.Palette, Wallpaper = c.Wallpaper, Transition = c.Transition,
            Modules = c.Modules.ToDictionary(kv => kv.Key, kv => new ModuleState { Enabled = kv.Value.Enabled, Settings = (System.Text.Json.Nodes.JsonObject)kv.Value.Settings.DeepClone() }),
        };
        Directory.CreateDirectory(Store.ProfilesDir);
        File.WriteAllText(ProfilePath(name), JsonSerializer.Serialize(p, Store.Json));
        c.ActiveProfile = name;
        Store.Save();
        Changed?.Invoke();
        return p;
    }

    public static void DeleteProfile(string name) { File.Delete(ProfilePath(name)); Changed?.Invoke(); }

    public static async Task ApplyProfileAsync(Profile p, IProgress<string>? log = null)
    {
        await Sys.OffUiThread();
        var c = Store.Config;
        foreach (var (id, state) in p.Modules)
        {
            if (Get(id) is not { } m) continue;
            var was = c.For(id).Enabled;
            c.For(id).Settings = (System.Text.Json.Nodes.JsonObject)state.Settings.DeepClone();
            try
            {
                if (state.Enabled && !was) await m.EnableAsync();
                if (!state.Enabled && was) await m.DisableAsync();
                if (state.Enabled) await m.ApplyAsync(m.WithDefaults(state.Settings));
            }
            catch (Exception e) { log?.Report($"{m.Name}: {e.Message}"); }
        }
        c.Transition = p.Transition;
        c.ActiveProfile = p.Name;
        var follow = c.FollowWallpaper;
        c.FollowWallpaper = false; // the profile carries its own palette
        try { if (p.Wallpaper is { } wp && File.Exists(wp)) await SetWallpaperAsync(wp, log); }
        finally { c.FollowWallpaper = follow; }
        await ApplyPaletteAsync(p.Palette, log);
        Notify?.Invoke($"Profile \"{p.Name}\" applied");
    }

    public static async Task NextProfileAsync()
    {
        await Sys.OffUiThread();
        var all = Profiles();
        if (all.Count == 0) { Notify?.Invoke("No saved profiles yet."); return; }
        var i = all.ToList().FindIndex(p => p.Name == Store.Config.ActiveProfile);
        await ApplyProfileAsync(all[(i + 1) % all.Count]);
    }

    /// <summary>A .hyprism file is a zip: profile.json plus the wallpaper.</summary>
    public static void ExportProfile(Profile p, string file)
    {
        using var zip = ZipFile.Open(file, ZipArchiveMode.Create);
        var copy = JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(p, Store.Json), Store.Json)!;
        if (p.Wallpaper is { } wp && File.Exists(wp))
        {
            var entry = "wallpaper" + Path.GetExtension(wp);
            zip.CreateEntryFromFile(wp, entry);
            copy.Wallpaper = entry;
        }
        using var w = new StreamWriter(zip.CreateEntry("profile.json").Open());
        w.Write(JsonSerializer.Serialize(copy, Store.Json));
    }

    public static Profile ImportProfile(string file)
    {
        using var zip = ZipFile.OpenRead(file);
        var json = zip.GetEntry("profile.json") ?? throw new InvalidDataException("Not a .hyprism file (no profile.json).");
        using var r = new StreamReader(json.Open());
        var p = JsonSerializer.Deserialize<Profile>(r.ReadToEnd(), Store.Json) ?? throw new InvalidDataException("Empty profile.");
        if (string.IsNullOrWhiteSpace(p.Name)) p.Name = Path.GetFileNameWithoutExtension(file);
        // Only accept the bundled wallpaper entry; never a path from the file (it could point anywhere).
        var wpEntry = zip.Entries.FirstOrDefault(e => e.FullName.StartsWith("wallpaper.", StringComparison.OrdinalIgnoreCase) && !e.FullName.Contains('/') && !e.FullName.Contains('\\'));
        p.Wallpaper = null;
        if (wpEntry is not null && ImageExtensions.Contains(Path.GetExtension(wpEntry.Name).ToLowerInvariant()))
        {
            Directory.CreateDirectory(Store.WallpapersDir);
            var dest = Path.Combine(Store.WallpapersDir, string.Concat(p.Name.Split(Path.GetInvalidFileNameChars())) + Path.GetExtension(wpEntry.Name));
            wpEntry.ExtractToFile(dest, overwrite: true);
            p.Wallpaper = dest;
        }
        Directory.CreateDirectory(Store.ProfilesDir);
        File.WriteAllText(ProfilePath(p.Name), JsonSerializer.Serialize(p, Store.Json));
        Changed?.Invoke();
        return p;
    }

    // ---------- effects / performance ----------

    public static bool BlurOn { get; private set; } = true;
    public static bool PerformanceOn { get; private set; }
    static bool performanceAuto;

    public static async Task SetEffectsAsync(bool blur, bool animate)
    {
        BlurOn = blur;
        await Sys.OffUiThread();
        await ForEachAsync(Enabled, m => m.SetEffectsAsync(blur, animate), null);
        Changed?.Invoke();
    }

    public static Task ToggleBlurAsync() => SetEffectsAsync(!BlurOn, !PerformanceOn);

    public static async Task SetPerformanceAsync(bool on, bool auto = false)
    {
        if (PerformanceOn == on) return;
        PerformanceOn = on;
        performanceAuto = on && auto;
        Store.Config.EffectsSuspended = on; Store.Save();
        await SetEffectsAsync(!on, !on);
        Notify?.Invoke(on ? "Performance mode on: live wallpapers paused, blur off" : "Performance mode off");
    }

    public static async Task ToggleTilingAsync()
    {
        await Sys.OffUiThread();
        if (Modules.OfType<IToggleable>().FirstOrDefault() is { } t) await t.ToggleAsync();
    }

    /// <summary>
    /// Performance mode writes "no blur" into the apps' own configs (Terminal: opaque, no acrylic). If Hyprism closed
    /// before it ended, they stayed that way while Hyprism showed them as on. Called at startup to put effects back.
    /// </summary>
    public static async Task RestoreSuspendedEffectsAsync()
    {
        if (!Store.Config.EffectsSuspended) return;
        Store.Config.EffectsSuspended = false; Store.Save();
        await SetEffectsAsync(true, true); // the next auto check turns performance mode on again if still needed
    }

    /// <summary>Polled by the app every few seconds: battery or a fullscreen game turns performance mode on.</summary>
    public static async Task CheckAutoPerformanceAsync()
    {
        if (!Store.Config.AutoPerformanceMode) return;
        bool busy = OnBattery() || FullscreenAppRunning();
        if (busy && !PerformanceOn) await SetPerformanceAsync(true, auto: true);
        else if (!busy && PerformanceOn && performanceAuto) await SetPerformanceAsync(false);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct SystemPowerStatus { public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag; public int BatteryLifeTime, BatteryFullLifeTime; }
    [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out SystemPowerStatus s);
    [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);

    static bool OnBattery() => GetSystemPowerStatus(out var s) && s.ACLineStatus == 0;

    // QUNS_BUSY (fullscreen app), QUNS_RUNNING_D3D_FULL_SCREEN (exclusive fullscreen game), QUNS_PRESENTATION_MODE
    static bool FullscreenAppRunning() => SHQueryUserNotificationState(out var st) == 0 && st is 2 or 3 or 4;

    /// <summary>Auto Dark Mode calls Hyprism with --theme light|dark when Windows switches.</summary>
    public static async Task OnSystemThemeAsync(bool dark)
    {
        await Sys.OffUiThread();
        var name = dark ? Store.Config.DarkProfile : Store.Config.LightProfile;
        if (name is not null && Profiles().FirstOrDefault(p => p.Name == name) is { } p) { await ApplyProfileAsync(p); return; }
        if (Store.Config.Wallpaper is { } wp && File.Exists(wp)) await ApplyPaletteAsync(await PaletteFromWallpaperAsync(wp, dark));
    }
}
