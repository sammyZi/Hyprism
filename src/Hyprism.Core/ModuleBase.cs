using System.Diagnostics;
using System.Text.Json.Nodes;
using Hyprism.Theming;

namespace Hyprism.Core;

/// <summary>
/// Default behavior for a module: install via winget (or a verified GitHub release), run one process,
/// autostart through HKCU\Run. Subclasses override only what their app does differently.
/// </summary>
public abstract class ModuleBase : IModule
{
    public abstract string Id { get; }
    public abstract string Name { get; }
    public abstract string Description { get; }
    public abstract string Glyph { get; }
    public abstract ModuleCategory Category { get; }
    public abstract string Repo { get; }
    public abstract string License { get; }
    public virtual IReadOnlyList<SettingDef> Settings => [];

    protected virtual string? WingetId => null;
    protected virtual string WingetSource => "winget";
    /// <summary>Process name without ".exe"; used for running/stop.</summary>
    protected virtual string? ProcessName => null;
    /// <summary>Executable to launch; null when not installed or not launchable.</summary>
    protected virtual string? ExePath => null;
    protected virtual string ExeArgs => "";
    /// <summary>GitHub fallback: regex for the release asset to use when winget has no package or fails.</summary>
    protected virtual string? ReleaseAsset => null;
    protected string FallbackDir => Path.Combine(Sys.ToolsDir, Id);

    protected JsonObject CurrentSettings => this.WithDefaults(Store.Config.For(Id).Settings);
    protected Palette CurrentPalette => Store.Config.Palette;

    public virtual async Task<ModuleStatus> GetStatusAsync(CancellationToken ct = default)
    {
        bool running = ProcessName is not null && Sys.IsRunning(ProcessName);
        if (WingetId is not null)
        {
            var (installed, version, available) = await Sys.WingetListAsync(WingetId, ct);
            if (installed) return new(true, running, version, available);
        }
        var exe = ExePath;
        if (exe is not null && File.Exists(exe))
        {
            var tagFile = Path.Combine(FallbackDir, ".hyprism-version");
            var version = File.Exists(tagFile) ? File.ReadAllText(tagFile) : FileVersionInfo.GetVersionInfo(exe).ProductVersion;
            string? available = null;
            if (File.Exists(tagFile) && await Sys.LatestGitHubTagAsync(Repo, ct) is { } latest && latest != version) available = latest;
            return new(true, running, version, available);
        }
        return ModuleStatus.Missing with { Running = running };
    }

    public virtual async Task InstallAsync(IProgress<string>? log = null, CancellationToken ct = default)
    {
        bool wasRunning = ProcessName is not null && Sys.IsRunning(ProcessName);
        if (WingetId is not null && await Sys.WingetInstallAsync(WingetId, WingetSource, log, ct)) { }
        else if (ReleaseAsset is not null)
        {
            if (ProcessName is not null) Sys.Kill(ProcessName); // files may be locked while running
            await Sys.InstallGitHubReleaseAsync(Repo, ReleaseAsset, FallbackDir, log, ct);
        }
        else throw new InvalidOperationException($"Installing {Name} failed. Open Details below for what went wrong.");
        if (wasRunning && ProcessName is not null && !Sys.IsRunning(ProcessName)) await StartAsync();
    }

    public virtual async Task UninstallAsync(IProgress<string>? log = null, CancellationToken ct = default)
    {
        await DisableAsync(ct);
        if (WingetId is not null && await Sys.WingetUninstallAsync(WingetId, log, ct)) return;
        if (Directory.Exists(FallbackDir)) Directory.Delete(FallbackDir, recursive: true);
    }

    /// <summary>Start menu name of a Store/MSIX app, launched through shell:AppsFolder when there's no exe path.</summary>
    protected virtual string? StartMenuName => null;

    /// <summary>(file, args) that starts the app, or null when it isn't installed.</summary>
    protected async Task<(string File, string Args)?> LaunchCommandAsync()
    {
        if (ExePath is { } exe && File.Exists(exe)) return (exe, ExeArgs);
        if (StartMenuName is { } n && await Sys.FindAumidAsync(n) is { } aumid) return ("explorer.exe", $"shell:AppsFolder\\{aumid}");
        return null;
    }

    public virtual bool RunsInBackground => true;

