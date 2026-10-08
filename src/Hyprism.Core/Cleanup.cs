using Microsoft.Win32;

namespace Hyprism.Core;

/// <summary>
/// Removes what Hyprism itself added to the system. Called by the uninstaller (`Hyprism.exe --uninstall`).
/// Apps Hyprism installed through winget (TranslucentTB, Lively...) are separate programs and stay installed.
/// </summary>
public static class Cleanup
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <param name="restore">Also stop every module and put back the original Windows look and config files.</param>
    /// <param name="purge">Also delete Hyprism's settings, profiles and backups.</param>
    /// <param name="dryRun">Only report what would happen.</param>
    public static async Task<List<string>> RunAsync(IReadOnlyList<IModule> modules, bool restore, bool purge, bool dryRun)
    {
        var log = new List<string>();
        async Task Step(string what, Func<Task> action)
        {
            log.Add((dryRun ? "[dry run] " : "") + what);
            if (dryRun) return;
            try { await action(); }
            catch (Exception e) { log.Add("  failed: " + e.Message); }
        }
        Task StepSync(string what, Action action) => Step(what, () => { action(); return Task.CompletedTask; });

        Store.Load();
        Hub.Init(modules);

        // 1. Stop processes Hyprism started from its own folders (tacky-borders runs from %LocalAppData%\Hyprism\tools).
        await StepSync("Stop tools running from Hyprism's folder", () => Sys.Kill("tacky-borders"));

        // 2. Restore first, while modules and backups are still there.
        if (restore)
        {
            foreach (var m in modules.Where(m => Store.Config.For(m.Id).Enabled))
                await Step($"Stop {m.Name}", () => m.DisableAsync());
            foreach (var id in Backup.ModulesWithBackups())
                await StepSync($"Restore original config files ({id})", () => Backup.RestoreOriginal(id));
            if (WindowsLook.HasOriginalBackup)
                await Step("Restore the original wallpaper, accent color and theme", WindowsLook.RestoreOriginalAsync);
        }

        // 3. The Explorer extension DLL lives in Hyprism's tools folder: unregister it (and restart Explorer to unload it)
        //    before that folder is deleted, or Explorer would keep pointing at a missing file.
        var ebmDir = Path.Combine(Sys.ToolsDir, "explorerblurmica");
        var dll = Directory.Exists(ebmDir) ? Directory.EnumerateFiles(ebmDir, "ExplorerBlurMica.dll", SearchOption.AllDirectories).FirstOrDefault() : null;
        if (dll is not null)
        {
            await Step("Unregister the Glass Explorer extension (asks for admin)", () => Sys.RunElevatedAsync("regsvr32", $"/s /u \"{dll}\""));
            await Step("Restart Explorer to unload it", Sys.RestartExplorerAsync);
        }

        // 4. Sign-in entries Hyprism created: its own and the per-module ones ("Hyprism.<module>").
        using (var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true))
            foreach (var name in run?.GetValueNames().Where(n => n == "Hyprism" || n.StartsWith("Hyprism.", StringComparison.Ordinal)).ToList() ?? [])
                await StepSync($"Remove sign-in entry \"{name}\"", () => run!.DeleteValue(name, false));

        // 5. Downloaded tools and caches.
        var local = Path.Combine(Sys.LocalAppData, "Hyprism");
        if (Directory.Exists(local))
            await Step("Delete downloaded tools and caches", async () =>
            {
                for (int i = 0; ; i++)
                {
                    try { Directory.Delete(local, recursive: true); return; }
                    catch (IOException) when (i < 10) { await Task.Delay(500); } // Explorer may still be releasing the DLL
                }
            });

        // 6. Settings, profiles and backups (only when asked: reinstalling keeps your rices).
        if (purge && Directory.Exists(Store.Root))
            await StepSync("Delete Hyprism settings, profiles and backups", () => Directory.Delete(Store.Root, recursive: true));

        return log;
    }
}
