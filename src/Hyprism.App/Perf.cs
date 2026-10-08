using System.Diagnostics;
using Microsoft.UI.Xaml.Media;

namespace Hyprism.App;

/// <summary>
/// Hidden diagnostics: `Hyprism.exe --perf` logs UI-thread frames per second, the worst frame gap and CPU use once a
/// second to %TEMP%\hyprism-perf.log. A/B switches for hunting jank: --no-motion, --no-transitions, --scrollview.
/// </summary>
public static class Perf
{
    static readonly string[] Args = Environment.GetCommandLineArgs();
    public static bool Enabled => Args.Contains("--perf");
    public static bool NoMotion => Args.Contains("--no-motion");
    public static bool NoTransitions => Args.Contains("--no-transitions");
    public static bool UseScrollView => Args.Contains("--scrollview");

    public static void Start()
    {
        if (!Enabled) return;
        var log = Path.Combine(Path.GetTempPath(), "hyprism-perf.log");
        File.WriteAllText(log, $"# {DateTime.Now:T} args: {string.Join(' ', Args.Skip(1))}\n# sec  uiFps  worstGapMs  cpu%\n");
        var proc = Process.GetCurrentProcess();
        var clock = Stopwatch.StartNew();
        long frames = 0, lastTick = 0;
        double worst = 0, lastSecond = 0;
        var cpuAt = proc.TotalProcessorTime;
        int second = 0;
        int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
        CompositionTarget.Rendering += (_, _) =>
        {
            long now = clock.ElapsedTicks;
            double gap = (now - lastTick) * 1000.0 / Stopwatch.Frequency;
            if (lastTick != 0 && gap > worst) worst = gap;
            lastTick = now;
            frames++;
            double t = clock.Elapsed.TotalSeconds;
            if (t - lastSecond < 1) return;
            proc.Refresh();
            var cpu = (proc.TotalProcessorTime - cpuAt).TotalMilliseconds / ((t - lastSecond) * 1000) * 100 / Environment.ProcessorCount;
            cpuAt = proc.TotalProcessorTime;
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            var info = GC.GetGCMemoryInfo(GCKind.Any);
            var pause = info.PauseDurations.ToArray().Sum(d => d.TotalMilliseconds);
            var gcText = g2 > gc2 ? $"  [gen{info.Generation} {(info.Concurrent ? "background" : "BLOCKING")} pause {pause:0.0}ms compacted={info.Compacted} heap {info.HeapSizeBytes / 1048576}MB]" : "";
            File.AppendAllText(log, $"{++second,4}  {frames / (t - lastSecond),5:0}  {worst,9:0.0}  {cpu,5:0.0}  gc {g0 - gc0}/{g1 - gc1}/{g2 - gc2}{gcText}\n");
            (gc0, gc1, gc2) = (g0, g1, g2);
            frames = 0; worst = 0; lastSecond = t;
        };
    }
}
