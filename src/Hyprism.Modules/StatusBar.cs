using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using Hyprism.Core;
using Hyprism.Theming;

namespace Hyprism.Modules;

/// <summary>
/// Waybar-style top bar with Zebar (Hyprism ships its own widget pack) or YASB (Hyprism writes config.yaml + styles.css).
/// Both are themed from the palette.
/// </summary>
public sealed class StatusBar : ModuleBase
{
    public override string Id => "statusbar";
    public override string Name => "Top Bar";
    public override string Description => "Waybar-style status bar with workspaces, clock, media, battery, network and volume. Powered by Zebar or YASB.";
    public override string Glyph => "";
    public override ModuleCategory Category => ModuleCategory.Desktop;
    public override string Repo => Bar == "yasb" ? "amnweb/yasb" : "glzr-io/zebar";
    public override string License => Bar == "yasb" ? "MIT" : "GPL-3.0";

    string Bar => Store.Config.For(Id).Settings["bar"]?.ToString() ?? "zebar";
    protected override string WingetId => Bar == "yasb" ? "AmN.yasb" : "glzr-io.zebar";
    protected override string ProcessName => Bar == "yasb" ? "yasb" : "zebar";
    protected override string? ExePath => Bar == "yasb"
        ? Sys.FirstExisting(@"%ProgramFiles%\YASB\yasb.exe", @"%LocalAppData%\Programs\YASB\yasb.exe")
        : Sys.FirstExisting(@"%ProgramFiles%\glzr.io\Zebar\zebar.exe");

    public override IReadOnlyList<SettingDef> Settings { get; } =
    [
        Choice("bar", "Bar", "zebar", ["zebar", "yasb"], "Zebar: web widgets, pairs with GlazeWM. YASB: Python/Qt with many built-in widgets.", "Bar"),
        Choice("wm", "Workspaces from", "glazewm", ["glazewm", "komorebi", "none"], group: "Bar"),
        Slider("height", "Height (px)", 36, 24, 56, 1, group: "Bar"),
        Slider("margin", "Floating margin (px)", 8, 0, 24, 1, "0 docks the bar to the screen edge.", "Bar"),
        Slider("radius", "Corner radius (px)", 12, 0, 24, 1, group: "Bar"),
        Slider("opacity", "Background opacity", 70, 0, 100, 5, group: "Bar"),
        Toggle("blur", "Blur behind the bar (YASB)", true, group: "Bar"),
        Text("font", "Font", "Google Sans Flex", group: "Bar"),
        Toggle("workspaces", "Workspaces", true, group: "Widgets"),
        Toggle("clock", "Clock", true, group: "Widgets"),
        Text("clockFormat", "Clock format", "EEE d MMM  HH:mm", "Zebar uses Luxon tokens, YASB uses strftime (%a %d %b %H:%M).", "Widgets"),
        Toggle("media", "Now playing", true, group: "Widgets"),
        Toggle("battery", "Battery", true, group: "Widgets"),
        Toggle("network", "Network", true, group: "Widgets"),
        Toggle("volume", "Volume", true, group: "Widgets"),
        Toggle("cpu", "CPU and memory", false, group: "Widgets"),
    ];

    static string ZebarDir => Path.Combine(Sys.UserProfile, ".glzr", "zebar");
    static string YasbDir => Path.Combine(Environment.GetEnvironmentVariable("YASB_CONFIG_HOME") ?? Path.Combine(Sys.UserProfile, ".config", "yasb"));

    public override async Task ApplyAsync(JsonObject s, CancellationToken ct = default)
    {
        // Only one bar at a time.
        Sys.Kill(Bar == "yasb" ? "zebar" : "yasb");
        bool changed = Bar == "yasb" ? WriteYasb(s) : WriteZebar(s);
        if (changed) await RestartIfRunningAsync();
    }

    public override Task ApplyPaletteAsync(Palette p, CancellationToken ct = default) =>
        ExePath is null ? Task.CompletedTask : ApplyAsync(CurrentSettings, ct);

