using System.Runtime.InteropServices;

namespace Hyprism.App;

/// <summary>System-wide hotkeys via RegisterHotKey, delivered through a subclass of the main window.</summary>
public sealed class Hotkeys : IDisposable
{
    const int WM_HOTKEY = 0x0312;
    const uint MOD_ALT = 1, MOD_CONTROL = 2, MOD_SHIFT = 4, MOD_WIN = 8, MOD_NOREPEAT = 0x4000;

    delegate IntPtr SubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, IntPtr id, IntPtr data);
    [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(IntPtr hWnd, SubclassProc proc, IntPtr id, IntPtr data);
    [DllImport("comctl32.dll")] static extern bool RemoveWindowSubclass(IntPtr hWnd, SubclassProc proc, IntPtr id);
    [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    readonly IntPtr hwnd;
    readonly SubclassProc proc; // kept in a field so the GC never collects the callback
    readonly Dictionary<int, Action> actions = [];
    int nextId = 1;

    public Hotkeys(IntPtr hwnd)
    {
        this.hwnd = hwnd;
        proc = WndProc;
        SetWindowSubclass(hwnd, proc, 1, 0);
    }

    IntPtr WndProc(IntPtr h, uint msg, IntPtr w, IntPtr l, IntPtr id, IntPtr data)
    {
        if (msg == WM_HOTKEY && actions.TryGetValue((int)w, out var a)) a();
        return DefSubclassProc(h, msg, w, l);
    }

    public void Clear()
    {
        foreach (var id in actions.Keys) UnregisterHotKey(hwnd, id);
        actions.Clear();
    }

    /// <summary>Registers "win+alt+p"-style shortcuts. Returns false when another app already owns it.</summary>
    public bool Register(string shortcut, Action action)
    {
        if (!TryParse(shortcut, out var mods, out var vk)) return false;
        int id = nextId++;
        if (!RegisterHotKey(hwnd, id, mods | MOD_NOREPEAT, vk)) return false;
        actions[id] = action;
        return true;
    }

    public static bool TryParse(string shortcut, out uint mods, out uint vk)
    {
        mods = 0; vk = 0;
        foreach (var part in shortcut.ToLowerInvariant().Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (part)
            {
                case "win" or "super": mods |= MOD_WIN; break;
                case "ctrl" or "control": mods |= MOD_CONTROL; break;
                case "alt": mods |= MOD_ALT; break;
                case "shift": mods |= MOD_SHIFT; break;
                case { Length: 1 } c when char.IsLetterOrDigit(c[0]): vk = char.ToUpperInvariant(c[0]); break;
                case ['f', .. var n] when int.TryParse(n, out var f) && f is >= 1 and <= 24: vk = (uint)(0x70 + f - 1); break;
                case "space": vk = 0x20; break;
                case "enter": vk = 0x0D; break;
                case "left": vk = 0x25; break;
                case "up": vk = 0x26; break;
                case "right": vk = 0x27; break;
                case "down": vk = 0x28; break;
                default: return false;
            }
        }
        return vk != 0;
    }

    public void Dispose()
    {
        Clear();
        RemoveWindowSubclass(hwnd, proc, 1);
    }
}
