using System.Text;
using System.Text.Json.Nodes;
using Hyprism.Core;
using Hyprism.Theming;

namespace Hyprism.Modules;

/// <summary>
/// Hyprland-style tiling with GlazeWM or komorebi (+ whkd for hotkeys). Hyprism owns the generated config files and
/// keeps one engine-neutral keybinding list, translated to each engine's command syntax.
/// Borders: native in both engines (Windows 11), or tacky-borders for rounded/gradient borders and Windows 10.
/// </summary>
public sealed class Tiling : ModuleBase, IToggleable
{
    public override string Id => "tiling";
    public override string Name => "Tiling";
    public override string Description => "Hyprland-style tiling with gaps, workspaces, keybindings and colored borders. Powered by GlazeWM or komorebi.";
    public override string Glyph => "";
    public override ModuleCategory Category => ModuleCategory.Desktop;
    public override string Repo => Engine == "komorebi" ? "LGUG2Z/komorebi" : "glzr-io/glazewm";
    public override string License => Engine == "komorebi" ? "Komorebi License 2.0 (source-available, free for personal use)" : "GPL-3.0";

    string Engine => Store.Config.For(Id).Settings["engine"]?.ToString() ?? "glazewm";
    protected override string WingetId => Engine == "komorebi" ? "LGUG2Z.komorebi" : "glzr-io.glazewm";
    protected override string ProcessName => Engine == "komorebi" ? "komorebi" : "glazewm";
    protected override string? ExePath => Engine == "komorebi"
        ? Sys.FirstExisting(@"%ProgramFiles%\komorebi\bin\komorebic.exe")
        : Sys.FirstExisting(@"%ProgramFiles%\glzr.io\GlazeWM\glazewm.exe");
    protected override string ExeArgs => Engine == "komorebi" ? "start --whkd" : "start";

    /// <summary>The engine-neutral action vocabulary shown in the keybinding editor.</summary>
    public static readonly string[] Actions =
    [
        "focus left", "focus right", "focus up", "focus down",
        "move left", "move right", "move up", "move down",
        .. Enumerable.Range(1, 9).Select(i => $"workspace {i}"),
        .. Enumerable.Range(1, 9).Select(i => $"move to workspace {i}"),
        "toggle floating", "toggle fullscreen", "toggle monocle", "minimize", "close", "pause", "reload", "launch terminal",
    ];

    static readonly JsonArray DefaultBindings = Bindings(
        ("alt+h", "focus left"), ("alt+l", "focus right"), ("alt+k", "focus up"), ("alt+j", "focus down"),
        ("alt+shift+h", "move left"), ("alt+shift+l", "move right"), ("alt+shift+k", "move up"), ("alt+shift+j", "move down"),
        ("alt+1", "workspace 1"), ("alt+2", "workspace 2"), ("alt+3", "workspace 3"), ("alt+4", "workspace 4"), ("alt+5", "workspace 5"),
        ("alt+shift+1", "move to workspace 1"), ("alt+shift+2", "move to workspace 2"), ("alt+shift+3", "move to workspace 3"),
        ("alt+shift+4", "move to workspace 4"), ("alt+shift+5", "move to workspace 5"),
        ("alt+shift+space", "toggle floating"), ("alt+f", "toggle fullscreen"), ("alt+m", "minimize"),
        ("alt+shift+q", "close"), ("alt+shift+p", "pause"), ("alt+shift+r", "reload"), ("alt+enter", "launch terminal"));

    static JsonArray Bindings(params (string Keys, string Action)[] b) =>
        new(b.Select(x => (JsonNode)new JsonObject { ["keys"] = x.Keys, ["action"] = x.Action }).ToArray());