    string Css(JsonObject s)
    {
        var p = CurrentPalette;
        byte a = (byte)Math.Round(s.Num(this, "opacity") * 2.55);
        return Generators.CssVariables(p) + $":root {{ --bar-bg: {p.Background.HexRgba(a)}; --radius: {(int)s.Num(this, "radius")}px; --font: '{s.Str(this, "font")}'; --margin: {(int)s.Num(this, "margin")}px; }}\n";
    }

    bool WriteZebar(JsonObject s)
    {
        var pack = Path.Combine(ZebarDir, "hyprism");
        int h = (int)s.Num(this, "height"), m = (int)s.Num(this, "margin");
        var zpack = new JsonObject
        {
            ["$schema"] = "https://github.com/glzr-io/zebar/raw/v3.0.0/resources/zpack-schema.json",
            ["name"] = "hyprism",
            ["version"] = "1.0.0",
            ["description"] = "Hyprism top bar, themed from your wallpaper.",
            ["tags"] = new JsonArray("topbar"),
            ["previewImages"] = new JsonArray(),
            ["widgets"] = new JsonArray(new JsonObject
            {
                ["name"] = "bar",
                ["htmlPath"] = "./bar.html",
                ["zOrder"] = "normal",
                ["shownInTaskbar"] = false,
                ["focused"] = false,
                ["resizable"] = false,
                ["transparent"] = true,
                ["includeFiles"] = new JsonArray("*.html", "*.css", "*.js"),
                ["privileges"] = new JsonObject { ["shellCommands"] = new JsonArray() },
                ["presets"] = new JsonArray(new JsonObject
                {
                    // Zebar sizes take px or %, not calc(): the window spans the screen and theme.css pads the bar inside it.
                    ["name"] = "default", ["anchor"] = "top_left", ["offsetX"] = "0px", ["offsetY"] = "0px",
                    ["width"] = "100%", ["height"] = $"{h + m}px",
                    ["monitorSelection"] = new JsonObject { ["type"] = "all" },
                    ["dockToEdge"] = new JsonObject { ["enabled"] = true, ["edge"] = "top", ["windowMargin"] = "0px" },
                }),
            }),
        };
        bool changed = Write(Path.Combine(pack, "zpack.json"), zpack);
        changed |= Write(Path.Combine(pack, "bar.html"), Resource("zebar.bar.html"));
        changed |= Write(Path.Combine(pack, "theme.css"), Css(s) + ZebarStyles);
        var cfg = new JsonObject
        {
            ["wm"] = s.Str(this, "wm"), ["clockFormat"] = s.Str(this, "clockFormat"),
            ["workspaces"] = s.Bool(this, "workspaces"), ["clock"] = s.Bool(this, "clock"), ["media"] = s.Bool(this, "media"),
            ["battery"] = s.Bool(this, "battery"), ["network"] = s.Bool(this, "network"), ["volume"] = s.Bool(this, "volume"), ["cpu"] = s.Bool(this, "cpu"),
        };
        changed |= Write(Path.Combine(pack, "config.js"), $"window.HYPRISM_BAR = {cfg.ToJsonString()};\n");

        // Start our widget when Zebar launches, keeping the user's other startup widgets.
        var settingsPath = Path.Combine(ZebarDir, "settings.json");
        var settings = Files.ReadJson(settingsPath);
        var startup = settings["startupConfigs"] as JsonArray ?? [];
        if (!startup.Any(c => (string?)c?["pack"] == "hyprism"))
        {
            // The starter bar would overlap ours.
            foreach (var starter in startup.Where(c => (string?)c?["pack"] == "starter").ToList()) startup.Remove(starter);
            startup.Add(new JsonObject { ["pack"] = "hyprism", ["widget"] = "bar", ["preset"] = "default" });
            settings["startupConfigs"] = startup.DeepClone();
            changed |= Write(settingsPath, settings);
        }
        return changed;
    }

