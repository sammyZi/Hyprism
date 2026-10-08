using System.Runtime.InteropServices;
using Hyprism.Theming;
using Microsoft.Win32;

namespace Hyprism.Core;

/// <summary>Built-in Windows appearance settings: wallpaper, accent, transparency, light/dark.</summary>
public static class WindowsLook
{
    const string Personalize = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    const string Dwm = @"Software\Microsoft\Windows\DWM";
    const string Accent = @"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent";

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SystemParametersInfo(uint action, uint uParam, string? pvParam, uint winIni);

    public static void SetWallpaper(string path)
    {
        // SPI_SETDESKWALLPAPER, SPIF_UPDATEINIFILE | SPIF_SENDCHANGE
        if (!SystemParametersInfo(0x0014, 0, Path.GetFullPath(path), 0x01 | 0x02))
            throw new InvalidOperationException($"Windows refused the wallpaper (error {Marshal.GetLastWin32Error()}).");
    }

    public static string? GetWallpaper()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
        return k?.GetValue("WallPaper") as string is { Length: > 0 } p && File.Exists(p) ? p : null;
    }

    public static bool Transparency
    {
        get { using var k = Registry.CurrentUser.OpenSubKey(Personalize); return (k?.GetValue("EnableTransparency") as int? ?? 1) != 0; }
        set { using var k = Registry.CurrentUser.CreateSubKey(Personalize); k.SetValue("EnableTransparency", value ? 1 : 0); Sys.BroadcastSettingChange("ImmersiveColorSet"); }
    }

    public static bool AppsDark
    {
        get { using var k = Registry.CurrentUser.OpenSubKey(Personalize); return (k?.GetValue("AppsUseLightTheme") as int? ?? 1) == 0; }
    }

    /// <summary>
    /// Sets the Windows accent color. There's no public API, so this writes the same registry values the
    /// Settings app writes and broadcasts the change. Taskbar/Start pick it up immediately on 10 and 11.
    /// </summary>
    public static void SetAccent(Rgb c)
    {
        int abgr = unchecked((int)(0xFF000000 | (uint)c.B << 16 | (uint)c.G << 8 | c.R));
        int argb = unchecked((int)(0xC4000000 | (uint)c.R << 16 | (uint)c.G << 8 | c.B));
        using (var k = Registry.CurrentUser.CreateSubKey(Dwm))
        {
            k.SetValue("AccentColor", abgr, RegistryValueKind.DWord);
            k.SetValue("ColorizationColor", argb, RegistryValueKind.DWord);
            k.SetValue("ColorizationAfterglow", argb, RegistryValueKind.DWord);
        }
        using (var k = Registry.CurrentUser.CreateSubKey(Accent))
        {
            // AccentPalette: 8 RGBA swatches from light to dark; index 3 is the accent itself.
            var shades = new[] { 0.85, 0.72, 0.6, -1, 0.38, 0.28, 0.18, 0.5 };
            var bytes = new byte[32];
            for (int i = 0; i < 8; i++)
            {
                var s = shades[i] < 0 ? c : c.WithLightness(shades[i]);
                bytes[i * 4] = s.R; bytes[i * 4 + 1] = s.G; bytes[i * 4 + 2] = s.B; bytes[i * 4 + 3] = 0;
            }
            k.SetValue("AccentPalette", bytes, RegistryValueKind.Binary);
            k.SetValue("AccentColorMenu", abgr, RegistryValueKind.DWord);
            k.SetValue("StartColorMenu", abgr, RegistryValueKind.DWord);
        }
        Sys.BroadcastSettingChange("ImmersiveColorSet");
    }

    // ---------- original look backup ----------

    static readonly string LookDir = Path.Combine(Store.Root, "backups", "windows-look");
    static readonly string[] Keys = [@"HKCU\Control Panel\Desktop", $@"HKCU\{Personalize}", $@"HKCU\{Dwm}", $@"HKCU\{Accent}"];

    public static bool HasOriginalBackup => Directory.Exists(LookDir);

    /// <summary>Snapshot of the stock look, taken once before Hyprism changes anything.</summary>
    public static async Task BackupOriginalAsync()
    {
        if (HasOriginalBackup) return;
        Directory.CreateDirectory(LookDir);
        for (int i = 0; i < Keys.Length; i++)
            await Sys.RunAsync("reg", $"export \"{Keys[i]}\" \"{Path.Combine(LookDir, $"{i}.reg")}\" /y");
        if (GetWallpaper() is { } wp) File.Copy(wp, Path.Combine(LookDir, "wallpaper" + Path.GetExtension(wp)), true);
        else if (File.Exists(CachedWallpaper)) File.Copy(CachedWallpaper, Path.Combine(LookDir, "wallpaper.jpg"), true);
    }

    /// <summary>Windows' own copy of the current wallpaper. It survives even when the original file is gone.</summary>
    static string CachedWallpaper => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Themes\TranscodedWallpaper");

    /// <summary>
    /// If the wallpaper file Windows points at was deleted, the desktop turns black at the next reload (sign-in,
    /// Explorer restart, display change). Puts Windows' cached copy back at that path. Returns true if it repaired.
    /// </summary>
    public static bool RepairMissingWallpaper()
    {
        using var k = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
        if (k?.GetValue("WallPaper") is not string { Length: > 0 } path || File.Exists(path) || !File.Exists(CachedWallpaper)) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.Copy(CachedWallpaper, path);
        SetWallpaper(path);
        return true;
    }

    public static async Task RestoreOriginalAsync()
    {
        if (!HasOriginalBackup) return;
        foreach (var reg in Directory.GetFiles(LookDir, "*.reg"))
            await Sys.RunAsync("reg", $"import \"{reg}\"");
        if (Directory.GetFiles(LookDir, "wallpaper.*").FirstOrDefault() is { } wp) SetWallpaper(wp);
        if (File.Exists(LockScreenBackup)) await SetLockScreenAsync(LockScreenBackup, backupFirst: false);
        Sys.BroadcastSettingChange("ImmersiveColorSet");
    }

    // ---------- lock screen ----------

    static string LockScreenBackup => Path.Combine(LookDir, "lockscreen.jpg");

    /// <summary>
    /// Sets the lock screen picture through Windows' LockScreen API (no admin needed). The first time, the current
    /// lock screen image is saved so "Restore original look" can put it back. Note: if Windows Spotlight was on,
    /// Windows switches the lock screen to "Picture"; turn Spotlight back on in Settings > Personalization > Lock screen.
    /// </summary>
    public static async Task SetLockScreenAsync(string path, bool backupFirst = true)
    {
        if (backupFirst && !File.Exists(LockScreenBackup))
        {
            try
            {
                Directory.CreateDirectory(LookDir);
                using var current = Windows.System.UserProfile.LockScreen.GetImageStream();
                if (current is not null)
                {
                    await using var src = current.AsStreamForRead();
                    await using var dst = File.Create(LockScreenBackup);
                    await src.CopyToAsync(dst);
                }
            }
            catch { /* no readable current image (e.g. Spotlight): nothing to back up */ }
        }
        var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        await Windows.System.UserProfile.LockScreen.SetImageFileAsync(file);
    }
}