    public override IReadOnlyList<SettingDef> Settings { get; } =
    [
        Choice("engine", "Tiling engine", "glazewm", ["glazewm", "komorebi"], "GlazeWM is i3-like and GPL-3.0. komorebi is bspwm-like; its license is free for personal use only.", "Engine"),
        Slider("workspaces", "Workspaces", 5, 1, 9, 1, group: "Layout"),
        Slider("innerGap", "Gap between windows (px)", 10, 0, 40, 1, group: "Layout"),
        Slider("outerGap", "Gap at screen edges (px)", 10, 0, 60, 1, group: "Layout"),
        Slider("topGap", "Extra top gap for a status bar (px)", 40, 0, 80, 1, group: "Layout"),
        Choice("layout", "komorebi layout", "BSP", ["BSP", "Columns", "Rows", "VerticalStack", "HorizontalStack", "UltrawideVerticalStack", "Grid", "RightMainVerticalStack"], group: "Layout"),
        Toggle("focusFollowsMouse", "Focus follows mouse", false, group: "Layout"),
        Toggle("borders", "Active window border", true, group: "Borders"),
        Choice("borderEngine", "Border renderer", "native", ["native", "tacky-borders"], "tacky-borders adds rounded/gradient borders and works on Windows 10.", "Borders"),
        Slider("borderWidth", "Border width", 2, 1, 8, 1, group: "Borders"),
        Toggle("roundedBorders", "Rounded corners", true, group: "Borders"),
        Toggle("paletteBorders", "Use theme colors", true, group: "Borders"),
        Color("activeColor", "Active border", "#CBA6F7", group: "Borders"),
        Color("inactiveColor", "Inactive border", "#45475A", group: "Borders"),
        Lines("floating", "Always float these apps", "Calculator\nTaskmgr\nPowerToys.ColorPickerUI\n", "One process name per line.", "Rules"),
        new("keybindings", "Keybindings", SettingKind.KeyBindings, DefaultBindings, "Record a shortcut, then pick what it does.", Group: "Keybindings"),
        Text("terminal", "Terminal command", "wt", "Used by \"launch terminal\".", "Keybindings"),
    ];

    public override async Task InstallAsync(IProgress<string>? log = null, CancellationToken ct = default)
    {
        await base.InstallAsync(log, ct);
        if (Engine == "komorebi") await Sys.WingetInstallAsync("LGUG2Z.whkd", "winget", log, ct);
        if (CurrentSettings.Str(this, "borderEngine") == "tacky-borders")
            await Sys.InstallGitHubReleaseAsync("luke-you/tacky-borders", @"\.zip$", TackyDir, log, ct);
    }

    static string TackyDir => Path.Combine(Sys.ToolsDir, "tacky-borders");
    static string? TackyExe => Directory.Exists(TackyDir) ? Directory.EnumerateFiles(TackyDir, "tacky-borders.exe", SearchOption.AllDirectories).FirstOrDefault() : null;

    static string GlazeConfig => Path.Combine(Sys.UserProfile, ".glzr", "glazewm", "config.yaml");
    static string KomorebiConfig => Path.Combine(Environment.GetEnvironmentVariable("KOMOREBI_CONFIG_HOME") ?? Sys.UserProfile, "komorebi.json");
    static string Whkdrc => Path.Combine(Environment.GetEnvironmentVariable("WHKD_CONFIG_HOME") ?? Path.Combine(Sys.UserProfile, ".config"), "whkdrc");
    static string TackyConfig => Path.Combine(Sys.UserProfile, ".config", "tacky-borders", "config.yaml");

    (Rgb Active, Rgb Inactive) BorderColors(JsonObject s) => s.Bool(this, "paletteBorders")
        ? (CurrentPalette.Accent, CurrentPalette.Overlay)
        : (Rgb.Parse(s.Str(this, "activeColor")), Rgb.Parse(s.Str(this, "inactiveColor")));

    IEnumerable<(string Keys, string Action)> ReadBindings(JsonObject s) =>
        (s.Raw(this, "keybindings") as JsonArray ?? DefaultBindings)
        .Select(b => ((string?)b?["keys"] ?? "", (string?)b?["action"] ?? ""))
        .Where(b => b.Item1.Length > 0 && Actions.Contains(b.Item2));