    const string ZebarStyles = """
        * { box-sizing: border-box; }
        html, body { margin: 0; height: 100%; overflow: hidden; background: transparent; }
        body { padding: var(--margin) var(--margin) 0; }
        body { font: 500 13px var(--font), 'Segoe UI Variable Text', sans-serif; color: var(--fg); user-select: none; }
        .bar {
          height: 100%; display: grid; grid-template-columns: 1fr auto 1fr; align-items: center; gap: 12px;
          padding: 0 10px; background: var(--bar-bg); border: 1px solid var(--overlay); border-radius: var(--radius);
        }
        .left, .right { display: flex; align-items: center; gap: 6px; }
        .right { justify-content: flex-end; }
        .center { display: flex; align-items: center; gap: 10px; }
        .logo { color: var(--accent); margin-right: 6px; }
        .ws {
          min-width: 26px; height: 22px; padding: 0 8px; border: 0; border-radius: 11px; font: inherit;
          background: transparent; color: var(--muted); cursor: pointer; transition: all .2s ease;
        }
        .ws.shown { color: var(--fg); background: var(--surface); }
        .ws.focused { color: var(--on-accent); background: var(--accent); min-width: 40px; }
        .pill { display: inline-flex; align-items: center; gap: 6px; padding: 2px 10px; border-radius: 11px; background: var(--surface); border: 0; color: var(--fg); font: inherit; }
        .pill i { color: var(--accent); }
        .clock { font-weight: 600; letter-spacing: .02em; }
        .media { max-width: 360px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; cursor: pointer; }
        """;

    bool WriteYasb(JsonObject s)
    {
        var y = new StringBuilder();
        y.AppendLine("# Generated by Hyprism. Edit from Hyprism > Top Bar.");
        y.AppendLine("watch_stylesheet: true\nwatch_config: true\ndebug: false");
        y.AppendLine("bars:\n  primary-bar:\n    enabled: true\n    screens: ['*']\n    class_name: \"yasb-bar\"\n    alignment:\n      position: \"top\"\n      center: false");
        y.AppendLine($"    blur_effect:\n      enabled: {(s.Bool(this, "blur") ? "true" : "false")}\n      acrylic: false\n      dark_mode: {(CurrentPalette.IsDark ? "true" : "false")}\n      round_corners: {(s.Num(this, "radius") > 0 ? "true" : "false")}\n      border_color: None");
        y.AppendLine("    window_flags:\n      always_on_top: false\n      windows_app_bar: true");
        int m = (int)s.Num(this, "margin");
        y.AppendLine($"    dimensions:\n      width: \"100%\"\n      height: {(int)s.Num(this, "height")}");
        y.AppendLine($"    padding:\n      top: {m}\n      left: {m}\n      bottom: 0\n      right: {m}");

        var left = new List<string>(); var center = new List<string>(); var right = new List<string>();
        var wm = s.Str(this, "wm");
        if (s.Bool(this, "workspaces") && wm == "glazewm") left.Add("glazewm_workspaces");
        if (s.Bool(this, "workspaces") && wm == "komorebi") left.Add("komorebi_workspaces");
        if (s.Bool(this, "clock")) center.Add("clock");
        if (s.Bool(this, "media")) center.Add("media");
        if (s.Bool(this, "cpu")) { right.Add("cpu"); right.Add("memory"); }
        if (s.Bool(this, "network")) right.Add("wifi");
        if (s.Bool(this, "volume")) right.Add("volume");
        if (s.Bool(this, "battery")) right.Add("battery");
        string L(List<string> l) => "[" + string.Join(", ", l.Select(x => $"\"{x}\"")) + "]";
        y.AppendLine($"    widgets:\n      left: {L(left)}\n      center: {L(center)}\n      right: {L(right)}");

        y.AppendLine("widgets:");
        var clock = s.Str(this, "clockFormat");
        if (!clock.Contains('%')) clock = "%a %d %b  %H:%M"; // the default is a Zebar (Luxon) format
        y.AppendLine($"  clock:\n    type: \"yasb.clock.ClockWidget\"\n    options:\n      label: \"{{{clock}}}\"\n      label_alt: \"{{%A %d %B %Y  %H:%M:%S}}\"");
        y.AppendLine("  glazewm_workspaces:\n    type: \"glazewm.workspaces.GlazewmWorkspacesWidget\"\n    options:\n      offline_label: \"\"\n      hide_if_offline: true");
        y.AppendLine("  komorebi_workspaces:\n    type: \"komorebi.workspaces.WorkspaceWidget\"\n    options:\n      label_offline: \"\"\n      hide_if_offline: true\n      label_workspace_btn: \"{index}\"\n      label_workspace_active_btn: \"{index}\"\n      label_workspace_populated_btn: \"{index}\"");
        y.AppendLine("  media:\n    type: \"yasb.media.MediaWidget\"\n    options:\n      label: \"{title}{s}{artist}\"\n      label_alt: \"{title}\"\n      separator: \" · \"\n      hide_empty: true\n      max_field_size:\n        label: 30\n        label_alt: 40\n      show_thumbnail: false");
        y.AppendLine("  battery:\n    type: \"yasb.battery.BatteryWidget\"\n    options:\n      label: \"<span>{icon}</span> {percent}%\"\n      label_alt: \"{percent}% | {time_remaining}\"\n      hide_unsupported: true");
        y.AppendLine("  wifi:\n    type: \"yasb.wifi.WifiWidget\"\n    options:\n      label: \"<span>{wifi_icon}</span>\"\n      label_alt: \"{wifi_name} {wifi_strength}%\"");
        y.AppendLine("  volume:\n    type: \"yasb.volume.VolumeWidget\"\n    options:\n      label: \"<span>{icon}</span> {level}\"\n      label_alt: \"{volume}\"");
        y.AppendLine("  cpu:\n    type: \"yasb.cpu.CpuWidget\"\n    options:\n      label: \"<span>\\uf4bc</span> {info[percent][total]}%\"\n      label_alt: \"{info[freq][current]} MHz\"");
        y.AppendLine("  memory:\n    type: \"yasb.memory.MemoryWidget\"\n    options:\n      label: \"<span>\\uefc5</span> {virtual_mem_percent}%\"\n      label_alt: \"{virtual_mem_used}/{virtual_mem_total}\"");

        bool changed = Write(Path.Combine(YasbDir, "config.yaml"), y.ToString());
        changed |= Write(Path.Combine(YasbDir, "styles.css"), YasbCss(s));
        return changed;
    }

