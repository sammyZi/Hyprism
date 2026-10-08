// Self-checks for the logic that writes other apps' configs. Run: dotnet run --project tests/Hyprism.Checks
using Hyprism.Core;
using Hyprism.Modules;
using Hyprism.Theming;

int failures = 0;
void Check(bool ok, string what) { Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {what}"); if (!ok) failures++; }

// Colors
var c = Rgb.Parse("#CBA6F7");
Check(c.Hex == "#CBA6F7" && c.HexRgba(0x80) == "#CBA6F780" && c.HexA(0x80) == "#80CBA6F7", "hex formats");
var (h, s, l) = c.ToHsl();
Check(Rgb.FromHsl(h, s, l) == c, "HSL round trip");
Check(Math.Abs(Rgb.Contrast(new(0, 0, 0), new(255, 255, 255)) - 21) < 0.01, "WCAG contrast black/white = 21");

// Palette engine: a mostly-red sunset with a dark band
var px = new List<Rgb>();
for (int i = 0; i < 3000; i++) px.Add(new(220, 60, 40));
for (int i = 0; i < 2000; i++) px.Add(new(20, 15, 30));
for (int i = 0; i < 500; i++) px.Add(new(250, 200, 120));
var p = PaletteExtractor.FromPixels(px);
Check(p.Ansi.Length == 16, "16 ANSI colors");
Check(Rgb.Contrast(p.Foreground, p.Background) >= 7, $"text contrast >= 7 (got {Rgb.Contrast(p.Foreground, p.Background):0.0})");
Check(p.Ansi.Skip(1).Take(6).All(a => Rgb.Contrast(a, p.Background) >= 3), "ANSI colors readable on background");
var accentHue = p.Accent.ToHsl().H;
Check(accentHue < 40 || accentHue > 340, $"accent follows the red wallpaper (hue {accentHue:0})");
Check(PaletteExtractor.FromPixels(px, dark: false).Background.Luminance > 0.7, "light variant has a light background");

// INI editing (TrafficMonitor, ExplorerBlurMica)
var ini = new List<string> { "[config]", "a=1", "[task_bar]", "font_name=Segoe UI" };
Files.SetIniLines(ini, "task_bar", "font_name", "Google Sans Flex");
Files.SetIniLines(ini, "config", "b", "2");
Files.SetIniLines(ini, "connection", "select_all", "false");
Check(ini.SequenceEqual(["[config]", "a=1", "b=2", "[task_bar]", "font_name=Google Sans Flex", "[connection]", "select_all=false"]), "INI replace/insert/new section");

// Managed block in a PowerShell profile keeps the user's own lines
var tmp = Path.Combine(Path.GetTempPath(), $"hyprism-check-{Guid.NewGuid():N}.ps1");
File.WriteAllText(tmp, "Set-Alias g git\r\n");
Files.SetManagedBlock(tmp, "oh-my-posh init pwsh | iex");
Files.SetManagedBlock(tmp, "oh-my-posh init pwsh --config x | iex");
var text = File.ReadAllText(tmp);
Check(text.Contains("Set-Alias g git") && text.Split(">>> hyprism >>>").Length == 2 && text.Contains("--config x"), "managed block is replaced, not duplicated");
Files.SetManagedBlock(tmp, null);
Check(!File.ReadAllText(tmp).Contains("hyprism") && File.ReadAllText(tmp).Contains("Set-Alias g git"), "managed block removal");
File.Delete(tmp);

// winget list parsing
var list = """
    Name           Id                        Version       Available     Source
    ----------------------------------------------------------------------------
    Windows Terminal Microsoft.WindowsTerminal 1.24.12741.0  1.25.2733.0   winget
    """;
var (installed, version, available) = Sys.ParseWingetList(list, "Microsoft.WindowsTerminal");
Check(installed && version == "1.24.12741.0" && available == "1.25.2733.0", "winget list with an upgrade");
var noUpgrade = "Name     Id                Version Source\n------------------------------------\nGlazeWM  glzr-io.glazewm   3.10.1  winget\n";
var r2 = Sys.ParseWingetList(noUpgrade, "glzr-io.glazewm");
Check(r2.Item1 && r2.Item2 == "3.10.1" && r2.Item3 is null, "winget list without an upgrade");
Check(!Sys.ParseWingetList("No installed package found matching input criteria.", "x.y").Item1, "winget list: not installed");
var truncated = "Name        Id                   Version  Available  Source\n-----------------------------------------\nTrafficMon  zhongyang219.Traffi… 1.85     1.86       winget\n";
Check(Sys.ParseWingetList(truncated, "zhongyang219.TrafficMonitor.Full") is (true, "1.85", "1.86"), "winget upgrade table with a truncated ID");

// Keybinding translation
Check(Tiling.GlazeCommands("move to workspace 3", "wt").SequenceEqual(["move --workspace 3", "focus --workspace 3"]), "GlazeWM: move to workspace");
Check(Tiling.KomorebiCommand("workspace 1", "wt") == "komorebic focus-workspace 0", "komorebi workspaces are 0-based");
Check(Tiling.WhkdKeys("alt+shift+enter") == "alt + shift + return", "whkd key names");
Check(Tiling.Actions.All(a => Tiling.GlazeCommands(a, "wt").Length > 0 && Tiling.KomorebiCommand(a, "wt") is not null), "every action maps to both engines");

// Auto Dark Mode YAML edits
var yaml = new List<string> { "AutoThemeSwitchingEnabled: false", "Location:", "  Enabled: false", "  UseGeolocatorService: true", "Sunrise: x" };
AutoDarkMode.SetYaml(yaml, null, "AutoThemeSwitchingEnabled", "true");
AutoDarkMode.SetYaml(yaml, "Location", "Enabled", "true");
Check(yaml[0] == "AutoThemeSwitchingEnabled: true" && yaml[2] == "  Enabled: true" && yaml.Count == 5, "ADM yaml top-level and nested keys");

// Generators
var scheme = Generators.TerminalScheme(StarterThemes.Nord);
Check((string?)scheme["brightCyan"] == "#8FBCBB" && (string?)scheme["purple"] == "#B48EAD", "Terminal scheme uses WT key names");
Check(Generators.FlowLauncherTheme(StarterThemes.Gruvbox, true).Contains("ThemeBlurEnabled\">True"), "Flow theme blur flag");

// Live install progress
Check(Math.Abs((Sys.ParseProgress("  ██████████▒▒▒▒▒▒▒▒▒▒  45.2 MB / 100 MB") ?? -1) - 0.452) < 0.001, "winget MB progress parsed");
Check(Sys.ParseProgress("  ████▒▒▒▒  30%") == 0.30, "winget percent progress parsed");
Check(Sys.ParseProgress("Successfully verified installer hash") is null, "plain message isn't progress");
var lines = new List<string>();
var (rc, _) = await Sys.RunAsync("cmd.exe", "/c echo first& echo second", onLine: lines.Add);
Check(rc == 0 && lines.SequenceEqual(["first", "second"]), "output is streamed line by line while the command runs");
using (var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300)))
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    bool cancelled = false;
    try { await Sys.RunAsync("cmd.exe", "/c ping -n 30 127.0.0.1 >nul", cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
    Check(cancelled && sw.Elapsed < TimeSpan.FromSeconds(5), "Cancel stops a running command right away");
}

// Antivirus (AMSI): every download is scanned before use
var cleanVerdict = Antivirus.Scan("just a harmless test buffer from Hyprism"u8.ToArray(), "hyprism-check.txt");
Check(cleanVerdict is ScanVerdict.Clean or ScanVerdict.Unavailable, $"harmless content passes the antivirus scan ({cleanVerdict})");
if (args.Contains("--av"))
{
    // EICAR: the industry-standard, harmless antivirus test file. Stored reversed and in pieces so this program's
    // own files never contain it; Defender will show one "EICAR test file" notification for this check.
    var eicar = new string(("*H+H$!ELIF-TSET-SURIVITNA-DRADNATS-RACIE$}7)CC7)^P(45XZP\\4[PA@%P!O5X").Reverse().ToArray());
    bool blocked = false;
    try { Antivirus.EnsureClean(System.Text.Encoding.ASCII.GetBytes(eicar), "eicar-test.com"); }
    catch (InvalidOperationException) { blocked = true; }
    Check(blocked, "a flagged download (EICAR test) is refused");
}

// Font detection (Glass Terminal must never point Terminal at a missing font)
Check(Sys.IsFontInstalled("Segoe UI"), "installed font detected (Segoe UI)");
Check(!Sys.IsFontInstalled("Hyprism No Such Font"), "missing font reported as missing");

// Online wallpaper downloads must really be images
Check(OnlineWallpapers.ImageExtension([0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0]) == ".jpg", "JPEG detected");
Check(OnlineWallpapers.ImageExtension([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0]) == ".png", "PNG detected");
Check(OnlineWallpapers.ImageExtension("RIFF\0\0\0\0WEBP"u8.ToArray()) == ".webp", "WebP detected");
Check(OnlineWallpapers.ImageExtension("<html>error</html>"u8.ToArray()) is null, "HTML error page rejected");
// Live check of every wallpaper source (needs internet): `dotnet run --project tests/Hyprism.Checks -- --online`
if (args.Contains("--online"))
{
    foreach (var (src, q) in new[] { (PhotoSource.Wallhaven, "mountains"), (PhotoSource.Openverse, "forest"), (PhotoSource.Nasa, "nebula"), (PhotoSource.Picsum, "") })
    {
        try
        {
            var found = await OnlineWallpapers.SearchAsync(src, q, 1, 1920, 1080);
            var thumb = await OnlineWallpapers.ThumbAsync(found[0]);
            var file = await OnlineWallpapers.DownloadAsync(found[0], 1920, 1080);
            var size = new FileInfo(file).Length;
            Check(found.Count > 0 && OnlineWallpapers.ImageExtension(thumb) is not null && size > 20_000,
                $"{src}: {found.Count} results, preview {thumb.Length / 1024} KB, download {size / 1024} KB ({Path.GetExtension(file)})");
            File.Delete(file);
            File.Delete(Path.ChangeExtension(file, ".txt"));
        }
        catch (Exception e) { Check(false, $"{src}: {e.Message}"); }
    }
}

// Every module declares sane settings
foreach (var m in Catalog.All)
{
    Check(m.Settings.Select(d => d.Key).Distinct().Count() == m.Settings.Count, $"{m.Name}: unique setting keys");
    Check(m.Settings.Where(d => d.Kind == SettingKind.Choice).All(d => d.Options!.Contains(d.Default!.ToString())), $"{m.Name}: choice defaults are valid options");
}

Console.WriteLine(failures == 0 ? "\nAll checks passed." : $"\n{failures} check(s) failed.");
return failures == 0 ? 0 : 1;
