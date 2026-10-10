using System.Text.RegularExpressions;
using Hyprism.Theming;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using Windows.UI.Core;

namespace Hyprism.App;

/// <summary>Small view builders shared by the pages.</summary>
public static partial class Ui
{
    public static SolidColorBrush Brush(Rgb c, byte a = 255) => new(Windows.UI.Color.FromArgb(a, c.R, c.G, c.B));
    public static Brush Res(string key) => (Brush)Application.Current.Resources[key];
    public static Style Style(string key) => (Style)Application.Current.Resources[key];

    public static FontIcon Icon(string glyph, double size = 16) => new() { Glyph = glyph, FontSize = size };

    public static TextBlock Text(string text, string? style = null, double? size = null)
    {
        var t = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap };
        if (style is not null) t.Style = Style(style);
        if (size is { } s) t.FontSize = s;
        return t;
    }

    public static Border Card(UIElement child, Thickness? padding = null) =>
        new() { Style = Style("CardStyle"), Child = child, Padding = padding ?? new Thickness(16) };

    /// <summary>A rounded color chip.</summary>
    public static Border Swatch(Rgb c, double size = 22, double radius = 6, string? tooltip = null)
    {
        var b = new Border
        {
            Width = size, Height = size, CornerRadius = new CornerRadius(radius), Background = Brush(c),
            BorderBrush = Res("ControlStrokeColorDefaultBrush"), BorderThickness = new Thickness(1),
        };
        ToolTipService.SetToolTip(b, tooltip ?? c.Hex);
        return b;
    }

    /// <summary>Bg, fg, accent then the 16 ANSI colors as one continuous strip.</summary>
    public static Grid PaletteStrip(Palette p, double height = 10, double radius = 5)
    {
        var g = new Grid { Height = height, CornerRadius = new CornerRadius(radius) };
        var colors = new[] { p.Background, p.Surface, p.Accent }.Concat(p.Ansi.Skip(1).Take(6)).Append(p.Foreground).ToArray();
        for (int i = 0; i < colors.Length; i++)
        {
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(i == 0 ? 2 : 1, GridUnitType.Star) });
            var r = new Border { Background = Brush(colors[i]) };
            Grid.SetColumn(r, i);
            g.Children.Add(r);
        }
        return g;
    }

    /// <summary>A tiny fake terminal rendered in the palette: the quickest way to judge a scheme.</summary>
    public static Border TerminalMock(Palette p, double fontSize = 12)
    {
        var rtb = new RichTextBlock
        {
            FontFamily = (FontFamily)Application.Current.Resources["NerdFont"], FontSize = fontSize, LineHeight = fontSize * 1.55,
            TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.Clip,
            // RichTextBlock selects text on press by default, which swallowed clicks meant for the tile around it.
            IsTextSelectionEnabled = false,
        };
        Paragraph Line(params (string Text, Rgb Color)[] parts)
        {
            var para = new Paragraph();
            foreach (var (t, c) in parts) para.Inlines.Add(new Run { Text = t, Foreground = Brush(c) });
            return para;
        }
        var a = p.Ansi;
        rtb.Blocks.Add(Line(("~/hyprism ", a[4]), ("on ", p.Foreground), (" main ", a[5]), ("❯ ", a[2]), ("git status", p.Foreground)));
        rtb.Blocks.Add(Line(("  modified: ", a[1]), ("src/Theming/Palette.cs", p.Foreground)));
        rtb.Blocks.Add(Line(("  new file: ", a[2]), ("profiles/tokyo.hyprism", p.Foreground)));
        rtb.Blocks.Add(Line(("❯ ", a[2]), ("ls ", p.Foreground), ("Assets ", a[4]), ("README.md ", p.Foreground), ("build.ps1", a[3])));
        rtb.Blocks.Add(Line(("warning ", a[3]), ("2 updates ", p.Foreground), ("· ", p.Muted), ("info ", a[6]), ("ready", p.Muted)));
        // Purely decorative: let clicks fall through to whatever card/tile hosts it.
        return new Border { Background = Brush(p.Background), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 12, 14, 12), Child = rtb, IsHitTestVisible = false };
    }

    // ---------- ANSI (Oh My Posh preview) ----------

    [GeneratedRegex(@"\x1B\[([0-9;]*)m|\x1B\][^\x07\x1B]*(\x07|\x1B\\)|\x1B\[[0-9;?]*[A-Za-ln-z]")]
    private static partial Regex AnsiRegex();

    static readonly Rgb[] Xterm16 = [new(0, 0, 0), new(205, 49, 49), new(13, 188, 121), new(229, 229, 16), new(36, 114, 200), new(188, 63, 188), new(17, 168, 205), new(229, 229, 229),
        new(102, 102, 102), new(241, 76, 76), new(35, 209, 139), new(245, 245, 67), new(59, 142, 234), new(214, 112, 214), new(41, 184, 219), new(255, 255, 255)];

    static Rgb Xterm256(int n)
    {
        if (n < 16) return Xterm16[n];
        if (n >= 232) { var v = (byte)(8 + (n - 232) * 10); return new(v, v, v); }
        n -= 16;
        static byte C(int x) => (byte)(x == 0 ? 0 : 55 + x * 40);
        return new(C(n / 36), C(n / 6 % 6), C(n % 6));
    }

    /// <summary>Renders SGR-colored text (truecolor, 256 and 16 colors, bold) into a RichTextBlock with background highlights.</summary>
    public static RichTextBlock Ansi(string text, Palette p)
    {
        var rtb = new RichTextBlock { FontFamily = (FontFamily)Application.Current.Resources["NerdFont"], FontSize = 14, LineHeight = 22, IsTextSelectionEnabled = false };
        var para = new Paragraph();
        Rgb? fg = null, bg = null;
        bool bold = false;
        int pos = 0, charIndex = 0;

        void Emit(string s)
        {
            if (s.Length == 0) return;
            var run = new Run { Text = s, Foreground = Brush(fg ?? p.Foreground) };
            if (bold) run.FontWeight = FontWeights.Bold;
            para.Inlines.Add(run);
            if (bg is { } b)
            {
                var h = new TextHighlighter { Background = Brush(b) };
                h.Ranges.Add(new TextRange(charIndex, s.Length));
                rtb.TextHighlighters.Add(h);
            }
            charIndex += s.Length;
        }

        foreach (Match m in AnsiRegex().Matches(text))
        {
            Emit(text[pos..m.Index]);
            pos = m.Index + m.Length;
            if (!m.Groups[1].Success) continue; // non-color escape: drop it
            var codes = m.Groups[1].Value.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(x => int.TryParse(x, out var v) ? v : 0).ToArray();
            if (codes.Length == 0) codes = [0];
            for (int i = 0; i < codes.Length; i++)
            {
                int c = codes[i];
                if (c == 0) { fg = bg = null; bold = false; }
                else if (c == 1) bold = true;
                else if (c == 22) bold = false;
                else if (c is >= 30 and <= 37) fg = p.Ansi[c - 30];
                else if (c is >= 90 and <= 97) fg = p.Ansi[c - 90 + 8];
                else if (c is >= 40 and <= 47) bg = p.Ansi[c - 40];
                else if (c is >= 100 and <= 107) bg = p.Ansi[c - 100 + 8];
                else if (c == 39) fg = null;
                else if (c == 49) bg = null;
                else if (c is 38 or 48 && i + 1 < codes.Length)
                {
                    Rgb? col = null;
                    if (codes[i + 1] == 2 && i + 4 < codes.Length) { col = new((byte)codes[i + 2], (byte)codes[i + 3], (byte)codes[i + 4]); i += 4; }
                    else if (codes[i + 1] == 5 && i + 2 < codes.Length) { col = Xterm256(codes[i + 2]); i += 2; }
                    if (c == 38) fg = col; else bg = col;
                }
            }
        }
        Emit(text[pos..].TrimEnd());
        rtb.Blocks.Add(para);
        return rtb;
    }

    // ---------- shortcut recording ----------

    /// <summary>Turns a key press into "alt+shift+h" (GlazeWM-style names, also used for Hyprism's own hotkeys).</summary>
    public static string? ShortcutFrom(VirtualKey key)
    {
        static bool Down(VirtualKey k) => InputKeyboardSource.GetKeyStateForCurrentThread(k).HasFlag(CoreVirtualKeyStates.Down);
        string? name = key switch
        {
            >= VirtualKey.A and <= VirtualKey.Z => key.ToString().ToLowerInvariant(),
            >= VirtualKey.Number0 and <= VirtualKey.Number9 => ((int)key - (int)VirtualKey.Number0).ToString(),
            >= VirtualKey.F1 and <= VirtualKey.F24 => key.ToString().ToLowerInvariant(),
            VirtualKey.Left or VirtualKey.Right or VirtualKey.Up or VirtualKey.Down => key.ToString().ToLowerInvariant(),
            VirtualKey.Enter => "enter", VirtualKey.Space => "space", VirtualKey.Tab => "tab", VirtualKey.Escape => "escape",
            VirtualKey.Back => "backspace", VirtualKey.Delete => "delete", VirtualKey.Home => "home", VirtualKey.End => "end",
            _ => null, // modifiers alone, or keys the WMs can't bind
        };
        if (name is null) return null;
        var mods = new List<string>();
        if (Down(VirtualKey.LeftWindows) || Down(VirtualKey.RightWindows)) mods.Add("win");
        if (Down(VirtualKey.Control)) mods.Add("ctrl");
        if (Down(VirtualKey.Menu)) mods.Add("alt");
        if (Down(VirtualKey.Shift)) mods.Add("shift");
        return string.Join("+", mods.Append(name));
    }

    /// <summary>A button that records the next shortcut pressed while it has focus.</summary>
    public static Button ShortcutRecorder(string current, Action<string> changed)
    {
        var b = new Button { Content = Pretty(current), MinWidth = 150, FontFamily = (FontFamily)Application.Current.Resources["MonoFont"] };
        bool recording = false;
        b.Click += (_, _) => { recording = true; b.Content = "Press a shortcut…"; };
        b.LostFocus += (_, _) => { if (recording) { recording = false; b.Content = Pretty(current); } };
        b.PreviewKeyDown += (_, e) =>
        {
            if (!recording) return;
            e.Handled = true;
            if (e.Key == VirtualKey.Escape) { recording = false; b.Content = Pretty(current); return; }
            if (ShortcutFrom(e.Key) is not { } s) return;
            recording = false;
            current = s;
            b.Content = Pretty(s);
            changed(s);
        };
        return b;
    }

    public enum Status { Waiting, Working, Done, Warning, Failed }

    /// <summary>A small chip: colored dot + text on a tinted background. Green done, yellow working/warning, red failed.</summary>
    public sealed class StatusPill : Grid // Border is sealed in WinUI; Grid has the same background/corner/padding
    {
        readonly Microsoft.UI.Xaml.Shapes.Ellipse dot = new() { Width = 7, Height = 7, VerticalAlignment = VerticalAlignment.Center };
        readonly TextBlock label = new() { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };

        public StatusPill()
        {
            CornerRadius = new CornerRadius(10);
            Padding = new Thickness(9, 3, 10, 3);
            HorizontalAlignment = HorizontalAlignment.Right;
            VerticalAlignment = VerticalAlignment.Center;
            Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7, Children = { dot, label } });
            Set(Status.Waiting, "Waiting");
        }

        public void Set(Status status, string text)
        {
            var (fg, bg) = status switch
            {
                Status.Done => ("SystemFillColorSuccessBrush", "SystemFillColorSuccessBackgroundBrush"),
                Status.Working or Status.Warning => ("SystemFillColorCautionBrush", "SystemFillColorCautionBackgroundBrush"),
                Status.Failed => ("SystemFillColorCriticalBrush", "SystemFillColorCriticalBackgroundBrush"),
                _ => ("TextFillColorTertiaryBrush", "SubtleFillColorSecondaryBrush"),
            };
            dot.Fill = Res(fg);
            label.Foreground = status == Status.Waiting ? Res("TextFillColorSecondaryBrush") : Res(fg);
            Background = Res(bg);
            label.Text = text;
            ToolTipService.SetToolTip(this, text.Length > 40 ? text : null);
        }
    }

    public static string Pretty(string shortcut) =>
        string.Join(" + ", shortcut.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(k => k.Length == 1 ? k.ToUpperInvariant() : char.ToUpperInvariant(k[0]) + k[1..]));
}
