using Hyprism.Core;

namespace Hyprism.Modules;

/// <summary>FluentFlyout: modern media/volume flyouts (Microsoft Store package; settings live in its own app).</summary>
public sealed class FluentFlyout : ModuleBase
{
    public override string Id => "fluentflyout";
    public override string Name => "Media & Volume Flyouts";
    public override string Description => "Fluent media and volume pop-ups that match the glass look. Powered by FluentFlyout.";
    public override string Glyph => "";
    public override ModuleCategory Category => ModuleCategory.Look;
    public override string Repo => "unchihugo/FluentFlyout";
    public override string License => "GPL-3.0";
    protected override string WingetId => "9N45NSM4TNBP";
    protected override string WingetSource => "msstore";
    protected override string ProcessName => "FluentFlyout";
    protected override string StartMenuName => "FluentFlyout";

    public override IReadOnlyList<SettingDef> Settings { get; } =
        [Button("open", "Open FluentFlyout settings", "Position, animations and layout are set in FluentFlyout itself.")];

    public override Task InvokeAsync(string key, CancellationToken ct = default) => StartAsync();
}

/// <summary>Windhawk: curated mods. Mods install from inside Windhawk, so each one opens its page there in one click.</summary>
public sealed class Windhawk : ModuleBase
{
    public override string Id => "windhawk";
    public override string Name => "Advanced Tweaks";
    public override string Description => "Hand-picked Windhawk mods: floating/rounded taskbar, Start menu styling, Explorer tweaks.";
    public override string Glyph => "";
    public override ModuleCategory Category => ModuleCategory.Look;
    public override string Repo => "ramensoftware/windhawk";
    public override string License => "GPL-3.0";
    protected override string WingetId => "RamenSoftware.Windhawk";
    protected override string ProcessName => "windhawk";
    protected override string? ExePath => Sys.FirstExisting(@"%ProgramFiles%\Windhawk\windhawk.exe");

    /// <summary>
    /// Uses Windhawk's official offline installer instead of winget's: the online one downloads a 50 MB compiler
    /// archive mid-install and fails outright ("Error downloading archive") if that one download hiccups.
    /// Same release, SHA256-checked against GitHub's digest and antivirus-scanned before it runs.
    /// </summary>
    public override async Task InstallAsync(IProgress<string>? log = null, CancellationToken ct = default)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"hyprism-windhawk-{Guid.NewGuid():N}");
        try
        {
            await Sys.InstallGitHubReleaseAsync(Repo, @"^windhawk_setup_offline\.exe$", dir, log, ct);
            log?.Report("Running the Windhawk installer. Approve the administrator prompt if Windows asks.");
            var exit = await Sys.RunElevatedAsync(Path.Combine(dir, "windhawk_setup_offline.exe"), "/S /STANDARD", ct);
            Sys.ForgetWingetStatus(WingetId);
            if (exit != 0) throw new InvalidOperationException($"The Windhawk installer exited with code {exit}. Open Details for the full log.");
        }
        finally { try { Directory.Delete(dir, true); } catch { } }
    }

    // Ids from ramensoftware/windhawk-mods (mods/<id>.wh.cpp).
    public static readonly (string Id, string Label, string Why)[] Mods =
    [
        ("windows-11-taskbar-styler", "Taskbar Styler", "Floating, rounded, centered-island taskbar. Replaces the archived RoundedTB."),
        ("windows-11-start-menu-styler", "Start Menu Styler", "Restyle or slim down the Windows 11 Start menu."),
        ("windows-11-notification-center-styler", "Notification Center Styler", "Glass quick settings and notification center."),
        ("windows-11-file-explorer-styler", "File Explorer Styler", "Hide or restyle Explorer's command bar and header."),
        ("taskbar-icon-size", "Taskbar height and icon size", "Smaller taskbar, Hyprland-bar proportions."),
        ("taskbar-clock-customization", "Taskbar clock", "Custom clock format, seconds, extra lines."),
        ("taskbar-labels", "Taskbar labels", "Show window titles next to taskbar icons."),
        ("taskbar-wheel-cycle", "Scroll to switch", "Mouse wheel over the taskbar cycles windows."),
        ("explorer-details-better-file-sizes", "Better file sizes", "Folder sizes and MB/GB units in Details view."),
    ];

    public override IReadOnlyList<SettingDef> Settings { get; } =
        [.. Mods.Select(m => Button("mod:" + m.Id, m.Label, m.Why, "Curated mods"))];

    public override Task InvokeAsync(string key, CancellationToken ct = default)
    {
        if (key.StartsWith("mod:")) Sys.OpenUrl($"https://windhawk.net/mods/{key[4..]}");
        return Task.CompletedTask;
    }
}