    string YasbCss(JsonObject s)
    {
        var p = CurrentPalette;
        byte a = (byte)Math.Round(s.Num(this, "opacity") * 2.55);
        // YASB's Qt stylesheet engine doesn't support var(); write literal colors.
        return $$"""
            /* Generated by Hyprism from palette "{{p.Name}}". */
            * { font-family: "{{s.Str(this, "font")}}", "Segoe UI Variable Text"; font-size: 13px; color: {{p.Foreground.Hex}}; }
            .yasb-bar { background-color: {{p.Background.HexRgba(a)}}; border-radius: {{(int)s.Num(this, "radius")}}px; border: 1px solid {{p.Overlay.Hex}}; }
            .widget { padding: 0 8px; margin: 4px 2px; border-radius: 10px; }
            .widget .label { padding: 0 2px; }
            .widget .icon, span { color: {{p.Accent.Hex}}; }
            .clock-widget .label { font-weight: 600; color: {{p.Foreground.Hex}}; }
            .glazewm-workspaces .ws-btn, .komorebi-workspaces .ws-btn { background: transparent; color: {{p.Muted.Hex}}; border: none; border-radius: 10px; padding: 0 8px; margin: 0 2px; }
            .glazewm-workspaces .ws-btn.populated, .komorebi-workspaces .ws-btn.populated { color: {{p.Foreground.Hex}}; background: {{p.Surface.Hex}}; }
            .glazewm-workspaces .ws-btn.active_populated, .glazewm-workspaces .ws-btn.active_empty, .komorebi-workspaces .ws-btn.active { color: {{p.OnAccent.Hex}}; background: {{p.Accent.Hex}}; }
            .media-widget .label { color: {{p.Foreground.Hex}}; }
            """;
    }

    static string Resource(string name)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var r = new StreamReader(asm.GetManifestResourceStream($"Hyprism.Modules.Assets.{name}")!);
        return r.ReadToEnd();
    }
}
