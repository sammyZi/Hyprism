using System.Text.Json.Nodes;
using Hyprism.Core;

namespace Hyprism.Modules;

/// <summary>
/// Windows Auto Dark Mode switches light/dark on a schedule. Hyprism registers itself as an ADM script so that every
/// switch also swaps the Hyprism profile (wallpaper, terminal scheme, bar...) via `Hyprism.exe --theme light|dark`.
/// </summary>
public sealed class AutoDarkMode : ModuleBase
{
    public override string Id => "autodarkmode";
    public override string Name => "Auto Dark Mode";
    public override string Description => "Switch light and dark by time or at sunset, and switch Hyprism profiles with it. Powered by Auto Dark Mode.";
    public override string Glyph => "";
    public override ModuleCategory Category => ModuleCategory.Look;
    public override string Repo => "AutoDarkMode/Windows-Auto-Night-Mode";
    public override string License => "GPL-3.0";
    protected override string WingetId => "ArminOsaj.AutoDarkMode";
    protected override string ProcessName => "AutoDarkModeSvc";
    protected override string? ExePath => Sys.FirstExisting(@"%LocalAppData%\Programs\AutoDarkMode\adm-app\AutoDarkModeSvc.exe", @"%LocalAppData%\Programs\AutoDarkMode\AutoDarkModeSvc.exe");

    public override IReadOnlyList<SettingDef> Settings { get; } =
    [
        Choice("mode", "Switch", "sunset", ["sunset", "custom times"], "Sunset uses your location (Windows location services).", "Schedule"),
        Text("lightAt", "Light from", "07:00", "24-hour time, used with custom times.", "Schedule"),
        Text("darkAt", "Dark from", "19:00", group: "Schedule"),
        Toggle("hyprism", "Switch Hyprism profiles too", true, "Pick the light and dark profiles on the Profiles page.", "Hyprism"),
        Button("openAdm", "Open Auto Dark Mode", "For everything else: wallpapers per mode, cursors, Office...", "Hyprism"),
    ];

    static string ConfigDir => Path.Combine(Sys.RoamingAppData, "AutoDarkMode");

    public override Task ApplyAsync(JsonObject s, CancellationToken ct = default)
    {
        var config = Path.Combine(ConfigDir, "config.yaml");
        if (File.Exists(config))
        {
            var lines = File.ReadAllLines(config).ToList();
            SetYaml(lines, null, "AutoThemeSwitchingEnabled", "true");
            bool sunset = s.Str(this, "mode") == "sunset";
            SetYaml(lines, "Location", "Enabled", sunset ? "true" : "false");
            if (!sunset)
            {
                SetYaml(lines, null, "Sunrise", At(s.Str(this, "lightAt"), 7));
                SetYaml(lines, null, "Sunset", At(s.Str(this, "darkAt"), 19));
            }
            Write(config, string.Join("\r\n", lines) + "\r\n");
        }

        // ADM's script hook (scripts.yaml, see AutoDarkModeLib ScriptSwitchSettings).
        var exe = Environment.ProcessPath ?? "Hyprism.exe";
        var scripts = s.Bool(this, "hyprism")
            ? $"""
              Enabled: true
              Component:
                Scripts:
                - Name: Hyprism
                  Command: '{exe.Replace("'", "''")}'
                  WorkingDirectory: '{Path.GetDirectoryName(exe)!.Replace("'", "''")}'
                  ArgsLight: [--theme, light]
                  ArgsDark: [--theme, dark]
                  AllowedSources: [Any]
                  TimeoutMillis: 30000
              """
            : "Enabled: false\nComponent:\n  Scripts: []\n";
        Write(Path.Combine(ConfigDir, "scripts.yaml"), scripts);
        return Task.CompletedTask;
    }

    static string At(string hhmm, int fallbackHour) =>
        (TimeSpan.TryParse(hhmm, out var t) ? DateTime.Today + t : DateTime.Today.AddHours(fallbackHour)).ToString("yyyy-MM-ddTHH:mm:ss.fffffff");

    /// <summary>Sets a top-level key, or a key one level under <paramref name="parent"/>, in simple block YAML.</summary>
    public static void SetYaml(List<string> lines, string? parent, string key, string value)
    {
        int start = 0, end = lines.Count, indent = 0;
        if (parent is not null)
        {
            start = lines.FindIndex(l => l.TrimEnd() == parent + ":");
            if (start < 0) { lines.Add($"{parent}:"); lines.Add($"  {key}: {value}"); return; }
            start++;
            end = lines.FindIndex(start, l => l.Length > 0 && !char.IsWhiteSpace(l[0]));
            if (end < 0) end = lines.Count;
            indent = 2;
        }
        var prefix = new string(' ', indent) + key + ":";
        int i = lines.FindIndex(start, end - start, l => l.StartsWith(prefix, StringComparison.Ordinal));
        if (i >= 0) lines[i] = $"{prefix} {value}"; else lines.Insert(end, $"{prefix} {value}");
    }

    public override Task InvokeAsync(string key, CancellationToken ct = default)
    {
        if (key == "openAdm" && Sys.FirstExisting(@"%LocalAppData%\Programs\AutoDarkMode\AutoDarkModeApp.exe", @"%LocalAppData%\Programs\AutoDarkMode\adm-app\AutoDarkModeApp.exe") is { } app)
            Sys.Start(app);
        return Task.CompletedTask;
    }
}