    public override async Task ApplyAsync(JsonObject s, CancellationToken ct = default)
    {
        bool tacky = s.Bool(this, "borders") && s.Str(this, "borderEngine") == "tacky-borders";
        // Switching engines: stop the other one so two window managers never fight.
        if (Engine == "komorebi" && Sys.IsRunning("glazewm")) Sys.Kill("glazewm");
        if (Engine != "komorebi" && Sys.IsRunning("komorebi")) { await Sys.RunAsync("komorebic", "stop --whkd", ct); }
        if (Engine == "komorebi") { WriteKomorebi(s, tacky); await Sys.RunAsync("komorebic", "reload-configuration", ct); }
        else { WriteGlaze(s, tacky); if (Sys.IsRunning("glazewm") && ExePath is { } exe) await Sys.RunAsync(exe, "command wm-reload-config", ct); }

        if (tacky) WriteTacky(s);
        if (tacky && TackyExe is { } t && Store.Config.For(Id).Enabled && !Sys.IsRunning("tacky-borders")) Sys.Start(t);
        if (!tacky) Sys.Kill("tacky-borders");
    }

    public override Task ApplyPaletteAsync(Palette p, CancellationToken ct = default) =>
        CurrentSettings.Bool(this, "paletteBorders") && ExePath is not null ? ApplyAsync(CurrentSettings, ct) : Task.CompletedTask;

    // ---------- GlazeWM ----------

    /// <summary>Hyprism action -> GlazeWM commands.</summary>
    public static string[] GlazeCommands(string action, string terminal) => action switch
    {
        _ when action.StartsWith("focus ") => [$"focus --direction {action[6..]}"],
        _ when action.StartsWith("move to workspace ") => [$"move --workspace {action[18..]}", $"focus --workspace {action[18..]}"],
        _ when action.StartsWith("move ") => [$"move --direction {action[5..]}"],
        _ when action.StartsWith("workspace ") => [$"focus --workspace {action[10..]}"],
        "toggle floating" => ["toggle-floating --centered"],
        "toggle fullscreen" => ["toggle-fullscreen"],
        "toggle monocle" => ["toggle-fullscreen"],
        "minimize" => ["toggle-minimized"],
        "close" => ["close"],
        "pause" => ["wm-toggle-pause"],
        "reload" => ["wm-reload-config"],
        "launch terminal" => [$"shell-exec {terminal}"],
        _ => [],
    };

