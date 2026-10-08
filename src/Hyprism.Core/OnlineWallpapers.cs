using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Hyprism.Core;

/// <summary>Free wallpaper sources that need no account or API key.</summary>
public enum PhotoSource { Wallhaven, Openverse, Nasa, Picsum }

/// <summary>One search result, normalized across sources.</summary>
/// <param name="Credit">Shown on the tile, e.g. the photographer or the resolution.</param>
/// <param name="Attribution">Full credit line saved next to the downloaded file (and shown as a tooltip).</param>
/// <param name="FullUrl">Direct image URL, or for NASA the asset manifest URL (resolved at download time).</param>
public sealed record OnlinePhoto(PhotoSource Source, string Id, string Credit, string Attribution,
    int Width, int Height, string PageUrl, string ThumbUrl, string FullUrl);

/// <summary>
/// Search and download wallpapers from Wallhaven, Openverse, NASA Images and Lorem Picsum (Unsplash photos).
/// All are queried anonymously; nothing personal is sent. Downloads are checked to really be images.
/// </summary>
public static class OnlineWallpapers
{
    public const int PicsumPageCount = 34; // picsum serves ~1000 photos, 30 per page

    public static string Describe(PhotoSource s) => s switch
    {
        PhotoSource.Wallhaven => "Wallhaven",
        PhotoSource.Openverse => "Openverse (Creative Commons)",
        PhotoSource.Nasa => "NASA Images",
        _ => "Unsplash (random, via Lorem Picsum)",
    };

    public static string Homepage(PhotoSource s) => s switch
    {
        PhotoSource.Wallhaven => "https://wallhaven.cc",
        PhotoSource.Openverse => "https://openverse.org",
        PhotoSource.Nasa => "https://images.nasa.gov",
        _ => "https://picsum.photos",
    };

    /// <summary>Whether the source can search (Picsum can only list/shuffle).</summary>
    public static bool CanSearch(PhotoSource s) => s != PhotoSource.Picsum;

