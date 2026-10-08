using System.Text.Json.Nodes;
using Hyprism.Theming;

namespace Hyprism.Core;

public enum ModuleCategory { Look, Desktop, Productivity }

/// <summary>
/// One integrated open-source project. Hyprism never reimplements the project; a module only
/// installs it, starts/stops it, and writes its config file.
/// </summary>
public interface IModule
{
    string Id { get; }
    string Name { get; }
    string Description { get; }
    /// <summary>Segoe Fluent Icons glyph.</summary>
    string Glyph { get; }
    ModuleCategory Category { get; }
    /// <summary>"owner/repo" on GitHub.</summary>
    string Repo { get; }
    /// <summary>SPDX id or short license name of the upstream project.</summary>
    string License { get; }
    /// <summary>Settings Hyprism exposes. The UI renders these generically.</summary>
    IReadOnlyList<SettingDef> Settings { get; }
    /// <summary>
    /// False for apps you open yourself (Terminal, Files): "enabled" then means Hyprism keeps them themed,
    /// not that it starts them at sign-in.
    /// </summary>
    bool RunsInBackground => true;

    Task<ModuleStatus> GetStatusAsync(CancellationToken ct = default);
    /// <summary>Installs, or updates when already installed.</summary>
    Task InstallAsync(IProgress<string>? log = null, CancellationToken ct = default);
    Task UninstallAsync(IProgress<string>? log = null, CancellationToken ct = default);
    /// <summary>Start now and at sign-in.</summary>
    Task EnableAsync(CancellationToken ct = default);
    /// <summary>Stop now and remove from sign-in.</summary>
    Task DisableAsync(CancellationToken ct = default);
    /// <summary>Write the project's config from Hyprism's settings and make the app reload it.</summary>
    Task ApplyAsync(JsonObject settings, CancellationToken ct = default);
    /// <summary>Put back the config files exactly as they were before Hyprism touched them.</summary>
    Task RestoreDefaultsAsync(CancellationToken ct = default);
    /// <summary>Recolor the app from a palette. Modules without colors ignore it.</summary>
    Task ApplyPaletteAsync(Palette palette, CancellationToken ct = default) => Task.CompletedTask;
    /// <summary>Global effects switch used by "toggle blur" and performance mode.</summary>
    Task SetEffectsAsync(bool blur, bool animate, CancellationToken ct = default) => Task.CompletedTask;
    /// <summary>Runs a <see cref="SettingKind.Action"/> button; <paramref name="key"/> is the setting key.</summary>
    Task InvokeAsync(string key, CancellationToken ct = default) => Task.CompletedTask;
}

/// <summary>A module that can render a live preview for one of its Choice settings (the Oh My Posh theme picker).</summary>
public interface IThemePreview
{
    string PreviewKey { get; }
    /// <summary>Raw ANSI text of the rendered preview.</summary>
    Task<string> PreviewAnsiAsync(string option, CancellationToken ct = default);
}

public sealed record ModuleStatus(bool Installed, bool Running, string? Version, string? Available)
{
    public bool UpdateAvailable => Installed && !string.IsNullOrEmpty(Available);
    public static readonly ModuleStatus Missing = new(false, false, null, null);
}

public enum SettingKind { Toggle, Slider, Choice, Color, Text, MultiLine, File, Folder, KeyBindings, Action }

/// <param name="Default">JSON value used when the user hasn't set one.</param>
/// <param name="Options">Choice values (display = value).</param>
public sealed record SettingDef(
    string Key, string Label, SettingKind Kind, JsonNode? Default = null,
    string? Description = null, string[]? Options = null,
    double Min = 0, double Max = 100, double Step = 1, string? Group = null);