    void WriteGlaze(JsonObject s, bool tacky)
    {
        var (active, inactive) = BorderColors(s);
        bool native = s.Bool(this, "borders") && !tacky;
        string Q(string v) => "'" + v.Replace("'", "''") + "'";
        int inner = (int)s.Num(this, "innerGap"), outer = (int)s.Num(this, "outerGap"), top = outer + (int)s.Num(this, "topGap");
        var y = new StringBuilder();
        y.AppendLine("# Generated by Hyprism. Edit from Hyprism > Tiling; manual changes are overwritten.");
        y.AppendLine("general:");
        y.AppendLine("  startup_commands: []");
        y.AppendLine("  shutdown_commands: []");
        y.AppendLine("  config_reload_commands: []");
        y.AppendLine($"  focus_follows_cursor: {(s.Bool(this, "focusFollowsMouse") ? "true" : "false")}");
        y.AppendLine("  toggle_workspace_on_refocus: false");
        y.AppendLine("  cursor_jump:\n    enabled: true\n    trigger: 'monitor_focus'");
        y.AppendLine("  hide_method: 'cloak'\n  show_all_in_taskbar: false");
        y.AppendLine("gaps:\n  scale_with_dpi: true");
        y.AppendLine($"  inner_gap: '{inner}px'");
        y.AppendLine($"  outer_gap:\n    top: '{top}px'\n    right: '{outer}px'\n    bottom: '{outer}px'\n    left: '{outer}px'");
        y.AppendLine("window_effects:");
        foreach (var (name, color) in new[] { ("focused_window", active), ("other_windows", inactive) })
        {
            y.AppendLine($"  {name}:");
            y.AppendLine($"    border:\n      enabled: {(native ? "true" : "false")}\n      color: '{color.Hex}'");
            y.AppendLine("    hide_title_bar:\n      enabled: false");
            y.AppendLine($"    corner_style:\n      enabled: true\n      style: '{(s.Bool(this, "roundedBorders") ? "rounded" : "square")}'");
        }
        y.AppendLine("window_behavior:\n  initial_state: 'tiling'\n  state_defaults:\n    floating:\n      centered: true\n      shown_on_top: false\n    fullscreen:\n      maximized: false\n      shown_on_top: false");
        y.AppendLine("workspaces:");
        for (int i = 1; i <= (int)s.Num(this, "workspaces"); i++) y.AppendLine($"  - name: '{i}'");
        y.AppendLine("window_rules:\n  - commands: ['ignore']\n    match:");
        foreach (var p in new[] { "zebar", "yasb", "Lively", "tacky-borders", "Flow.Launcher", "Hyprism" })
            y.AppendLine($"      - window_process: {{ equals: {Q(p)} }}");
        var floating = s.Lines(this, "floating").ToList();
        if (floating.Count > 0)
        {
            y.AppendLine("  - commands: ['set-floating --centered']\n    match:");
            foreach (var p in floating) y.AppendLine($"      - window_process: {{ equals: {Q(Path.GetFileNameWithoutExtension(p))} }}");
        }
        y.AppendLine("keybindings:");
        foreach (var group in ReadBindings(s).GroupBy(b => b.Action))
        {
            var cmds = GlazeCommands(group.Key, s.Str(this, "terminal"));
            if (cmds.Length == 0) continue;
            y.AppendLine($"  - commands: [{string.Join(", ", cmds.Select(Q))}]");
            y.AppendLine($"    bindings: [{string.Join(", ", group.Select(b => Q(b.Keys)))}]");
        }
        Write(GlazeConfig, y.ToString());
    }

    // ---------- komorebi + whkd ----------

    /// <summary>Hyprism action -> komorebic command (workspaces are 0-based there).</summary>
    public static string? KomorebiCommand(string action, string terminal) => action switch
    {
        _ when action.StartsWith("focus ") => $"komorebic focus {action[6..]}",
        _ when action.StartsWith("move to workspace ") => $"komorebic move-to-workspace {int.Parse(action[18..]) - 1}",
        _ when action.StartsWith("move ") => $"komorebic move {action[5..]}",
        _ when action.StartsWith("workspace ") => $"komorebic focus-workspace {int.Parse(action[10..]) - 1}",
        "toggle floating" => "komorebic toggle-float",
        "toggle fullscreen" => "komorebic toggle-maximize",
        "toggle monocle" => "komorebic toggle-monocle",
        "minimize" => "komorebic minimize",
        "close" => "komorebic close",
        "pause" => "komorebic toggle-pause",
        "reload" => "komorebic reload-configuration",
        "launch terminal" => $"Start-Process {terminal}",
        _ => null,
    };

    /// <summary>"alt+shift+enter" -> "alt + shift + return" (whkd key names).</summary>
    public static string WhkdKeys(string keys) => string.Join(" + ", keys.Split('+').Select(k => k.Trim().ToLowerInvariant() switch
    {
        "enter" => "return", "win" or "super" => "win", "ctrl" or "control" => "ctrl", var k2 => k2,
    }));

