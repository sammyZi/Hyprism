using System.Text.Json.Nodes;
using Hyprism.Core;
using Hyprism.Theming;

namespace Hyprism.Modules;

/// <summary>TranslucentTB: per-state taskbar appearance. It watches its settings.json and reloads by itself.</summary>
public sealed class TranslucentTB : ModuleBase
{
    public override string Id => "translucenttb";
    public override string Name => "Transparent Taskbar";
    public override string Description => "Clear, blurred or acrylic taskbar that changes with what's on screen. Powered by TranslucentTB.";
    public override string Glyph => "";
    public override ModuleCategory Category => ModuleCategory.Look;
    public override string Repo => "TranslucentTB/TranslucentTB";
    public override string License => "GPL-3.0";
    protected override string WingetId => "CharlesMilette.TranslucentTB";
    protected override string ProcessName => "TranslucentTB";
    protected override string StartMenuName => "TranslucentTB";

    static readonly string[] Accents = ["normal", "opaque", "clear", "blur", "acrylic"];
    static readonly string[] OptionalAccents = ["off", .. Accents];

    // TranslucentTB key -> (Hyprism label, default). "off" disables an optional state.
    static readonly (string Key, string Label, string Default)[] States =
    [
        ("desktop_appearance", "On the desktop", "clear"),
        ("visible_window_appearance", "With a window open", "off"),
        ("maximized_window_appearance", "With a maximized window", "acrylic"),
        ("start_opened_appearance", "Start menu open", "normal"),
        ("search_opened_appearance", "Search open", "normal"),
        ("task_view_opened_appearance", "Task View open", "normal"),
        ("battery_saver_appearance", "Battery saver", "opaque"),
    ];

    public override IReadOnlyList<SettingDef> Settings { get; } =
    [
        .. States.Select(s => Choice(s.Key, s.Label, s.Default, s.Key == "desktop_appearance" ? Accents : OptionalAccents, group: "Appearance per state")),
        Toggle("paletteTint", "Tint with theme colors", true, "Uses the palette background as the taskbar tint.", "Color"),
        Color("tint", "Tint color", "#000000", "Used when theme tint is off.", "Color"),
        Slider("tintOpacity", "Tint opacity", 0, 0, 100, 5, "0 is fully clear.", "Color"),
        Slider("blurRadius", "Blur radius", 9, 0, 30, 1, "Applies to the blur mode.", "Color"),
        Toggle("showLine", "Show the taskbar top line", false, group: "Details"),
        Toggle("showPeek", "Show the Aero Peek button", true, group: "Details"),
    ];

    static string? ConfigPath =>
        Sys.FindInPackages("*TranslucentTB*", @"RoamingState\settings.json")
        ?? Sys.FirstExisting(@"%LocalAppData%\Programs\TranslucentTB\settings.json");

    public override Task ApplyAsync(JsonObject s, CancellationToken ct = default)
    {
        var path = ConfigPath ?? throw new InvalidOperationException("Start TranslucentTB once so it creates its settings file.");
        var json = Files.ReadJson(path);
        var tint = s.Bool(this, "paletteTint") ? CurrentPalette.Background : Rgb.Parse(s.Str(this, "tint"));
        var alpha = (byte)Math.Round(s.Num(this, "tintOpacity") * 2.55);
        foreach (var (key, _, _) in States)
        {
            var accent = s.Str(this, key);
            var o = json[key] as JsonObject ?? [];
            if (key != "desktop_appearance") o["enabled"] = accent != "off";
            if (accent != "off") o["accent"] = accent;
            o["color"] = tint.HexRgba(alpha);
            o["show_line"] = s.Bool(this, "showLine");
            o["show_peek"] = s.Bool(this, "showPeek");
            o["blur_radius"] = s.Num(this, "blurRadius");
            json[key] = o;
        }
        Write(path, json);
        return Task.CompletedTask;
    }

    public override Task ApplyPaletteAsync(Palette p, CancellationToken ct = default) =>
        CurrentSettings.Bool(this, "paletteTint") && ConfigPath is not null ? ApplyAsync(CurrentSettings, ct) : Task.CompletedTask;

    // Stopping TranslucentTB hands the taskbar straight back to Windows: the cheapest "no effects" there is.
    public override Task SetEffectsAsync(bool blur, bool animate, CancellationToken ct = default)
    {
        if (!blur) Sys.Kill(ProcessName);
        else if (Store.Config.For(Id).Enabled) return StartAsync();
        return Task.CompletedTask;
    }
}
