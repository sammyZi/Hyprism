using System.Text.Json.Nodes;
using Hyprism.Core;

namespace Hyprism.Modules;

/// <summary>Mica For Everyone 2.x: backdrop rules stored in its packaged LocalState\settings.json.</summary>
public sealed class MicaForEveryone : ModuleBase
{
    public override string Id => "micaforeveryone";
    public override string Name => "Glass Everywhere";
    public override string Description => "Mica, Acrylic or Tabbed backdrops for any app, with per-app rules. Powered by Mica For Everyone.";
    public override string Glyph => "";
    public override ModuleCategory Category => ModuleCategory.Look;
    public override string Repo => "MicaForEveryone/MicaForEveryone";
    public override string License => "MIT";
    protected override string WingetId => "MicaForEveryone.MicaForEveryone";
    protected override string ProcessName => "MicaForEveryone";
    protected override string StartMenuName => "Mica For Everyone";

    /// <summary>
    /// winget can't install this one: its listing depends on "Microsoft.WindowsAppRuntime.2.1", an ID winget doesn't
    /// have ("No matches", 0x8A150104), and skipping that, Windows streams the MSIX from GitHub and times out
    /// (0x80072EE2). So: the runtime it needs (published as Microsoft.WindowsAppRuntime.2) from winget, then the
    /// official bundle from the GitHub release, SHA256- and antivirus-checked, installed from disk.
    /// </summary>
    public override async Task InstallAsync(IProgress<string>? log = null, CancellationToken ct = default)
    {
        if (!await Sys.WingetInstallAsync("Microsoft.WindowsAppRuntime.2", "winget", log, ct))
            throw new InvalidOperationException("Couldn't install the Windows App Runtime that Mica For Everyone needs. Open Details below for what went wrong.");
        var dir = Path.Combine(Path.GetTempPath(), $"hyprism-mfe-{Guid.NewGuid():N}");
        try
        {
            await Sys.InstallGitHubReleaseAsync(Repo, @"^bundle\.msixbundle$", dir, log, ct);
            if (ProcessName is not null) Sys.Kill(ProcessName);
            log?.Report("Installing Mica For Everyone…");
            var (exit, output) = await Sys.RunAsync("powershell.exe",
                $"-NoProfile -Command \"Add-AppxPackage -Path '{Path.Combine(dir, "bundle.msixbundle")}' -ForceApplicationShutdown\"", ct, onLine: l => log?.Report(l));
            Sys.ForgetWingetStatus(WingetId);
            if (exit != 0) throw new InvalidOperationException($"Windows couldn't install the Mica For Everyone package: {output.Trim()}");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // Values of MicaForEveryone.Models.BackdropType / TitleBarColorMode / CornerPreference.
    static readonly string[] Backdrops = ["Default", "None", "Mica", "Acrylic", "MicaAlt"];
    static readonly string[] TitleBars = ["Default", "Light", "Dark", "System"];
    static readonly string[] Corners = ["Default", "Square", "Rounded", "RoundedSmall"];

    public override IReadOnlyList<SettingDef> Settings { get; } =
    [
        Choice("backdrop", "Backdrop for all apps", "Mica", Backdrops, "MicaAlt is the \"Tabbed\" material.", "Everywhere"),
        Choice("titleBar", "Title bar color", "System", TitleBars, group: "Everywhere"),
        Choice("corners", "Window corners", "Default", Corners, group: "Everywhere"),
        Toggle("extendFrame", "Extend the backdrop into the window body", false, "Looks great on simple apps, can break complex ones.", "Everywhere"),
        Lines("rules", "Per-app rules", "explorer = Default\nnotepad = Acrylic\n",
            "One per line: process name = backdrop (Default, None, Mica, Acrylic, MicaAlt). Lines starting with # are ignored.", "Per-app rules"),
    ];

    static string? ConfigPath => Sys.FindInPackages("*MicaForEveryone*", @"LocalState\settings.json");

    public override async Task ApplyAsync(JsonObject s, CancellationToken ct = default)
    {
        var path = ConfigPath ?? throw new InvalidOperationException("Start Mica For Everyone once so it creates its settings file.");
        var json = Files.ReadJson(path);
        var rules = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "global",
                ["titleBarColor"] = s.Str(this, "titleBar"),
                ["backdropPreference"] = s.Str(this, "backdrop"),
                ["cornerPreference"] = s.Str(this, "corners"),
                ["extendFrameIntoClientArea"] = s.Bool(this, "extendFrame"),
            },
        };
        foreach (var line in s.Lines(this, "rules"))
        {
            var parts = line.Split('=', 2, StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || !Backdrops.Contains(parts[1], StringComparer.OrdinalIgnoreCase)) continue;
            rules.Add(new JsonObject
            {
                ["type"] = "process",
                ["processName"] = Path.GetFileNameWithoutExtension(parts[0]),
                ["backdropPreference"] = Backdrops.First(b => b.Equals(parts[1], StringComparison.OrdinalIgnoreCase)),
            });
        }
        json["rules"] = rules;
        if (Write(path, json)) await RestartIfRunningAsync();
    }

    public override Task SetEffectsAsync(bool blur, bool animate, CancellationToken ct = default)
    {
        if (!blur) Sys.Kill(ProcessName);
        else if (Store.Config.For(Id).Enabled) return StartAsync();
        return Task.CompletedTask;
    }
}
