using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace Hyprism.Core;

/// <summary>Process, installer and shell helpers shared by every module.</summary>
public static partial class Sys
{
    public static readonly string LocalAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
    public static readonly string RoamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
    public static readonly string UserProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    public static readonly string ToolsDir = Path.Combine(LocalAppData, "Hyprism", "tools");

    /// <summary>Called when a download has no published SHA256. Receives (asset name, computed sha256); return true to install anyway.</summary>
    public static Func<string, string, Task<bool>> ConfirmUnverified { get; set; } = (_, _) => Task.FromResult(false);

    // ---------- threading ----------

    /// <summary>
    /// <c>await Sys.OffUiThread();</c> moves the rest of the method to the thread pool. Process scans, kills,
    /// file and registry work then never stall the UI's frames; the caller still resumes on its own thread.
    /// </summary>
    public static ThreadPoolHop OffUiThread() => default;

    public readonly struct ThreadPoolHop : System.Runtime.CompilerServices.INotifyCompletion
    {
        public ThreadPoolHop GetAwaiter() => this;
        public bool IsCompleted => SynchronizationContext.Current is null && Thread.CurrentThread.IsThreadPoolThread;
        public void OnCompleted(Action continuation) => ThreadPool.UnsafeQueueUserWorkItem(_ => continuation(), null);
        public void GetResult() { }
    }

    // ---------- processes ----------

    /// <param name="background">Run at below-normal priority so it can't steal CPU from the UI's frames.</param>
    /// <param name="onLine">Live output: called for every line and every in-place update (winget redraws its
    /// progress bar with \r), so the UI can show progress while the command runs.</param>
    /// <remarks>Cancelling kills the process and everything it started (e.g. an installer winget launched).</remarks>
    public static async Task<(int Exit, string Output)> RunAsync(string file, string args, CancellationToken ct = default, string? workDir = null, bool background = false, Action<string>? onLine = null)
    {
        var psi = new ProcessStartInfo(file, args)
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8, WorkingDirectory = workDir ?? "",
        };
        using var p = Process.Start(psi) ?? throw new InvalidOperationException($"Could not start {file}");
        if (background) try { p.PriorityClass = ProcessPriorityClass.BelowNormal; } catch { /* already exited */ }
        using var kill = ct.Register(() => { try { p.Kill(entireProcessTree: true); } catch { /* already exited */ } });

