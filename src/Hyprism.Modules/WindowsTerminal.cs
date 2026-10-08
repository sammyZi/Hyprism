using System.Text.Json.Nodes;
using Hyprism.Core;
using Hyprism.Theming;

namespace Hyprism.Modules;

/// <summary>
/// Windows Terminal (+ Oh My Posh and Nerd Fonts). Edits profiles.defaults in settings.json, which Terminal hot-reloads.
/// Oh My Posh is wired into the PowerShell profiles inside a marked block that Hyprism owns.
/// </summary>
public sealed class WindowsTerminal : ModuleBase, IThemePreview
{
    public override string Id => "terminal";
    public override string Name => "Glass Terminal";
    public override string Description => "Acrylic, background images, color schemes, Nerd Fonts, retro effects and Oh My Posh prompts for Windows Terminal.";
    public override string Glyph => "";
    public override ModuleCategory Category => ModuleCategory.Look;
    public override string Repo => "microsoft/terminal";
    public override string License => "MIT";
    protected override string WingetId => "Microsoft.WindowsTerminal";
    protected override string ProcessName => "WindowsTerminal";
    protected override string StartMenuName => "Terminal";
    public override bool RunsInBackground => false;

    // Font face Terminal sees -> name `oh-my-posh font install` takes (ryanoasis/nerd-fonts release names).
    static readonly Dictionary<string, string> NerdFonts = new()
    {
        ["CaskaydiaCove Nerd Font"] = "CascadiaCode",
        ["JetBrainsMono Nerd Font"] = "JetBrainsMono",
        ["FiraCode Nerd Font"] = "FiraCode",
        ["MesloLGM Nerd Font"] = "Meslo",
        ["Hack Nerd Font"] = "Hack",
        ["Iosevka Nerd Font"] = "Iosevka",
        ["GeistMono Nerd Font"] = "GeistMono",
    };

    static readonly string[] OmpThemes =
    [
        "catppuccin_mocha", "catppuccin_latte", "tokyonight_storm", "tokyo", "nordtron", "gruvbox", "dracula", "night-owl",
        "atomic", "atomicBit", "bubbles", "bubblesline", "clean-detailed", "craver", "easy-term", "half-life", "jandedobbeleer",
        "kushal", "lambda", "M365Princess", "montys", "paradox", "powerlevel10k_rainbow", "powerlevel10k_lean", "pure", "quick-term",
        "robbyrussell", "slim", "space", "spaceship", "star", "takuya", "the-unnamed", "uew", "wholespace", "zash",
    ];

    public override IReadOnlyList<SettingDef> Settings { get; } =
    [
        Toggle("acrylic", "Acrylic", true, "Blur what's behind the terminal.", "Glass"),
        Slider("opacity", "Opacity", 80, 20, 100, 1, group: "Glass"),
        Toggle("wallpaperBackground", "Use the desktop wallpaper as background image", false, group: "Background image"),
        FilePick("backgroundImage", "Background image", "Ignored while the option above is on.", "Background image"),
        Slider("imageOpacity", "Image opacity", 25, 0, 100, 1, group: "Background image"),
        Choice("stretch", "Stretch", "uniformToFill", ["none", "fill", "uniform", "uniformToFill"], group: "Background image"),
        Choice("alignment", "Alignment", "center", ["center", "left", "top", "right", "bottom", "topLeft", "topRight", "bottomLeft", "bottomRight"], group: "Background image"),
        Choice("scheme", "Color scheme", "Hyprism", ["Hyprism", "Campbell", "One Half Dark", "One Half Light", "Tango Dark", "Solarized Dark", "Vintage"], "\"Hyprism\" follows the current palette.", "Colors and text"),
        Choice("font", "Font", "CaskaydiaCove Nerd Font", [.. NerdFonts.Keys, "Cascadia Mono", "Consolas"], "Nerd Fonts are downloaded and installed automatically when you press Apply.", "Colors and text"),
        Slider("fontSize", "Font size", 12, 8, 24, 1, group: "Colors and text"),
        Button("installFont", "Install selected Nerd Font", "Downloads it from ryanoasis/nerd-fonts via Oh My Posh.", "Colors and text"),
        Toggle("retro", "Retro CRT effect", false, "Scan lines and glow.", "Effects"),
        FilePick("shader", "Pixel shader (.hlsl)", "Custom shader effect.", "Effects"),
        Toggle("ohMyPosh", "Oh My Posh prompt", false, "Adds the prompt to your PowerShell profiles.", "Prompt"),
        Choice("ompTheme", "Prompt theme", "catppuccin_mocha", OmpThemes, group: "Prompt"),
    ];

