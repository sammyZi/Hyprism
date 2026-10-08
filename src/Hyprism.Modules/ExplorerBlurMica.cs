using System.Text.Json.Nodes;
using Hyprism.Core;
using Hyprism.Theming;

namespace Hyprism.Modules;

/// <summary>
/// ExplorerBlurMica: a shell extension DLL that Explorer loads. Not on winget, so it comes from the GitHub release.
/// Registering needs admin; config changes need an Explorer restart; safe mode guards every change.
/// </summary>
public sealed class ExplorerBlurMica : ModuleBase
{
    public override string Id => "explorerblurmica";
    public override string Name => "Glass Explorer";
    public override string Description => "Blur, Acrylic or Mica behind File Explorer, with your own tint. Powered by ExplorerBlurMica.";
    public override string Glyph => "";
    public override ModuleCategory Category => ModuleCategory.Look;
    public override string Repo => "Maplespe/ExplorerBlurMica";
    public override string License => "LGPL-3.0";
    protected override string ReleaseAsset => @"x64\.zip$";

    // Index = value of [config] effect= (from the project's README).
    static readonly string[] Effects = ["Blur", "Acrylic", "Mica", "Blur (clear)", "Mica Alt"];

    public override IReadOnlyList<SettingDef> Settings { get; } =
    [
        Choice("effect", "Effect", "Acrylic", Effects, "Blur works up to Windows 11 22H2. Mica needs Windows 11. Blur (clear) works everywhere.", "Effect"),
        Toggle("clearAddress", "Clear the address bar background", true, group: "Effect"),
        Toggle("clearBarBg", "Clear the scrollbar background", true, group: "Effect"),
        Toggle("clearWinUIBg", "Clear the Windows 11 toolbar background", true, group: "Effect"),
        Toggle("showLine", "Line between navigation pane and files", true, group: "Effect"),
        Toggle("paletteTint", "Tint with theme colors", true, group: "Tint"),
        Color("lightTint", "Light mode tint", "#DCDCDC", group: "Tint"),
        Slider("lightAlpha", "Light mode tint strength", 63, 0, 100, 1, group: "Tint"),
        Color("darkTint", "Dark mode tint", "#000000", group: "Tint"),
        Slider("darkAlpha", "Dark mode tint strength", 47, 0, 100, 1, group: "Tint"),
    ];

    string? Dll => Directory.Exists(FallbackDir) ? Directory.EnumerateFiles(FallbackDir, "ExplorerBlurMica.dll", SearchOption.AllDirectories).FirstOrDefault() : null;
    protected override string? ExePath => Dll;
    string? ConfigPath => Dll is { } d ? Path.Combine(Path.GetDirectoryName(d)!, "config.ini") : null;

    public override async Task<ModuleStatus> GetStatusAsync(CancellationToken ct = default)
    {
        var s = await base.GetStatusAsync(ct);
        return s with { Running = s.Installed && Store.Config.For(Id).Enabled }; // a DLL has no process; "running" = registered
    }

    public override async Task EnableAsync(CancellationToken ct = default)
    {
        var dll = Dll ?? throw new InvalidOperationException("Install Glass Explorer first.");
        if (await Sys.RunElevatedAsync("regsvr32", $"/s \"{dll}\"", ct) != 0) throw new InvalidOperationException("Registering the Explorer extension failed.");
        Store.Config.For(Id).Enabled = true;
        Store.Save();
        await Sys.RestartExplorerAsync();
        SafeMode.Track("Glass Explorer", DisableAsyncNoTrack);
    }

    public override async Task DisableAsync(CancellationToken ct = default) => await DisableAsyncNoTrack();

    async Task DisableAsyncNoTrack()
    {
        if (Dll is { } dll) await Sys.RunElevatedAsync("regsvr32", $"/s /u \"{dll}\"");
        Store.Config.For(Id).Enabled = false;
        Store.Save();
        await Sys.RestartExplorerAsync();
    }

    public override async Task ApplyAsync(JsonObject s, CancellationToken ct = default)
    {
        var path = ConfigPath ?? throw new InvalidOperationException("Install Glass Explorer first.");
        var p = CurrentPalette;
        bool usePalette = s.Bool(this, "paletteTint");
        var light = usePalette ? (p.IsDark ? p.Foreground : p.Background) : Rgb.Parse(s.Str(this, "lightTint"));
        var dark = usePalette ? (p.IsDark ? p.Background : p.Foreground) : Rgb.Parse(s.Str(this, "darkTint"));
        string B(string k) => s.Bool(this, k) ? "true" : "false";
        string A(string k) => ((int)Math.Round(s.Num(this, k) * 2.55)).ToString();

        var ini = $"""
            [config]
            effect={Math.Max(0, Array.IndexOf(Effects, s.Str(this, "effect")))}
            clearAddress={B("clearAddress")}
            clearBarBg={B("clearBarBg")}
            clearWinUIBg={B("clearWinUIBg")}
            showLine={B("showLine")}
            [light]
            r={light.R}
            g={light.G}
            b={light.B}
            a={A("lightAlpha")}
            [dark]
            r={dark.R}
            g={dark.G}
            b={dark.B}
            a={A("darkAlpha")}

            """;
        // Explorer only reads the file when it starts; restart only when something actually changed.
        if (Write(path, ini) && Store.Config.For(Id).Enabled)
        {
            await Sys.RestartExplorerAsync();
            SafeMode.Track("Glass Explorer settings", async () => { Backup.RestoreLast(Id); await Sys.RestartExplorerAsync(); });
        }
    }

    public override Task ApplyPaletteAsync(Palette p, CancellationToken ct = default) =>
        CurrentSettings.Bool(this, "paletteTint") && ConfigPath is not null ? ApplyAsync(CurrentSettings, ct) : Task.CompletedTask;

    public override async Task UninstallAsync(IProgress<string>? log = null, CancellationToken ct = default)
    {
        await DisableAsyncNoTrack(); // unregister first, or Explorer keeps the DLL locked
        await Task.Delay(1500, ct);
        if (Directory.Exists(FallbackDir)) Directory.Delete(FallbackDir, true);
    }
}
