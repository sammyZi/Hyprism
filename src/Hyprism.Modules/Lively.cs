using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using Hyprism.Core;

namespace Hyprism.Modules;

/// <summary>
/// Lively Wallpaper: video/GIF/web/shader wallpapers. Hyprism adds a swww-style transition slideshow: a Lively web
/// wallpaper installed into Lively's library and driven with `setprop`, so every wallpaper change animates.
/// </summary>
public sealed class Lively : ModuleBase, IWallpaperHost
{
    public override string Id => "lively";
    public override string Name => "Live Wallpapers";
    public override string Description => "Video, GIF, web and shader wallpapers with animated transitions. Powered by Lively Wallpaper.";
    public override string Glyph => "";
    public override ModuleCategory Category => ModuleCategory.Desktop;
    public override string Repo => "rocksdanister/lively";
    public override string License => "GPL-3.0";
    protected override string WingetId => "rocksdanister.LivelyWallpaper";
    protected override string ProcessName => "Lively";
    protected override string? ExePath => Sys.FirstExisting(@"%LocalAppData%\Programs\Lively Wallpaper\Lively.exe", @"%ProgramFiles%\Lively Wallpaper\Lively.exe");
    protected override string StartMenuName => "Lively Wallpaper";

    static readonly string[] Rules = ["pause", "ignore", "kill"];

    public override IReadOnlyList<SettingDef> Settings { get; } =
    [
        Choice("transition", "Transition", "grow", ["grow", "fade", "wipe", "slide", "outer", "random"], "\"grow\" expands from the mouse pointer, like swww.", "Transitions"),
        Slider("duration", "Transition length (ms)", 900, 200, 3000, 100, group: "Transitions"),
        Choice("arrangement", "Multiple monitors", "per", ["per", "span", "duplicate"], "per = a wallpaper per screen, span = one across all, duplicate = same on each.", "Screens"),
        Choice("AppFullscreenPause", "When a fullscreen app runs", "pause", Rules, group: "Pause rules"),
        Choice("AppFocusPause", "When another app is focused", "ignore", Rules, group: "Pause rules"),
        Choice("BatteryPause", "On battery", "pause", Rules, group: "Pause rules"),
        Choice("PowerSaveModePause", "In power saver mode", "pause", Rules, group: "Pause rules"),
        Toggle("mute", "Mute wallpaper audio", true, group: "Pause rules"),
    ];

    public bool IsActive => Store.Config.For(Id).Enabled && ExePath is not null;

    static string LivelyData => Path.Combine(Sys.LocalAppData, "Lively Wallpaper");
    static string SettingsPath => Path.Combine(LivelyData, "Settings.json");

    /// <summary>Lively only accepts folder wallpapers that live under its own library folder.</summary>
    static string SlideshowDir
    {
        get
        {
            var lib = Files.ReadJson(SettingsPath)["WallpaperDir"]?.ToString();
            if (string.IsNullOrEmpty(lib)) lib = Path.Combine(LivelyData, "Library");
            return Path.Combine(lib, "wallpapers", "hyprism-slideshow");
        }
    }

    // The command utility ships next to Lively.exe; Lively.exe itself forwards the same verbs to the running instance.
    string Cli => ExePath is { } exe
        ? new[] { "Livelycu.exe", "Lively.Utility.Commandline.exe", "Lively.exe" }.Select(n => Path.Combine(Path.GetDirectoryName(exe)!, n)).First(File.Exists)
        : throw new InvalidOperationException("Lively Wallpaper isn't installed.");

    Task Cmd(string args, CancellationToken ct = default) => Sys.RunAsync(Cli, args, ct);