    void WriteKomorebi(JsonObject s, bool tacky)
    {
        var (active, inactive) = BorderColors(s);
        var json = Files.ReadJson(KomorebiConfig); // keep keys Hyprism doesn't manage
        json["$schema"] = "https://raw.githubusercontent.com/LGUG2Z/komorebi/master/schema.json";
        json["default_workspace_padding"] = (int)s.Num(this, "outerGap");
        json["default_container_padding"] = (int)s.Num(this, "innerGap");
        json["mouse_follows_focus"] = false;
        if (s.Bool(this, "focusFollowsMouse")) json["focus_follows_mouse"] = "Windows"; else json.Remove("focus_follows_mouse");
        json["border"] = s.Bool(this, "borders") && !tacky;
        json["border_width"] = (int)s.Num(this, "borderWidth");
        json["border_offset"] = -1;
        json["border_style"] = s.Bool(this, "roundedBorders") ? "Rounded" : "Square";
        json["border_colours"] = new JsonObject
        {
            ["single"] = active.Hex, ["stack"] = active.Hex, ["monocle"] = active.Hex,
            ["floating"] = active.Hex, ["unfocused"] = inactive.Hex,
        };
        json.Remove("theme"); // a theme overrides border_colours
        // komorebi subtracts bottom from the height, so a top bar needs top == bottom.
        json["global_work_area_offset"] = new JsonObject { ["left"] = 0, ["top"] = (int)s.Num(this, "topGap"), ["right"] = 0, ["bottom"] = (int)s.Num(this, "topGap") };
        json["floating_applications"] = new JsonArray(s.Lines(this, "floating")
            .Select(p => (JsonNode)new JsonObject { ["kind"] = "Exe", ["id"] = p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? p : p + ".exe", ["matching_strategy"] = "Equals" }).ToArray());
        var workspaces = new JsonArray(Enumerable.Range(1, (int)s.Num(this, "workspaces"))
            .Select(i => (JsonNode)new JsonObject { ["name"] = i.ToString(), ["layout"] = s.Str(this, "layout") }).ToArray());
        json["monitors"] = new JsonArray(new JsonObject { ["workspaces"] = workspaces });
        Write(KomorebiConfig, json);

        var rc = new StringBuilder(".shell pwsh\n\n# Generated by Hyprism. Edit from Hyprism > Tiling.\n");
        foreach (var (keys, action) in ReadBindings(s))
            if (KomorebiCommand(action, s.Str(this, "terminal")) is { } cmd) rc.AppendLine($"{WhkdKeys(keys),-24}: {cmd}");
        Write(Whkdrc, rc.ToString());
        if (Sys.IsRunning("whkd")) { Sys.Kill("whkd"); Sys.Start("whkd"); }
    }

    // ---------- tacky-borders ----------

    void WriteTacky(JsonObject s)
    {
        var (active, inactive) = BorderColors(s);
        var acc2 = CurrentPalette.Ansi[12]; // bright blue: a second gradient stop that matches the theme
        Write(TackyConfig, $"""
            # Generated by Hyprism.
            watch_config_changes: True
            enable_logging: False
            enable_ipc_server: True
            rendering_backend: V2
            global:
              border_width: {(int)s.Num(this, "borderWidth")}
              border_offset: -1
              border_radius: {(s.Bool(this, "roundedBorders") ? "Auto" : "Square")}
              border_z_order: AboveWindow
              follow_native_border: True
              initialize_delay: 200
              unminimize_delay: 150
              active_color:
                colors: ["{active.Hex}", "{acc2.Hex}"]
                direction: 45deg
              inactive_color:
                colors: ["{inactive.Hex}"]
              komorebi_colors:
                enabled: False
            """);
    }

    public override async Task DisableAsync(CancellationToken ct = default)
    {
        if (Engine == "komorebi") await Sys.RunAsync("komorebic", "stop --whkd", ct);
        else if (ExePath is { } exe && Sys.IsRunning("glazewm")) await Sys.RunAsync(exe, "command wm-exit", ct);
        Sys.Kill("tacky-borders");
        await base.DisableAsync(ct);
    }

    protected override async Task StartAsync()
    {
        await base.StartAsync();
        if (CurrentSettings.Bool(this, "borders") && CurrentSettings.Str(this, "borderEngine") == "tacky-borders" && TackyExe is { } t && !Sys.IsRunning("tacky-borders"))
            Sys.Start(t);
    }

    public async Task ToggleAsync(CancellationToken ct = default)
    {
        if (Sys.IsRunning(ProcessName)) await DisableAsync(ct);
        else await EnableAsync(ct);
    }
}