    public string PreviewKey => "ompTheme";

    static string? SettingsPath => Sys.FirstExisting(
        @"%LocalAppData%\Packages\Microsoft.WindowsTerminal_8wekyb3d8bbwe\LocalState\settings.json",
        @"%LocalAppData%\Packages\Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe\LocalState\settings.json",
        @"%LocalAppData%\Microsoft\Windows Terminal\settings.json");

    // The installer puts it in Programs\oh-my-posh; the winget package exposes it as an alias in WindowsApps (on PATH).
    static string? OmpPath => Sys.FirstExisting(@"%LocalAppData%\Programs\oh-my-posh\bin\oh-my-posh.exe") ?? Sys.FindOnPath("oh-my-posh.exe");
    static string Omp => OmpPath ?? "oh-my-posh";

    /// <summary>The font every Windows Terminal install ships with: the fallback when a Nerd Font can't be installed.</summary>
    const string BuiltInFont = "Cascadia Mono";

    /// <summary>
    /// Makes sure the chosen font exists before Terminal is told to use it (otherwise Terminal shows a
    /// "Unable to find the following fonts" warning on every start). Missing Nerd Fonts are installed through
    /// Oh My Posh; if that fails, Terminal gets Cascadia Mono. Returns the face to write.
    /// </summary>
    static async Task<string> EnsureFontAsync(string face, CancellationToken ct)
    {
        if (Sys.IsFontInstalled(face)) return face;
        if (NerdFonts.TryGetValue(face, out var name))
        {
            try
            {
                await EnsureOhMyPoshAsync(ct);
                var (exit, _) = await Sys.RunAsync(OmpPath ?? "oh-my-posh", $"font install {name}", ct);
                if (exit == 0)
                {
                    // Give Windows a moment to register the new per-user font.
                    for (int i = 0; i < 10 && !Sys.IsFontInstalled(face); i++) await Task.Delay(500, ct);
                    if (Sys.IsFontInstalled(face)) return face;
                }
            }
            catch (Exception) when (!ct.IsCancellationRequested) { /* offline or blocked: fall back below */ }
        }
        return Sys.IsFontInstalled(BuiltInFont) ? BuiltInFont : "Consolas";
    }

    static string ThemeConfig(string theme)
    {
        var local = Path.Combine(Sys.LocalAppData, "Programs", "oh-my-posh", "themes", theme + ".omp.json");
        return File.Exists(local) ? local : $"https://raw.githubusercontent.com/JanDeDobbeleer/oh-my-posh/main/themes/{theme}.omp.json";
    }

    public override async Task ApplyAsync(JsonObject s, CancellationToken ct = default)
    {
        var path = SettingsPath ?? throw new InvalidOperationException("Open Windows Terminal once so it creates settings.json.");
        var face = await EnsureFontAsync(s.Str(this, "font"), ct);
        var json = Files.ReadJson(path);
        UpsertScheme(json, CurrentPalette);

        var profiles = json["profiles"] as JsonObject ?? (JsonObject)(json["profiles"] = new JsonObject());
        var d = profiles["defaults"] as JsonObject ?? (JsonObject)(profiles["defaults"] = new JsonObject());
        d["useAcrylic"] = s.Bool(this, "acrylic");
        d["opacity"] = (int)s.Num(this, "opacity");
        var image = s.Bool(this, "wallpaperBackground") ? Store.Config.Wallpaper : s.Str(this, "backgroundImage");
        if (string.IsNullOrEmpty(image)) d.Remove("backgroundImage"); else d["backgroundImage"] = image;
        d["backgroundImageOpacity"] = Math.Round(s.Num(this, "imageOpacity") / 100, 2);
        d["backgroundImageStretchMode"] = s.Str(this, "stretch");
        d["backgroundImageAlignment"] = s.Str(this, "alignment");
        d["colorScheme"] = s.Str(this, "scheme");
        d["font"] = new JsonObject { ["face"] = face, ["size"] = s.Num(this, "fontSize") };
        d["experimental.retroTerminalEffect"] = s.Bool(this, "retro");
        var shader = s.Str(this, "shader");
        if (string.IsNullOrEmpty(shader)) d.Remove("experimental.pixelShaderPath"); else d["experimental.pixelShaderPath"] = shader;
        Write(path, json);

        WritePromptBlock(s.Bool(this, "ohMyPosh") ? s.Str(this, "ompTheme") : null);
    }

