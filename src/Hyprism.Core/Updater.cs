using System.Diagnostics;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Hyprism.Core;

public sealed record UpdateInfo(Version Version, string Tag, string AssetName, string Url, string? Sha256, string Notes);

/// <summary>
/// Self-update from GitHub Releases. A release carries "Hyprism-Setup-&lt;version&gt;-x64.exe" (built by build.ps1).
/// The setup is verified against GitHub's SHA256 digest, then run silently; it closes Hyprism, upgrades in place
/// (settings are kept) and starts it again.
/// </summary>
public static class Updater
{
    static readonly HttpClient Http = CreateHttp();
    static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Hyprism-Updater");
        return h;
    }

    public static Version Current =>
        (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).GetName().Version ?? new Version(0, 0);

    /// <summary>Newer release for this CPU, or null when up to date / no update source configured.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        await Sys.OffUiThread();
        var repo = Store.Config.UpdateRepo?.Trim();
        if (string.IsNullOrEmpty(repo) || !Regex.IsMatch(repo, @"^[\w.-]+/[\w.-]+$")) return null;

        var rel = await Http.GetFromJsonAsync<JsonObject>($"https://api.github.com/repos/{repo}/releases/latest", ct)
            ?? throw new InvalidOperationException("No releases found.");
        var tag = (string?)rel["tag_name"] ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var version) || version <= Current) return null;

        var arch = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant(); // x64 / arm64
        var asset = rel["assets"]?.AsArray().Select(a => a!.AsObject())
            .FirstOrDefault(a => Regex.IsMatch((string)a["name"]!, $@"^Hyprism-Setup-.*-{arch}\.exe$", RegexOptions.IgnoreCase))
            ?? throw new InvalidOperationException($"Release {tag} has no Hyprism-Setup-*-{arch}.exe.");
        return new UpdateInfo(version, tag, (string)asset["name"]!, (string)asset["browser_download_url"]!,
            ((string?)asset["digest"])?.Replace("sha256:", ""), (string?)rel["body"] ?? "");
    }

    /// <summary>Downloads, verifies and launches the setup. The caller should exit right after (the setup closes us anyway).</summary>
    public static async Task InstallAsync(UpdateInfo update, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        await Sys.OffUiThread();
        var file = Path.Combine(Path.GetTempPath(), update.AssetName);
        using (var resp = await Http.GetAsync(update.Url, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? -1, read = 0;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            await using var dst = File.Create(file);
            var buffer = new byte[81920];
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct);
                read += n;
                if (total > 0) progress?.Report((double)read / total);
            }
        }

        // Never run an installer we can't verify.
        if (update.Sha256 is null) { File.Delete(file); throw new InvalidOperationException("The release has no published SHA256, so it wasn't installed."); }
        await using (var s = File.OpenRead(file))
        {
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(s, ct));
            if (!actual.Equals(update.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(file);
                throw new InvalidOperationException($"Download is corrupted or tampered with (SHA256 {actual[..12]}… doesn't match).");
            }
        }
        // Antivirus before running it.
        Antivirus.EnsureClean(await File.ReadAllBytesAsync(file, ct), update.AssetName);
        // /UPDATE=1 tells our setup to relaunch Hyprism when it's done.
        Process.Start(new ProcessStartInfo(file, "/SILENT /SUPPRESSMSGBOXES /NORESTART /UPDATE=1") { UseShellExecute = true });
    }
}