        var all = new StringBuilder();
        async Task Pump(StreamReader reader)
        {
            var buffer = new char[1024];
            var segment = new StringBuilder();
            int n;
            while ((n = await reader.ReadAsync(buffer.AsMemory(), CancellationToken.None)) > 0)
            {
                lock (all) all.Append(buffer, 0, n);
                if (onLine is null) continue;
                for (int i = 0; i < n; i++)
                {
                    char c = buffer[i];
                    if (c is '\r' or '\n') { if (segment.Length > 0) { onLine(segment.ToString()); segment.Clear(); } }
                    else segment.Append(c);
                }
            }
            if (onLine is not null && segment.Length > 0) onLine(segment.ToString());
        }
        var pumps = Task.WhenAll(Pump(p.StandardOutput), Pump(p.StandardError));
        await p.WaitForExitAsync(CancellationToken.None);
        await pumps;
        ct.ThrowIfCancellationRequested();
        return (p.ExitCode, all.ToString());
    }

    /// <summary>
    /// Reads winget's progress bar ("██████▒▒▒▒  45.2 MB / 100 MB" or "███▒▒  30%") into a 0..1 fraction.
    /// </summary>
    public static double? ParseProgress(string line)
    {
        var m = Regex.Match(line, @"(\d+(?:\.\d+)?)\s*(B|KB|MB|GB)\s*/\s*(\d+(?:\.\d+)?)\s*(B|KB|MB|GB)", RegexOptions.IgnoreCase);
        if (m.Success)
        {
            static double Bytes(string v, string unit) => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture) * unit.ToUpperInvariant() switch
            { "KB" => 1e3, "MB" => 1e6, "GB" => 1e9, _ => 1 };
            double done = Bytes(m.Groups[1].Value, m.Groups[2].Value), total = Bytes(m.Groups[3].Value, m.Groups[4].Value);
            return total > 0 ? Math.Clamp(done / total, 0, 1) : null;
        }
        m = Regex.Match(line, @"(?<![\d.])(\d{1,3})\s*%");
        return m.Success && int.Parse(m.Groups[1].Value) <= 100 ? int.Parse(m.Groups[1].Value) / 100.0 : null;
    }

    /// <summary>Runs with a UAC prompt. Output can't be captured across the elevation boundary.</summary>
    public static async Task<int> RunElevatedAsync(string file, string args, CancellationToken ct = default)
    {
        using var p = Process.Start(new ProcessStartInfo(file, args) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden })!;
        await p.WaitForExitAsync(ct);
        return p.ExitCode;
    }

    public static void Start(string path, string args = "") =>
        Process.Start(new ProcessStartInfo(path, args) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(path) ?? "" });

    public static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    public static bool IsRunning(string processName) => Process.GetProcessesByName(processName).Length > 0;

    /// <param name="tree">Also kill child processes. Fine for helper apps; never for explorer (its children are the user's apps).</param>
    public static void Kill(string processName, bool tree = true)
    {
        foreach (var p in Process.GetProcessesByName(processName))
            try { p.Kill(entireProcessTree: tree); p.WaitForExit(3000); } catch { /* already gone or not ours */ }
    }

    /// <summary>Full path of an executable on PATH (winget installs many tools as aliases in WindowsApps).</summary>
    public static string? FindOnPath(string exe) =>
        (Environment.GetEnvironmentVariable("PATH") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(d => { try { return Path.Combine(Environment.ExpandEnvironmentVariables(d.Trim()), exe); } catch { return null; } })
            .FirstOrDefault(p => p is not null && File.Exists(p));

    // ---------- fonts ----------

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct LOGFONT
    {
        public int lfHeight, lfWidth, lfEscapement, lfOrientation, lfWeight;
        public byte lfItalic, lfUnderline, lfStrikeOut, lfCharSet, lfOutPrecision, lfClipPrecision, lfQuality, lfPitchAndFamily;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string lfFaceName;
    }
    delegate int EnumFontProc(IntPtr logFont, IntPtr textMetric, uint fontType, IntPtr lParam);
    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)] static extern int EnumFontFamiliesEx(IntPtr hdc, ref LOGFONT lf, EnumFontProc proc, IntPtr lParam, uint flags);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

    /// <summary>Whether a font family is installed (system-wide or per-user), as apps like Windows Terminal see it.</summary>
    public static bool IsFontInstalled(string family)
    {
        if (string.IsNullOrWhiteSpace(family) || family.Length > 31) return false;
        var hdc = GetDC(IntPtr.Zero);
        try
        {
            bool found = false;
            var lf = new LOGFONT { lfCharSet = 1 /* DEFAULT_CHARSET */, lfFaceName = family };
            EnumFontFamiliesEx(hdc, ref lf, (_, _, _, _) => { found = true; return 0; }, IntPtr.Zero, 0);
            return found;
        }
        finally { ReleaseDC(IntPtr.Zero, hdc); }
    }

    /// <summary>
    /// Removes per-user font registrations whose file was deleted (left behind by an uninstaller). WinUI's text
    /// fallback trips over them and the whole app dies with DWRITE_E_FILENOTFOUND (0x88985003), so Hyprism repairs
    /// this before showing any UI. Only HKCU entries with a missing file are touched; the entry is useless anyway.
    /// </summary>
    public static List<string> RemoveBrokenUserFonts()
    {
        var removed = new List<string>();
        var userFonts = Path.Combine(LocalAppData, "Microsoft", "Windows", "Fonts");
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\Fonts", writable: true);
        foreach (var name in key?.GetValueNames() ?? [])
        {
            if (key!.GetValue(name) is not string { Length: > 0 } file) continue;
            var path = Path.IsPathRooted(file) ? file : Path.Combine(userFonts, file);
            if (File.Exists(path)) continue;
            key.DeleteValue(name, throwOnMissingValue: false);
            removed.Add(name);
        }
        if (removed.Count > 0) SendMessageTimeout(new IntPtr(0xFFFF), 0x001D /* WM_FONTCHANGE */, 0, 0, 0x0002, 1000, out _);
        return removed;
    }
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, nint wParam, nint lParam, uint flags, uint timeout, out nint result);

    /// <summary>First existing path, with environment variables expanded.</summary>
    public static string? FirstExisting(params string[] candidates) =>
        candidates.Select(Environment.ExpandEnvironmentVariables).FirstOrDefault(File.Exists);

    /// <summary>First file matching a pattern under %LocalAppData%\Packages (for MSIX apps whose family name we don't hard-code).</summary>
    public static string? FindInPackages(string packageGlob, string relative)
    {
        var root = Path.Combine(LocalAppData, "Packages");
        if (!Directory.Exists(root)) return null;
        return Directory.EnumerateDirectories(root, packageGlob).Select(d => Path.Combine(d, relative)).FirstOrDefault(File.Exists);
    }

    /// <summary>Exe of a winget "portable" package (zip installs land under WinGet\Packages\&lt;id&gt;_&lt;source hash&gt;).</summary>
    public static string? FindWingetPortable(string wingetId, string exeName)
    {
        var root = Path.Combine(LocalAppData, "Microsoft", "WinGet", "Packages");
        if (!Directory.Exists(root)) return null;
        return Directory.EnumerateDirectories(root, wingetId + "_*")
            .SelectMany(d => Directory.EnumerateFiles(d, exeName, SearchOption.AllDirectories)).FirstOrDefault();
    }

    static readonly Dictionary<string, string?> AumidCache = [];

    /// <summary>AppUserModelID of a Store/MSIX app by its Start menu name (those can't be launched by exe path).</summary>
    public static async Task<string?> FindAumidAsync(string startMenuName)
    {
        if (AumidCache.TryGetValue(startMenuName, out var cached) && cached is not null) return cached;
        var (_, output) = await RunAsync("powershell", $"-NoProfile -Command \"(Get-StartApps | Where-Object Name -like '{startMenuName.Replace("'", "''")}' | Select-Object -First 1).AppID\"");
        var id = output.Trim();
        return AumidCache[startMenuName] = id.Length > 0 && !id.Contains(' ') ? id : null;
    }

    // ---------- explorer ----------

    /// <summary>Set while Hyprism itself restarts Explorer, so safe mode doesn't count it as a crash.</summary>
    public static DateTime LastIntentionalExplorerRestart { get; private set; }

    public static async Task RestartExplorerAsync()
    {
        LastIntentionalExplorerRestart = DateTime.UtcNow;
        // Only Explorer itself: killing its process tree would also kill every app the user started from the
        // taskbar or Start menu.
        Kill("explorer", tree: false);
        // Windows restarts the shell by itself (AutoRestartShell), which re-registers everything the shell
        // provides. Starting it ourselves is the last resort.
        for (int i = 0; i < 25 && !IsRunning("explorer"); i++) await Task.Delay(200);
        if (!IsRunning("explorer")) Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = true });
    }

    // ---------- autostart (HKCU Run) ----------

    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void SetAutostart(string name, string? command)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (command is null) k.DeleteValue(name, false); else k.SetValue(name, command);
    }

    public static bool HasAutostart(string name)
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue(name) is not null;
    }

    // ---------- winget ----------

    /// <summary>
    /// One winget at a time. Two concurrent winget processes fight over its shared index and download files and
    /// fail with 0x80070020 ("file is being used by another process").
    /// </summary>
    static readonly SemaphoreSlim WingetLane = new(1);
    static CancellationTokenSource? inventoryCts;

    /// <summary>Runs a user-requested winget command: pauses background status checks, waits its turn, retries
    /// temporary "file in use" / download failures, and streams progress the whole time.</summary>
    static async Task<int> RunWingetForUserAsync(string args, IProgress<string>? log, CancellationToken ct)
    {
        inventoryCts?.Cancel(); // the user's action wins over a background status refresh
        if (!await WingetLane.WaitAsync(0, ct))
        {
            log?.Report("Waiting for another winget task to finish…");
            await WingetLane.WaitAsync(ct);
        }
        try
        {
            for (int attempt = 1; ; attempt++)
            {
                var (exit, _) = await RunAsync("winget", args, ct, onLine: StreamTo(log));
                uint code = (uint)exit;
                bool transient = code is 0x80070020 or 0x80070021 // file in use / locked (antivirus scan, another installer)
                                 or 0x8A150008;                  // download failed
                if (!transient || attempt == 3) return exit;
                var wait = TimeSpan.FromSeconds(5 * attempt);
                log?.Report($"winget hit a temporary problem (0x{code:X8}{WingetHint(code)}). Retrying in {wait.TotalSeconds:0} s (attempt {attempt + 1} of 3)…");
                await Task.Delay(wait, ct);
            }
        }
        finally { WingetLane.Release(); }
    }

    public static async Task<bool> WingetInstallAsync(string id, string source, IProgress<string>? log, CancellationToken ct)
    {
        log?.Report($"winget install {id}");
        ForgetWingetStatus(id);
        var exit = await RunWingetForUserAsync(
            $"install --id {id} --exact --source {source} --silent --accept-package-agreements --accept-source-agreements --disable-interactivity", log, ct);
        ForgetWingetStatus(id);
        // 0 = ok; 0x8A15002B = already installed and no upgrade available.
        bool ok = exit == 0 || (uint)exit == 0x8A15002B;
        if (!ok) log?.Report($"winget finished with code 0x{(uint)exit:X8}{WingetHint((uint)exit)}");
        return ok;
    }

    public static async Task<bool> WingetUninstallAsync(string id, IProgress<string>? log, CancellationToken ct)
    {
        log?.Report($"winget uninstall {id}");
        ForgetWingetStatus(id);
        var exit = await RunWingetForUserAsync($"uninstall --id {id} --exact --silent --accept-source-agreements --disable-interactivity", log, ct);
        if (exit != 0) log?.Report($"winget finished with code 0x{(uint)exit:X8}{WingetHint((uint)exit)}");
        return exit == 0;
    }

    static string WingetHint(uint code) => code switch
    {
        0x80070020 or 0x80070021 => " (a file was in use by another program, often an antivirus scan or another installer)",
        0x8A150014 => " (no installer for this PC)",
        0x8A150104 => " (the package lists a dependency winget can't find; a problem in its winget listing)",
        0x8A150011 => " (the installer's hash didn't match; try again later)",
        0x8A150008 or 0x8A15000F => " (download failed; check your connection)",
        0x80073D02 => " (the app is running; close it and retry)",
        0x8A150103 or 0x8A15010C => " (an administrator prompt was declined)",
        _ => "",
    };

    /// <summary>
    /// Forwards winget's live output: drops spinner frames, keeps real messages, and throttles progress-bar redraws
    /// (winget repaints them many times a second) to a few updates per second.
    /// </summary>
    static Action<string>? StreamTo(IProgress<string>? log)
    {
        if (log is null) return null;
        var last = DateTime.MinValue;
        string? lastLine = null;
        return raw =>
        {
            var line = raw.Trim();
            if (line.Length < 2 || line.All(c => "-\\|/ █▒".Contains(c)) || line == lastLine) return;
            if (ParseProgress(line) is not null)
            {
                if (DateTime.UtcNow - last < TimeSpan.FromMilliseconds(200)) return;
                last = DateTime.UtcNow;
            }
            lastLine = line;
            log.Report(line);
        };
    }

    // winget is heavy (hundreds of MB, seconds of CPU per run). Instead of one `winget list` per module, Hyprism
    // takes ONE inventory (`winget export` for installed IDs + versions, `winget upgrade` for updates), at below-normal
    // priority, shared by every page and cached for 10 minutes.
    static Task<Dictionary<string, (string? Version, string? Available)>>? inventory;
    static DateTime inventoryAt;
    static readonly object InventoryGate = new();
    static readonly TimeSpan InventoryTtl = TimeSpan.FromMinutes(10);

    static readonly string InventoryFile = Path.Combine(LocalAppData, "Hyprism", "winget-inventory.json");
    static bool refreshing;

    /// <summary>
    /// Installed version and available upgrade for a package, from the shared inventory.
    /// Stale-while-revalidate: the last inventory saved on disk answers instantly while a new one is taken in the
    /// background (a full `winget export` can take 30+ seconds on a PC with many packages).
    /// </summary>
    public static async Task<(bool Installed, string? Version, string? Available)> WingetListAsync(string id, CancellationToken ct)
    {
        Task<Dictionary<string, (string? Version, string? Available)>> task;
        lock (InventoryGate)
        {
            if (inventory is null && ReadInventoryFile() is { } saved)
            {
                inventory = Task.FromResult(saved);
                inventoryAt = DateTime.MinValue; // stale: answer now, refresh below
            }
            if (inventory is null || inventory.IsFaulted || inventory.IsCanceled)
            {
                inventory = LoadInventoryAsync();
                inventoryAt = DateTime.UtcNow;
            }
            else if (DateTime.UtcNow - inventoryAt > InventoryTtl && !refreshing)
            {
                refreshing = true;
                _ = RefreshInBackgroundAsync();
            }
            task = inventory;
        }
        var all = await task.WaitAsync(ct);
        return all.TryGetValue(id, out var v) ? (true, v.Version, v.Available) : (false, null, null);
    }

    static async Task RefreshInBackgroundAsync()
    {
        try
        {
            var fresh = await LoadInventoryAsync();
            lock (InventoryGate) { inventory = Task.FromResult(fresh); inventoryAt = DateTime.UtcNow; } // pages pick it up on their next visit
        }
        catch { /* keep the stale copy */ }
        finally { refreshing = false; }
    }

    static Dictionary<string, (string? Version, string? Available)>? ReadInventoryFile()
    {
        try
        {
            if (!File.Exists(InventoryFile) || DateTime.UtcNow - File.GetLastWriteTimeUtc(InventoryFile) > TimeSpan.FromDays(7)) return null;
            var o = JsonNode.Parse(File.ReadAllText(InventoryFile))!.AsObject();
            return o.ToDictionary(kv => kv.Key, kv => ((string?)kv.Value?["v"], (string?)kv.Value?["a"]), StringComparer.OrdinalIgnoreCase);
        }
        catch { return null; }
    }

    /// <summary>Call after installs/uninstalls so the next status check takes a fresh inventory.</summary>
    public static void ForgetWingetStatus(string id)
    {
        lock (InventoryGate) inventory = null;
        try { File.Delete(InventoryFile); } catch { }
    }

    static async Task<Dictionary<string, (string? Version, string? Available)>> LoadInventoryAsync()
    {
        await OffUiThread();
        var result = new Dictionary<string, (string? Version, string? Available)>(StringComparer.OrdinalIgnoreCase);
        var file = Path.Combine(Path.GetTempPath(), $"hyprism-winget-{Environment.ProcessId}.json");
        // Shares the winget lane with installs; an install that starts meanwhile cancels this (status is re-read after it).
        var cts = new CancellationTokenSource();
        inventoryCts = cts;
        await WingetLane.WaitAsync(cts.Token);
        string upgrades;
        try
        {
            await RunAsync("winget", $"export -o \"{file}\" --include-versions --accept-source-agreements --disable-interactivity", cts.Token, background: true);
            if (File.Exists(file))
            {
                var json = JsonNode.Parse(await File.ReadAllTextAsync(file));
                foreach (var source in json?["Sources"]?.AsArray() ?? [])
                    foreach (var pkg in source?["Packages"]?.AsArray() ?? [])
                        if ((string?)pkg?["PackageIdentifier"] is { } pid) result[pid] = ((string?)pkg?["Version"], null);
            }
            (_, upgrades) = await RunAsync("winget", "upgrade --accept-source-agreements --disable-interactivity", cts.Token, background: true);
        }
        finally
        {
            WingetLane.Release();
            try { File.Delete(file); } catch { }
        }
        var table = CleanWinget(upgrades);
        foreach (var id in result.Keys.ToList())
            if (ParseWingetList(table, id) is (true, _, { } available)) result[id] = (result[id].Version, available);

        if (result.Count > 0)
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(InventoryFile)!);
                var o = new JsonObject();
                foreach (var (k, v) in result) o[k] = new JsonObject { ["v"] = v.Version, ["a"] = v.Available };
                File.WriteAllText(InventoryFile, o.ToJsonString());
            }
            catch { /* cache only */ }
        return result;
    }

    /// <summary>
    /// Finds a package row in a winget table (`list` or `upgrade`) and returns (found, version, available).
    /// winget truncates long IDs with "…" when the table is wide, so a truncated ID that prefixes ours also matches.
    /// </summary>
    public static (bool, string?, string?) ParseWingetList(string output, string id)
    {
        var lines = output.Split('\n').Select(l => l.TrimEnd()).ToList();
        var header = lines.FirstOrDefault(l => l.Contains(" Id ") || l.Contains(" ID "));
        bool hasAvailable = header?.Contains("Available") == true || header?.Contains("Verfügbar") == true;
        bool hasSource = header?.Contains("Source") == true;
        foreach (var line in lines)
        {
            if (line == header) continue;
            var tokens = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            int t = Array.FindIndex(tokens, tok => tok.Equals(id, StringComparison.OrdinalIgnoreCase)
                || (tok.EndsWith('…') && tok.Length > 4 && id.StartsWith(tok[..^1], StringComparison.OrdinalIgnoreCase)));
            if (t < 0) continue;
            var after = tokens[(t + 1)..];
            string? version = after.ElementAtOrDefault(0);
            string? available = hasAvailable && after.Length >= (hasSource ? 3 : 2) ? after[1] : null;
            return (true, version, available);
        }
        return (false, null, null);
    }

    // winget writes spinner frames separated by \r; keep only what follows the last \r on each line.
    static string CleanWinget(string s) =>
        string.Join('\n', s.Split('\n').Select(l => l.Split('\r', StringSplitOptions.RemoveEmptyEntries).LastOrDefault()?.Trim() ?? "")
            .Where(l => l.Length > 1 && !l.All(c => "-\\|/ █▒".Contains(c))));

    // ---------- GitHub Releases fallback ----------

    static readonly HttpClient Http = CreateHttp();
    static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Hyprism/1.0 (+https://github.com/hyprism)");
        return h;
    }

    /// <summary>
    /// Downloads the newest release asset whose name matches <paramref name="assetPattern"/>, verifies its SHA256
    /// against the digest GitHub publishes (or asks the user when there is none), and unpacks it into <paramref name="dest"/>.
    /// Returns the release tag.
    /// </summary>
    public static async Task<string> InstallGitHubReleaseAsync(string repo, string assetPattern, string dest, IProgress<string>? log, CancellationToken ct)
    {
        var release = await Http.GetFromJsonAsync<JsonObject>($"https://api.github.com/repos/{repo}/releases/latest", ct)
            ?? throw new InvalidOperationException($"No releases for {repo}");
        var asset = release["assets"]!.AsArray().Select(a => a!.AsObject())
            .FirstOrDefault(a => Regex.IsMatch((string)a["name"]!, assetPattern, RegexOptions.IgnoreCase))
            ?? throw new InvalidOperationException($"{repo}: no release asset matches {assetPattern}");
        var name = (string)asset["name"]!;
        log?.Report($"Downloading {name} from {repo} {release["tag_name"]}");
        byte[] bytes;
        using (var resp = await Http.GetAsync((string)asset["browser_download_url"]!, HttpCompletionOption.ResponseHeadersRead, ct))
        {
            resp.EnsureSuccessStatusCode();
            long total = resp.Content.Headers.ContentLength ?? 0;
            await using var src = await resp.Content.ReadAsStreamAsync(ct);
            using var ms = new MemoryStream();
            var buffer = new byte[81920];
            var last = DateTime.MinValue;
            int n;
            while ((n = await src.ReadAsync(buffer, ct)) > 0)
            {
                ms.Write(buffer, 0, n);
                if (total > 0 && DateTime.UtcNow - last > TimeSpan.FromMilliseconds(200))
                {
                    last = DateTime.UtcNow;
                    log?.Report($"Downloading {ms.Length / 1e6:0.0} MB / {total / 1e6:0.0} MB");
                }
            }
            bytes = ms.ToArray();
        }
        var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

        var expected = ((string?)asset["digest"])?.Replace("sha256:", "");
        if (expected is not null)
        {
            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"SHA256 mismatch for {name}: expected {expected}, got {actual}");
            log?.Report($"SHA256 verified ({actual[..12]}…)");
        }
        else if (!await ConfirmUnverified(name, actual))
            throw new OperationCanceledException($"{name} has no published checksum and was not approved.");

        // Antivirus before anything is unpacked: the archive and every file in it.
        Antivirus.EnsureClean(bytes, name, log);

        Directory.CreateDirectory(dest);
        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = new ZipArchive(new MemoryStream(bytes));
            zip.ExtractToDirectory(dest, overwriteFiles: true);
        }
        else await File.WriteAllBytesAsync(Path.Combine(dest, name), bytes, ct);
        var tag = (string)release["tag_name"]!;
        await File.WriteAllTextAsync(Path.Combine(dest, ".hyprism-version"), tag, ct);
        return tag;
    }

    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (DateTime At, string? Tag)> TagCache = new();

    /// <summary>Latest release tag, cached for an hour (the unauthenticated GitHub API allows 60 calls/hour).</summary>
    public static async Task<string?> LatestGitHubTagAsync(string repo, CancellationToken ct)
    {
        if (TagCache.TryGetValue(repo, out var hit) && DateTime.UtcNow - hit.At < TimeSpan.FromHours(1)) return hit.Tag;
        string? tag;
        try { tag = (string?)(await Http.GetFromJsonAsync<JsonObject>($"https://api.github.com/repos/{repo}/releases/latest", ct))?["tag_name"]; }
        catch { tag = null; } // offline or rate-limited: just don't report an update
        TagCache[repo] = (DateTime.UtcNow, tag);
        return tag;
    }

    // ---------- Win32 ----------

    [DllImport("user32.dll", EntryPoint = "SendMessageTimeoutW", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);

    /// <summary>Tells running apps that a user setting (theme, colors, environment) changed.</summary>
    public static void BroadcastSettingChange(string area) =>
        SendMessageTimeout(0xFFFF /*HWND_BROADCAST*/, 0x001A /*WM_SETTINGCHANGE*/, UIntPtr.Zero, area, 0x0002 /*SMTO_ABORTIFHUNG*/, 2000, out _);
}