    static void UpsertScheme(JsonObject json, Palette p)
    {
        var schemes = json["schemes"] as JsonArray ?? (JsonArray)(json["schemes"] = new JsonArray());
        var existing = schemes.FirstOrDefault(x => (string?)x?["name"] == "Hyprism");
        if (existing is not null) schemes.Remove(existing);
        schemes.Add(Generators.TerminalScheme(p));
    }

    /// <summary>Adds or removes the Oh My Posh line in both Windows PowerShell and PowerShell 7 profiles.</summary>
    void WritePromptBlock(string? theme)
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        foreach (var (dir, shell) in new[] { ("PowerShell", "pwsh"), ("WindowsPowerShell", "powershell") })
        {
            var profile = Path.Combine(docs, dir, "Microsoft.PowerShell_profile.ps1");
            if (theme is null && !File.Exists(profile)) continue;
            Backup.BeforeWrite(Id, profile);
            Files.SetManagedBlock(profile, theme is null ? null
                : $"oh-my-posh init {shell} --config '{ThemeConfig(theme)}' | Invoke-Expression");
        }
    }

    public override async Task ApplyPaletteAsync(Palette p, CancellationToken ct = default)
    {
        if (SettingsPath is not { } path) return;
        var json = Files.ReadJson(path);
        UpsertScheme(json, p);
        Write(path, json);
        await Task.CompletedTask;
    }

    public override Task SetEffectsAsync(bool blur, bool animate, CancellationToken ct = default)
    {
        if (blur) return SettingsPath is null ? Task.CompletedTask : ApplyAsync(CurrentSettings, ct);
        if (SettingsPath is not { } path) return Task.CompletedTask;
        var json = Files.ReadJson(path);
        if (json["profiles"]?["defaults"] is JsonObject d) { d["useAcrylic"] = false; d["opacity"] = 100; Write(path, json); }
        return Task.CompletedTask;
    }

    public override async Task InvokeAsync(string key, CancellationToken ct = default)
    {
        if (key != "installFont") return;
        var face = CurrentSettings.Str(this, "font");
        if (!NerdFonts.TryGetValue(face, out var name)) throw new InvalidOperationException($"{face} is not a Nerd Font; nothing to install.");
        await EnsureOhMyPoshAsync(ct);
        var (exit, output) = await Sys.RunAsync(Omp, $"font install {name}", ct);
        if (exit != 0) throw new InvalidOperationException(output.Trim());
    }

    public override async Task InstallAsync(IProgress<string>? log = null, CancellationToken ct = default)
    {
        await base.InstallAsync(log, ct);
        await EnsureOhMyPoshAsync(ct, log);
    }

    static async Task EnsureOhMyPoshAsync(CancellationToken ct, IProgress<string>? log = null)
    {
        if (OmpPath is not null) return;
        if (!await Sys.WingetInstallAsync("JanDeDobbeleer.OhMyPosh", "winget", log, ct))
            throw new InvalidOperationException("Couldn't install Oh My Posh.");
    }

    public async Task<string> PreviewAnsiAsync(string theme, CancellationToken ct = default)
    {
        if (OmpPath is null) return "Install Oh My Posh (Install button above) to see live previews.";
        var (_, output) = await Sys.RunAsync(Omp, $"print preview --config \"{ThemeConfig(theme)}\" --shell pwsh --escape=false --force", ct,
            workDir: Sys.UserProfile);
        return output;
    }
}
