using System.Text.Json.Nodes;
using Hyprism.Core;
using Hyprism.Theming;

namespace Hyprism.Modules;

/// <summary>
/// TrafficMonitor: network speed and system stats docked in the taskbar. It saves config.ini on exit,
/// so Hyprism stops it, edits the ini, and starts it again.
/// </summary>
public sealed class TrafficMonitor : ModuleBase
{
    public override string Id => "trafficmonitor";
    public override string Name => "Network & System Monitor";
    public override string Description => "Upload/download speed in the taskbar, plus CPU, RAM, GPU and temperatures. Powered by TrafficMonitor.";
    public override string Glyph => "";
    public override ModuleCategory Category => ModuleCategory.Desktop;
    public override string Repo => "zhongyang219/TrafficMonitor";
    public override string License => "Anti-996 License 1.0";
    // The Full build includes the hardware monitor (temperatures, GPU).
    protected override string WingetId => "zhongyang219.TrafficMonitor.Full";
    protected override string ProcessName => "TrafficMonitor";
    protected override string? ExePath => Sys.FindWingetPortable("zhongyang219.TrafficMonitor.Full", "TrafficMonitor.exe")
        ?? Sys.FindWingetPortable("zhongyang219.TrafficMonitor.Lite", "TrafficMonitor.exe")
        ?? Sys.FirstExisting(@"%ProgramFiles%\TrafficMonitor\TrafficMonitor.exe");

    // Bit positions follow TrafficMonitor's DisplayItem enum (TDI_UP = 0 ... TDI_TODAY_TRAFFIC = 12).
    static readonly (string Key, string Label, int Bit, bool Default)[] Items =
    [
        ("up", "Upload speed", 0, true), ("down", "Download speed", 1, true),
        ("cpu", "CPU usage", 2, true), ("memory", "Memory usage", 3, true),
        ("gpu", "GPU usage", 4, false), ("cpuTemp", "CPU temperature", 5, false),
        ("gpuTemp", "GPU temperature", 6, false), ("diskUsage", "Disk usage", 9, false),
        ("totalSpeed", "Total speed", 10, false), ("todayTraffic", "Today's traffic", 12, false),
    ];

    public override IReadOnlyList<SettingDef> Settings { get; } =
    [
        .. Items.Select(i => Toggle(i.Key, i.Label, i.Default, i.Bit is 5 or 6 ? "Temperatures need TrafficMonitor to run as administrator." : null, "Shown in the taskbar")),
        Text("font", "Font", "Google Sans Flex", "Any installed font name.", "Look"),
        Slider("fontSize", "Font size", 9, 7, 16, 1, group: "Look"),
        Toggle("paletteColors", "Use theme colors", true, group: "Look"),
        Color("labelColor", "Label color", "#A6ADC8", "Used when theme colors are off.", "Look"),
        Color("valueColor", "Value color", "#CDD6F4", group: "Look"),
        Toggle("transparent", "Transparent background", true, group: "Look"),
        Toggle("left", "Dock on the left of the taskbar", false, group: "Position"),
        Text("adapter", "Network adapter", "", "Leave empty to follow the active connection.", "Position"),
    ];

    static string? ConfigPath(string? exe)
    {
        var appData = Path.Combine(Sys.RoamingAppData, "TrafficMonitor", "config.ini");
        if (File.Exists(appData)) return appData;
        return exe is null ? null : Path.Combine(Path.GetDirectoryName(exe)!, "config.ini"); // portable mode
    }

    public override async Task ApplyAsync(JsonObject s, CancellationToken ct = default)
    {
        var exe = ExePath;
        var path = ConfigPath(exe) ?? throw new InvalidOperationException("TrafficMonitor isn't installed.");
        bool running = Sys.IsRunning(ProcessName);
        if (running) { Sys.Kill(ProcessName); await Task.Delay(500, ct); }

        int mask = Items.Where(i => s.Bool(this, i.Key)).Aggregate(0, (m, i) => m | 1 << i.Bit);
        var p = CurrentPalette;
        var (label, value) = s.Bool(this, "paletteColors")
            ? (p.Muted, p.Foreground)
            : (Rgb.Parse(s.Str(this, "labelColor")), Rgb.Parse(s.Str(this, "valueColor")));
        // task_bar_text_color is "label,value," repeated for every display item (13 built-in).
        var colors = string.Concat(Enumerable.Repeat($"{label.ColorRef},{value.ColorRef},", 13));
        // Background equal to the transparent key color means "fully transparent".
        int back = s.Bool(this, "transparent") ? 0 : p.Background.ColorRef;

        var values = new List<(string, string, string)>
        {
            ("config", "show_task_bar_wnd", "true"),
            ("config", "hide_main_window", "1"),
            ("task_bar", "tbar_display_item", mask.ToString()),
            ("task_bar", "font_name", s.Str(this, "font")),
            ("task_bar", "font_size", ((int)s.Num(this, "fontSize")).ToString()),
            ("task_bar", "specify_each_item_color", "false"),
            ("task_bar", "task_bar_text_color", colors),
            ("task_bar", "task_bar_back_color", back.ToString()),
            ("task_bar", "transparent_color", "0"),
            ("task_bar", "task_bar_wnd_on_left", s.Bool(this, "left") ? "true" : "false"),
            ("task_bar", "auto_adapt_light_theme", "false"),
        };
        var adapter = s.Str(this, "adapter");
        values.Add(("connection", "select_all", "false"));
        values.Add(("connection", "connection_name", adapter));
        WriteIni(path, [.. values]);

        if (running || Store.Config.For(Id).Enabled) await StartAsync();
    }

    public override Task ApplyPaletteAsync(Palette p, CancellationToken ct = default) =>
        CurrentSettings.Bool(this, "paletteColors") && ExePath is not null ? ApplyAsync(CurrentSettings, ct) : Task.CompletedTask;
}
