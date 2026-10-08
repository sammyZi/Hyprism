using System.Text.Json.Nodes;
using Hyprism.Core;
using Hyprism.Theming;

namespace Hyprism.Modules;

/// <summary>Flow Launcher (Rofi/Wofi-style). Hyprism writes a "Hyprism" theme from the palette and selects it.</summary>
public sealed class FlowLauncher : ModuleBase
{
    public override string Id => "flowlauncher";
    public override string Name => "App Launcher";
    public override string Description => "Rofi-style launcher for apps, files, calculations and web search, themed to match. Powered by Flow Launcher.";
    public override string Glyph => "";
    public override ModuleCategory Category => ModuleCategory.Productivity;
    public override string Repo => "Flow-Launcher/Flow.Launcher";
    public override string License => "MIT";
    protected override string WingetId => "Flow-Launcher.Flow-Launcher";
    protected override string ProcessName => "Flow.Launcher";
    protected override string? ExePath => Sys.FirstExisting(@"%LocalAppData%\FlowLauncher\Flow.Launcher.exe");

    public override IReadOnlyList<SettingDef> Settings { get; } =
    [
        Toggle("theme", "Use the Hyprism theme", true, "Generated from the current palette.", "Look"),
        Toggle("blur", "Blur behind the launcher", true, group: "Look"),
        Text("hotkey", "Open with", "Alt + Space", "Flow's own hotkey syntax, e.g. \"Alt + Space\" or \"LWin + Space\".", "Behavior"),
    ];

    static string DataDir => Path.Combine(Sys.RoamingAppData, "FlowLauncher");
    static string SettingsPath => Path.Combine(DataDir, "Settings", "Settings.json");

    public override async Task ApplyAsync(JsonObject s, CancellationToken ct = default)
    {
        // Flow saves Settings.json on exit, so stop it first.
        bool running = Sys.IsRunning(ProcessName);
        if (running) { Sys.Kill(ProcessName); await Task.Delay(300, ct); }
        if (s.Bool(this, "theme")) Write(Path.Combine(DataDir, "Themes", "Hyprism.xaml"), Generators.FlowLauncherTheme(CurrentPalette, s.Bool(this, "blur")));
        if (File.Exists(SettingsPath))
        {
            var json = Files.ReadJson(SettingsPath);
            if (s.Bool(this, "theme")) json["Theme"] = "Hyprism";
            json["Hotkey"] = s.Str(this, "hotkey");
            Write(SettingsPath, json);
        }
        if (running || Store.Config.For(Id).Enabled) await StartAsync();
    }

    public override Task ApplyPaletteAsync(Palette p, CancellationToken ct = default) =>
        CurrentSettings.Bool(this, "theme") && ExePath is not null ? ApplyAsync(CurrentSettings, ct) : Task.CompletedTask;
}