    public override async Task ApplyAsync(JsonObject s, CancellationToken ct = default)
    {
        // Lively saves Settings.json on exit, so stop it before editing.
        bool running = Sys.IsRunning(ProcessName);
        if (running) { await Cmd("app --shutdown true", ct); await Task.Delay(1500, ct); Sys.Kill(ProcessName); }
        if (File.Exists(SettingsPath))
        {
            var json = Files.ReadJson(SettingsPath);
            foreach (var key in new[] { "AppFullscreenPause", "AppFocusPause", "BatteryPause", "PowerSaveModePause" })
                json[key] = Array.IndexOf(Rules, s.Str(this, key)) is var i and >= 0 ? i : 0; // Lively stores the enum as a number
            json["WallpaperArrangement"] = s.Str(this, "arrangement") switch { "span" => 1, "duplicate" => 2, _ => 0 };
            json["AudioVolumeGlobal"] = s.Bool(this, "mute") ? 0 : 100;
            Write(SettingsPath, json);
        }
        if (running || Store.Config.For(Id).Enabled) await StartAsync();
        WriteCurrent(null, s);
    }

    public async Task ShowAsync(string path, string transition, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        if (!Sys.IsRunning(ProcessName)) { await StartAsync(); await Task.Delay(3000, ct); }

        if (ext is ".mp4" or ".webm" or ".mkv" or ".gif")
        {
            await ForEachMonitor(m => Cmd($"setwp --file \"{path}\"{m}", ct)); // Lively imports media files itself
            Store.Config.For(Id).Settings["_slideshowActive"] = false;
            return;
        }

        var dir = InstallSlideshow();
        var images = Path.Combine(dir, "images");
        Directory.CreateDirectory(images);
        var name = $"{DateTime.Now.Ticks}{ext}";
        File.Copy(path, Path.Combine(images, name));
        // Keep the two newest: the one on screen and the one transitioning in.
        foreach (var old in new DirectoryInfo(images).GetFiles().OrderByDescending(f => f.CreationTimeUtc).Skip(2)) old.Delete();
        var s = CurrentSettings;
        s["transition"] = transition;
        WriteCurrent(name, s);

        var state = Store.Config.For(Id).Settings;
        if (state["_slideshowActive"]?.GetValue<bool>() != true)
        {
            await ForEachMonitor(m => Cmd($"setwp --file \"{dir}\"{m}", ct));
            state["_slideshowActive"] = true;
            Store.Save();
            return; // the page picks the image up from current.js
        }
        await ForEachMonitor(async m =>
        {
            await Cmd($"setprop --property transition={transition}{m}", ct);
            await Cmd($"setprop --property duration={(int)s.Num(this, "duration")}{m}", ct);
            await Cmd($"setprop --property image={name}{m}", ct);
        });
    }

    /// <summary>"per" arrangement needs one call per screen; span/duplicate take one call.</summary>
    async Task ForEachMonitor(Func<string, Task> run)
    {
        if (CurrentSettings.Str(this, "arrangement") != "per") { await run(""); return; }
        for (int i = 1; i <= Math.Max(1, GetSystemMetrics(80 /*SM_CMONITORS*/)); i++) await run($" --monitor {i}");
    }

    [DllImport("user32.dll")] static extern int GetSystemMetrics(int index);

    string InstallSlideshow()
    {
        var dir = SlideshowDir;
        Directory.CreateDirectory(dir);
        var asm = Assembly.GetExecutingAssembly();
        foreach (var res in asm.GetManifestResourceNames().Where(r => r.Contains(".slideshow.")))
        {
            var file = res[(res.IndexOf(".slideshow.", StringComparison.Ordinal) + ".slideshow.".Length)..];
            using var src = asm.GetManifestResourceStream(res)!;
            using var dst = File.Create(Path.Combine(dir, file));
            src.CopyTo(dst);
        }
        return dir;
    }

    void WriteCurrent(string? image, JsonObject s)
    {
        var dir = SlideshowDir;
        if (!Directory.Exists(dir)) return;
        var current = Path.Combine(dir, "current.js");
        var existing = File.Exists(current) ? File.ReadAllText(current) : "";
        image ??= System.Text.RegularExpressions.Regex.Match(existing, "image: \"([^\"]*)\"").Groups[1].Value;
        File.WriteAllText(current, $"window.HYPRISM = {{ image: \"{image}\", transition: \"{s.Str(this, "transition")}\" }};\n");
    }

    public override Task SetEffectsAsync(bool blur, bool animate, CancellationToken ct = default) =>
        Sys.IsRunning(ProcessName) ? Cmd($"app --play {(animate ? "true" : "false")}", ct) : Task.CompletedTask;
}
