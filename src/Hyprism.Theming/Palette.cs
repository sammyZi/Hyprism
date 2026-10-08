using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hyprism.Theming;

[JsonConverter(typeof(RgbJsonConverter))]
public readonly record struct Rgb(byte R, byte G, byte B)
{
    public static Rgb Parse(string hex)
    {
        var s = hex.TrimStart('#');
        if (s.Length == 8) s = s[2..]; // #AARRGGBB -> drop alpha
        if (s.Length != 6) throw new FormatException($"Bad color '{hex}'");
        var v = uint.Parse(s, NumberStyles.HexNumber);
        return new((byte)(v >> 16), (byte)(v >> 8), (byte)v);
    }

    public string Hex => $"#{R:X2}{G:X2}{B:X2}";
    public string HexA(byte a) => $"#{a:X2}{R:X2}{G:X2}{B:X2}";      // WPF/WinUI order
    public string HexRgba(byte a) => $"#{R:X2}{G:X2}{B:X2}{a:X2}";   // CSS / TranslucentTB order
    public int ColorRef => R | G << 8 | B << 16;                       // Win32 COLORREF

    /// <summary>WCAG relative luminance.</summary>
    public double Luminance
    {
        get
        {
            static double Lin(byte c) { var x = c / 255.0; return x <= 0.03928 ? x / 12.92 : Math.Pow((x + 0.055) / 1.055, 2.4); }
            return 0.2126 * Lin(R) + 0.7152 * Lin(G) + 0.0722 * Lin(B);
        }
    }

    public static double Contrast(Rgb a, Rgb b)
    {
        double l1 = a.Luminance, l2 = b.Luminance;
        return (Math.Max(l1, l2) + 0.05) / (Math.Min(l1, l2) + 0.05);
    }

    public Rgb Mix(Rgb o, double t) => new(
        (byte)Math.Round(R + (o.R - R) * t), (byte)Math.Round(G + (o.G - G) * t), (byte)Math.Round(B + (o.B - B) * t));

    public (double H, double S, double L) ToHsl()
    {
        double r = R / 255.0, g = G / 255.0, b = B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), l = (max + min) / 2, d = max - min;
        if (d < 1e-9) return (0, 0, l);
        double s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h * 60, s, l);
    }

    public static Rgb FromHsl(double h, double s, double l)
    {
        h = ((h % 360) + 360) % 360; s = Math.Clamp(s, 0, 1); l = Math.Clamp(l, 0, 1);
        double c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = l - c / 2;
        var (r, g, b) = (int)(h / 60) switch
        {
            0 => (c, x, 0.0), 1 => (x, c, 0.0), 2 => (0.0, c, x),
            3 => (0.0, x, c), 4 => (x, 0.0, c), _ => (c, 0.0, x),
        };
        return new((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }

    public Rgb WithLightness(double l) { var (h, s, _) = ToHsl(); return FromHsl(h, s, l); }
    public override string ToString() => Hex;
}

public sealed class RgbJsonConverter : JsonConverter<Rgb>
{
    public override Rgb Read(ref Utf8JsonReader r, Type t, JsonSerializerOptions o) => Rgb.Parse(r.GetString()!);
    public override void Write(Utf8JsonWriter w, Rgb v, JsonSerializerOptions o) => w.WriteStringValue(v.Hex);
}

/// <summary>
/// A full color scheme. Ansi holds the 16 terminal colors in the usual order:
/// black, red, green, yellow, blue, magenta, cyan, white, then the bright variants.
/// </summary>
public sealed record Palette(string Name, Rgb Background, Rgb Foreground, Rgb Accent, Rgb[] Ansi, bool IsDark = true)
{
    [JsonIgnore] public Rgb Surface => Background.Mix(Foreground, 0.08);
    [JsonIgnore] public Rgb Overlay => Background.Mix(Foreground, 0.16);
    [JsonIgnore] public Rgb Muted => Background.Mix(Foreground, 0.5);
    [JsonIgnore] public Rgb OnAccent => Rgb.Contrast(Accent, new(0, 0, 0)) > Rgb.Contrast(Accent, new(255, 255, 255)) ? new(17, 17, 17) : new(255, 255, 255);

    public static Palette FromHex(string name, string bg, string fg, string accent, string ansi, bool dark = true) =>
        new(name, Rgb.Parse(bg), Rgb.Parse(fg), Rgb.Parse(accent),
            ansi.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(Rgb.Parse).ToArray(), dark);
}