    static readonly HttpClient Http = CreateHttp();
    static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromMinutes(2) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("Hyprism/1.2 (Windows wallpaper manager)");
        return h;
    }

    /// <summary>One page of results. Empty queries get a sensible default per source.</summary>
    public static async Task<IReadOnlyList<OnlinePhoto>> SearchAsync(PhotoSource source, string? query, int page, int screenWidth, int screenHeight, CancellationToken ct = default)
    {
        await Sys.OffUiThread();
        var q = Uri.EscapeDataString((query ?? "").Trim());
        return source switch
        {
            PhotoSource.Wallhaven => await WallhavenAsync(q, page, screenWidth, screenHeight, ct),
            PhotoSource.Openverse => await OpenverseAsync(q.Length > 0 ? q : "landscape", page, ct),
            PhotoSource.Nasa => await NasaAsync(q.Length > 0 ? q : "earth", page, ct),
            _ => await PicsumAsync(page, ct),
        };
    }

    // Wallhaven: wallpaper-only site. Anonymous access returns safe-for-work results only; we also ask for them
    // explicitly (purity=100), and only general/anime categories at least the screen's resolution.
    static async Task<IReadOnlyList<OnlinePhoto>> WallhavenAsync(string q, int page, int w, int h, CancellationToken ct)
    {
        var sorting = q.Length > 0 ? "relevance" : "toplist";
        var json = await Http.GetFromJsonAsync<JsonObject>(
            $"https://wallhaven.cc/api/v1/search?q={q}&categories=110&purity=100&atleast={w}x{h}&ratios=landscape&sorting={sorting}&page={page}", ct);
        return (json?["data"]?.AsArray() ?? []).Select(d => d!.AsObject()).Select(d =>
        {
            int dw = (int?)d["dimension_x"] ?? 0, dh = (int?)d["dimension_y"] ?? 0;
            var page_ = (string?)d["url"] ?? "";
            return new OnlinePhoto(PhotoSource.Wallhaven, (string)d["id"]!, $"{dw}×{dh}", $"Wallhaven {page_}",
                dw, dh, page_, (string?)d["thumbs"]?["large"] ?? "", (string?)d["path"] ?? "");
        }).Where(p => p.FullUrl.Length > 0).ToList();
    }

    // Openverse: Creative Commons images (Flickr, Wikimedia, museums...). Each comes with creator and license.
    static async Task<IReadOnlyList<OnlinePhoto>> OpenverseAsync(string q, int page, CancellationToken ct)
    {
        var json = await Http.GetFromJsonAsync<JsonObject>(
            // Anonymous max page size is 20. Wikimedia is excluded: its scans and maps make poor wallpapers, Openverse's
            // thumbnail proxy fails for it (HTTP 424), and upload.wikimedia.org rate-limits bursts (429).
            $"https://api.openverse.org/v1/images/?q={q}&aspect_ratio=wide&size=large&excluded_source=wikimedia&page={page}&page_size=20", ct);
        return (json?["results"]?.AsArray() ?? []).Select(r => r!.AsObject()).Select(r =>
        {
            var creator = (string?)r["creator"] ?? "Unknown";
            var license = $"CC {((string?)r["license"] ?? "").ToUpperInvariant()} {(string?)r["license_version"]}".Trim();
            var title = (string?)r["title"] ?? "Untitled";
            var landing = (string?)r["foreign_landing_url"] ?? "";
            return new OnlinePhoto(PhotoSource.Openverse, (string)r["id"]!, $"{creator} · {license}",
                $"\"{title}\" by {creator}, {license}. {landing}", (int?)r["width"] ?? 0, (int?)r["height"] ?? 0,
                landing, (string?)r["thumbnail"] ?? "", (string?)r["url"] ?? "");
        }).Where(p => p.FullUrl.StartsWith("https://") && p.ThumbUrl.Length > 0).ToList();
    }

    // NASA Image and Video Library: public-domain space and Earth imagery.
    static async Task<IReadOnlyList<OnlinePhoto>> NasaAsync(string q, int page, CancellationToken ct)
    {
        var json = await Http.GetFromJsonAsync<JsonObject>($"https://images-api.nasa.gov/search?q={q}&media_type=image&page={page}&page_size=30", ct);
        return (json?["collection"]?["items"]?.AsArray() ?? []).Select(i => i!.AsObject()).Select(i =>
        {
            var data = i["data"]?[0];
            var id = (string?)data?["nasa_id"] ?? "";
            var title = (string?)data?["title"] ?? id;
            return new OnlinePhoto(PhotoSource.Nasa, id, title, $"\"{title}\" — NASA (public domain). https://images.nasa.gov/details/{id}",
                0, 0, $"https://images.nasa.gov/details/{Uri.EscapeDataString(id)}", (string?)i["links"]?[0]?["href"] ?? "", (string?)i["href"] ?? "");
        }).Where(p => p.Id.Length > 0 && p.ThumbUrl.Length > 0 && p.FullUrl.Length > 0).ToList();
    }

    // Lorem Picsum: random Unsplash photos, no search.
    static async Task<IReadOnlyList<OnlinePhoto>> PicsumAsync(int page, CancellationToken ct)
    {
        var list = await Http.GetFromJsonAsync<JsonArray>($"https://picsum.photos/v2/list?page={page}&limit=30", ct) ?? [];
        return list.Select(p => p!.AsObject()).Select(p =>
        {
            var id = (string)p["id"]!;
            var author = (string?)p["author"] ?? "Unknown";
            var url = (string?)p["url"] ?? "";
            return new OnlinePhoto(PhotoSource.Picsum, id, author, $"Photo by {author} on Unsplash. {url}",
                (int?)p["width"] ?? 0, (int?)p["height"] ?? 0, url, $"https://picsum.photos/id/{id}/464/264", $"https://picsum.photos/id/{id}");
        }).Where(p => p.Width > p.Height && p.Id.All(char.IsDigit)).ToList();
    }

    // ---------- thumbnails ----------

    static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Task<byte[]>> Thumbs = new();

    /// <summary>Preview bytes, cached for the session (HttpClient follows the CDN redirects some sources use).</summary>
    public static Task<byte[]> ThumbAsync(OnlinePhoto photo)
    {
        if (Thumbs.Count > 400) Thumbs.Clear();
        var key = photo.Source + ":" + photo.Id;
        var task = Thumbs.GetOrAdd(key, _ => Task.Run(() => LoadThumbAsync(photo)));
        if (task.IsFaulted) Thumbs.TryRemove(key, out _); // retry next time instead of caching a failure
        return task;
    }

    // Image hosts rate-limit bursts (Wikimedia answers 429): a page of previews loads 4 at a time.
    static readonly SemaphoreSlim ThumbGate = new(4);

    static async Task<byte[]> LoadThumbAsync(OnlinePhoto photo)
    {
        await ThumbGate.WaitAsync();
        try
        {
            try { return await GetWithRetryAsync(photo.ThumbUrl); }
            catch (HttpRequestException) when (photo.Source == PhotoSource.Openverse)
            {
                // Openverse renders thumbnails on the fly and occasionally fails one: use the original instead
                // (it is decoded at thumbnail size, so only the download is bigger).
                return await GetWithRetryAsync(photo.FullUrl);
            }
        }
        finally { ThumbGate.Release(); }
    }

    static async Task<byte[]> GetWithRetryAsync(string url)
    {
        for (int attempt = 0; ; attempt++)
        {
            using var resp = await Http.GetAsync(url);
            if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests && attempt < 2)
            {
                await Task.Delay(resp.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(2 * (attempt + 1)));
                continue;
            }
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsByteArrayAsync();
        }
    }

    // ---------- download ----------

    const long MaxBytes = 60 * 1024 * 1024;

    /// <summary>Downloads the full image into %AppData%\Hyprism\wallpapers\&lt;source&gt; and returns its path.</summary>
    public static async Task<string> DownloadAsync(OnlinePhoto photo, int screenWidth, int screenHeight, CancellationToken ct = default)
    {
        await Sys.OffUiThread();
        var url = photo.Source switch
        {
            // Picsum crops server-side to the screen's aspect ratio (never upscaling past the original).
            PhotoSource.Picsum => PicsumUrl(photo, screenWidth, screenHeight),
            PhotoSource.Nasa => await NasaAssetAsync(photo.FullUrl, ct),
            _ => photo.FullUrl,
        };
        if (!url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Refusing a non-HTTPS download.");

        var dir = Path.Combine(Store.WallpapersDir, photo.Source.ToString());
        Directory.CreateDirectory(dir);
        var safeId = Regex.Replace(photo.Id, @"[^\w\-]", "_");
        var existing = Directory.EnumerateFiles(dir, safeId + ".*").FirstOrDefault(f => !f.EndsWith(".txt"));
        if (existing is not null) return existing;

        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();
        if (resp.Content.Headers.ContentLength > MaxBytes) throw new InvalidDataException("That image is too large (over 60 MB).");
        await using var src = await resp.Content.ReadAsStreamAsync(ct);
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int n;
        while ((n = await src.ReadAsync(buffer, ct)) > 0)
        {
            ms.Write(buffer, 0, n);
            if (ms.Length > MaxBytes) throw new InvalidDataException("That image is too large (over 60 MB).");
        }
        var bytes = ms.ToArray();
        var ext = ImageExtension(bytes) ?? throw new InvalidDataException("The download wasn't a JPEG, PNG or WebP image.");
        Antivirus.EnsureClean(bytes, $"{photo.Source}-{photo.Id}{ext}"); // antivirus before it's saved

        var file = Path.Combine(dir, safeId + ext);
        await File.WriteAllBytesAsync(file + ".part", bytes, ct);
        File.Move(file + ".part", file, overwrite: true);
        await File.WriteAllTextAsync(Path.Combine(dir, safeId + ".txt"), photo.Attribution + "\n", ct); // credit travels with the file
        return file;
    }

    static string PicsumUrl(OnlinePhoto p, int sw, int sh)
    {
        double scale = Math.Min(1.0, Math.Min((double)p.Width / sw, (double)p.Height / sh));
        return $"https://picsum.photos/id/{p.Id}/{Math.Max(1, (int)(sw * scale))}/{Math.Max(1, (int)(sh * scale))}";
    }

    /// <summary>NASA search results point at a JSON list of renditions; take the original, else the largest JPEG.</summary>
    static async Task<string> NasaAssetAsync(string manifestUrl, CancellationToken ct)
    {
        var urls = (await Http.GetFromJsonAsync<JsonArray>(manifestUrl, ct) ?? []).Select(u => ((string?)u ?? "").Replace("http://", "https://")).ToList();
        foreach (var suffix in new[] { "~orig.jpg", "~large.jpg", "~medium.jpg" })
            if (urls.FirstOrDefault(u => u.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) is { } hit) return hit;
        return urls.FirstOrDefault(u => u.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || u.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("NASA has no still image for this item.");
    }

    /// <summary>File extension from the magic bytes; null if it isn't an image Windows can use as a wallpaper.</summary>
    public static string? ImageExtension(byte[] b) =>
        b.Length >= 12 && b[0] == 0xFF && b[1] == 0xD8 ? ".jpg" :
        b.Length >= 12 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 ? ".png" :
        b.Length >= 12 && b[0] == (byte)'R' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'F' && b[8] == (byte)'W' && b[9] == (byte)'E' && b[10] == (byte)'B' && b[11] == (byte)'P' ? ".webp" :
        null;
}