    public virtual async Task EnableAsync(CancellationToken ct = default)
    {
        if (!RunsInBackground) { SaveEnabled(true); return; }
        var (file, args) = await LaunchCommandAsync() ?? throw new InvalidOperationException($"{Name} is not installed.");
        Sys.SetAutostart($"Hyprism.{Id}", $"\"{file}\" {args}".Trim());
        await StartAsync();
        SaveEnabled(true);
    }

    public virtual Task DisableAsync(CancellationToken ct = default)
    {
        Sys.SetAutostart($"Hyprism.{Id}", null);
        if (ProcessName is not null && RunsInBackground) Sys.Kill(ProcessName);
        SaveEnabled(false);
        return Task.CompletedTask;
    }

    public virtual Task ApplyAsync(JsonObject settings, CancellationToken ct = default) => Task.CompletedTask;

    public virtual async Task RestoreDefaultsAsync(CancellationToken ct = default)
    {
        bool running = ProcessName is not null && Sys.IsRunning(ProcessName);
        if (running) Sys.Kill(ProcessName!);
        Backup.RestoreOriginal(Id);
        Store.Config.For(Id).Settings = [];
        Store.Save();
        if (running) await StartAsync();
    }

    public virtual Task ApplyPaletteAsync(Palette palette, CancellationToken ct = default) => Task.CompletedTask;
    public virtual Task SetEffectsAsync(bool blur, bool animate, CancellationToken ct = default) => Task.CompletedTask;
    public virtual Task InvokeAsync(string key, CancellationToken ct = default) => Task.CompletedTask;

    protected virtual async Task StartAsync()
    {
        if (ProcessName is not null && Sys.IsRunning(ProcessName)) return;
        if (await LaunchCommandAsync() is var (file, args)) Sys.Start(file, args);
    }

    /// <summary>Restart the app so it re-reads its config (for apps that don't watch their files).</summary>
    protected async Task RestartIfRunningAsync()
    {
        if (ProcessName is null || !Sys.IsRunning(ProcessName)) return;
        Sys.Kill(ProcessName);
        await Task.Delay(300);
        await StartAsync();
    }

    /// <summary>Backs up, then writes. Every module write goes through one of these. Returns false when the content was already identical.</summary>
    protected bool Write(string path, string text)
    {
        if (File.Exists(path) && File.ReadAllText(path) == text) return false;
        Backup.BeforeWrite(Id, path);
        Files.WriteText(path, text);
        return true;
    }
    protected bool Write(string path, JsonNode json) => Write(path, json.ToJsonString(new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }));
    protected void WriteIni(string path, params (string Section, string Key, string Value)[] values)
    {
        Backup.BeforeWrite(Id, path);
        foreach (var (s, k, v) in values) Files.SetIni(path, s, k, v);
    }

    void SaveEnabled(bool on) { Store.Config.For(Id).Enabled = on; Store.Save(); }

    // Small factories so module setting lists read like a table.
    protected static SettingDef Toggle(string key, string label, bool def, string? desc = null, string? group = null) => new(key, label, SettingKind.Toggle, def, desc, Group: group);
    protected static SettingDef Slider(string key, string label, double def, double min, double max, double step = 1, string? desc = null, string? group = null) => new(key, label, SettingKind.Slider, def, desc, Min: min, Max: max, Step: step, Group: group);
    protected static SettingDef Choice(string key, string label, string def, string[] options, string? desc = null, string? group = null) => new(key, label, SettingKind.Choice, def, desc, options, Group: group);
    protected static SettingDef Color(string key, string label, string def, string? desc = null, string? group = null) => new(key, label, SettingKind.Color, def, desc, Group: group);
    protected static SettingDef Text(string key, string label, string def, string? desc = null, string? group = null) => new(key, label, SettingKind.Text, def, desc, Group: group);
    protected static SettingDef Lines(string key, string label, string def, string? desc = null, string? group = null) => new(key, label, SettingKind.MultiLine, def, desc, Group: group);
    protected static SettingDef FilePick(string key, string label, string? desc = null, string? group = null) => new(key, label, SettingKind.File, "", desc, Group: group);
    protected static SettingDef Button(string key, string label, string? desc = null, string? group = null) => new(key, label, SettingKind.Action, null, desc, Group: group);
}
