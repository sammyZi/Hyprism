using System.Diagnostics;

namespace Hyprism.Core;

/// <summary>
/// After a change that runs inside Explorer (shell extensions, Explorer DLLs, Windhawk mods), watch Explorer for a few
/// minutes. If it restarts on its own twice, assume the change crashes it: revert and tell the user.
/// </summary>
public static class SafeMode
{
    static readonly TimeSpan Window = TimeSpan.FromMinutes(3);
    const int CrashLimit = 2;
    static (string What, Func<Task> Revert, DateTime Until)? pending;
    static int crashes;
    static int lastExplorerPid = CurrentExplorerPid();
    static Timer? timer;

    public static event Action<string>? Reverted;

    /// <summary>Called by modules right after an Explorer-affecting change.</summary>
    public static void Track(string what, Func<Task> revert)
    {
        if (!Store.Config.SafeMode) return;
        pending = (what, revert, DateTime.UtcNow + Window);
        crashes = 0;
        lastExplorerPid = CurrentExplorerPid();
        timer ??= new Timer(_ => Tick(), null, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(2));
    }

    static int CurrentExplorerPid()
    {
        // The shell is the oldest explorer.exe in this session.
        var shell = Process.GetProcessesByName("explorer")
            .Where(p => p.SessionId == Process.GetCurrentProcess().SessionId)
            .OrderBy(p => { try { return p.StartTime; } catch { return DateTime.MaxValue; } })
            .FirstOrDefault();
        return shell?.Id ?? 0;
    }

    static async void Tick()
    {
        if (pending is not { } p) return;
        if (DateTime.UtcNow > p.Until) { pending = null; return; }
        int pid = CurrentExplorerPid();
        if (pid == lastExplorerPid || pid == 0) return;
        lastExplorerPid = pid;
        if (DateTime.UtcNow - Sys.LastIntentionalExplorerRestart < TimeSpan.FromSeconds(15)) return; // we did that
        if (++crashes < CrashLimit) return;

        pending = null;
        try { await p.Revert(); } catch { /* best effort: still tell the user */ }
        Reverted?.Invoke($"Explorer kept crashing after \"{p.What}\", so Hyprism reverted it.");
    }
}
