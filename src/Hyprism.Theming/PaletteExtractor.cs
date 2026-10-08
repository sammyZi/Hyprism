using Windows.Graphics.Imaging;
using Windows.Storage;

namespace Hyprism.Theming;

/// <summary>pywal/matugen-style palette from an image: k-means on a thumbnail, then derive a readable scheme.</summary>
public static class PaletteExtractor
{
    public static async Task<Palette> FromImageAsync(string path, bool dark = true)
    {
        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(path));
        using var stream = await file.OpenReadAsync();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        var scale = 96.0 / Math.Max(decoder.PixelWidth, decoder.PixelHeight);
        var transform = new BitmapTransform
        {
            ScaledWidth = (uint)Math.Max(1, decoder.PixelWidth * scale),
            ScaledHeight = (uint)Math.Max(1, decoder.PixelHeight * scale),
            InterpolationMode = BitmapInterpolationMode.Fant,
        };
        var data = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, transform,
            ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
        var bytes = data.DetachPixelData();
        var px = new Rgb[bytes.Length / 4];
        for (int i = 0; i < px.Length; i++) px[i] = new(bytes[i * 4 + 2], bytes[i * 4 + 1], bytes[i * 4]);
        return FromPixels(px, dark ? "From wallpaper" : "From wallpaper (light)", dark);
    }

    public static Palette FromPixels(IReadOnlyList<Rgb> px, string name = "Wallpaper", bool dark = true)
    {
        var clusters = KMeans(px, 8);

        // Accent: the most vivid color that still has real presence in the image.
        var accent = clusters
            .Select(c => (c.Color, Score: c.Color.ToHsl().S * Math.Pow(c.Weight, 0.3) * LightnessFit(c.Color.ToHsl().L)))
            .MaxBy(x => x.Score).Color;
        var (ah, asat, _) = accent.ToHsl();
        asat = Math.Clamp(asat, 0.35, 0.85);
        accent = Rgb.FromHsl(ah, asat, dark ? 0.68 : 0.45);

        var darkest = clusters.MinBy(c => c.Color.Luminance).Color.ToHsl();
        var bg = dark ? Rgb.FromHsl(darkest.H, Math.Min(darkest.S, 0.35), 0.09) : Rgb.FromHsl(ah, 0.25, 0.95);
        var fg = dark ? Rgb.FromHsl(ah, 0.25, 0.88) : Rgb.FromHsl(ah, 0.2, 0.16);

        // ANSI hues, nudged toward wallpaper hues when one is nearby (keeps red red, but "wallpaper-red").
        double[] baseHues = [0, 125, 48, 220, 300, 185];
        var vivid = clusters.Where(c => c.Color.ToHsl().S > 0.2).Select(c => c.Color.ToHsl().H).ToList();
        var ansi = new Rgb[16];
        double l = dark ? 0.68 : 0.40;
        for (int i = 0; i < 6; i++)
        {
            var h = baseHues[i];
            var near = vivid.Select(v => (v, d: HueDist(v, h))).Where(x => x.d < 30).OrderBy(x => x.d).Select(x => (double?)x.v).FirstOrDefault();
            if (near is { } n) h = h + (Signed(n - h) * 0.6);
            ansi[i + 1] = EnsureContrast(Rgb.FromHsl(h, Math.Clamp(asat, 0.45, 0.75), l), bg, 3.0);
            ansi[i + 9] = EnsureContrast(Rgb.FromHsl(h, Math.Clamp(asat + 0.1, 0.5, 0.85), dark ? l + 0.08 : l - 0.06), bg, 3.0);
        }
        ansi[0] = bg.Mix(fg, 0.12);
        ansi[7] = fg.Mix(bg, 0.15);
        ansi[8] = EnsureContrast(bg.Mix(fg, 0.4), bg, 2.2);
        ansi[15] = fg;
        return new Palette(name, bg, fg, EnsureContrast(accent, bg, 3.0), ansi, dark);
    }

    static double LightnessFit(double l) => l is > 0.2 and < 0.85 ? 1 : 0.3;
    static double HueDist(double a, double b) => Math.Abs(Signed(a - b));
    static double Signed(double d) => ((d + 540) % 360) - 180;

    /// <summary>Move lightness away from the background until the contrast ratio is met.</summary>
    public static Rgb EnsureContrast(Rgb c, Rgb bg, double ratio)
    {
        var (h, s, l) = c.ToHsl();
        double step = bg.Luminance < 0.5 ? 0.02 : -0.02;
        for (int i = 0; i < 50 && Rgb.Contrast(c, bg) < ratio; i++) c = Rgb.FromHsl(h, s, l += step);
        return c;
    }

    record struct Cluster(Rgb Color, double Weight);

    static List<Cluster> KMeans(IReadOnlyList<Rgb> px, int k)
    {
        if (px.Count == 0) return [new(new(30, 30, 46), 1)];
        // Deterministic seeds: spread across the luminance-sorted pixels.
        var sorted = px.OrderBy(p => p.Luminance).ToArray();
        var centers = Enumerable.Range(0, k).Select(i => ToVec(sorted[(int)((i + 0.5) / k * (sorted.Length - 1))])).ToArray();
        var assign = new int[px.Count];
        for (int iter = 0; iter < 12; iter++)
        {
            var sum = new (double r, double g, double b, int n)[k];
            for (int i = 0; i < px.Count; i++)
            {
                var v = ToVec(px[i]);
                int best = 0; double bd = double.MaxValue;
                for (int c = 0; c < k; c++) { var d = Dist(v, centers[c]); if (d < bd) { bd = d; best = c; } }
                assign[i] = best;
                sum[best] = (sum[best].r + v.r, sum[best].g + v.g, sum[best].b + v.b, sum[best].n + 1);
            }
            for (int c = 0; c < k; c++) if (sum[c].n > 0) centers[c] = (sum[c].r / sum[c].n, sum[c].g / sum[c].n, sum[c].b / sum[c].n);
        }
        var counts = new int[k];
        foreach (var a in assign) counts[a]++;
        return Enumerable.Range(0, k).Where(c => counts[c] > 0)
            .Select(c => new Cluster(new((byte)centers[c].r, (byte)centers[c].g, (byte)centers[c].b), (double)counts[c] / px.Count)).ToList();
    }

    static (double r, double g, double b) ToVec(Rgb c) => (c.R, c.G, c.B);
    static double Dist((double r, double g, double b) a, (double r, double g, double b) b) =>
        // "redmean" weighted distance: cheap and noticeably better than plain RGB.
        ((a.r + b.r) / 2 < 128 ? 2 : 3) * (a.r - b.r) * (a.r - b.r) + 4 * (a.g - b.g) * (a.g - b.g) + ((a.r + b.r) / 2 < 128 ? 3 : 2) * (a.b - b.b) * (a.b - b.b);
}
