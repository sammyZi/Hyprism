using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Hyprism.Core;

/// <summary>Read-modify-write helpers for the config formats the integrated apps use.</summary>
public static class Files
{
    static readonly JsonDocumentOptions JsoncOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    /// <summary>Parses JSON with comments (Windows Terminal's settings.json is JSONC).</summary>
    public static JsonObject ReadJson(string path) =>
        File.Exists(path) && JsonNode.Parse(File.ReadAllText(path), documentOptions: JsoncOptions) is JsonObject o ? o : [];

    public static void WriteJson(string path, JsonNode node) => WriteText(path, node.ToJsonString(Indented));

    /// <summary>Writes via a temp file so a reader never sees a half-written config.</summary>
    public static void WriteText(string path, string text, Encoding? encoding = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".hyprism-tmp";
        File.WriteAllText(tmp, text, encoding ?? new UTF8Encoding(false));
        File.Move(tmp, path, overwrite: true);
    }

    /// <summary>Sets <c>key=value</c> inside <c>[section]</c>, adding either if missing. Keeps the file's encoding and other lines.</summary>
    public static void SetIni(string path, string section, string key, string value)
    {
        var encoding = DetectEncoding(path);
        var lines = File.Exists(path) ? File.ReadAllLines(path, encoding).ToList() : [];
        SetIniLines(lines, section, key, value);
        WriteText(path, string.Join("\r\n", lines) + "\r\n", encoding);
    }

    public static void SetIniLines(List<string> lines, string section, string key, string value)
    {
        int start = lines.FindIndex(l => l.Trim().Equals($"[{section}]", StringComparison.OrdinalIgnoreCase));
        if (start < 0) { lines.Add($"[{section}]"); lines.Add($"{key}={value}"); return; }
        int end = lines.FindIndex(start + 1, l => l.TrimStart().StartsWith('['));
        if (end < 0) end = lines.Count;
        for (int i = start + 1; i < end; i++)
        {
            var eq = lines[i].IndexOf('=');
            if (eq > 0 && lines[i][..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) { lines[i] = $"{key}={value}"; return; }
        }
        lines.Insert(end, $"{key}={value}");
    }

    public static string? GetIni(string path, string section, string key)
    {
        if (!File.Exists(path)) return null;
        bool inSection = false;
        foreach (var raw in File.ReadLines(path, DetectEncoding(path)))
        {
            var l = raw.Trim();
            if (l.StartsWith('[')) { inSection = l.Equals($"[{section}]", StringComparison.OrdinalIgnoreCase); continue; }
            var eq = l.IndexOf('=');
            if (inSection && eq > 0 && l[..eq].Trim().Equals(key, StringComparison.OrdinalIgnoreCase)) return l[(eq + 1)..].Trim();
        }
        return null;
    }

    static Encoding DetectEncoding(string path)
    {
        if (!File.Exists(path)) return new UTF8Encoding(false);
        Span<byte> bom = stackalloc byte[3];
        using var f = File.OpenRead(path);
        int n = f.Read(bom);
        if (n >= 2 && bom[0] == 0xFF && bom[1] == 0xFE) return Encoding.Unicode;
        if (n >= 3 && bom[0] == 0xEF && bom[1] == 0xBB && bom[2] == 0xBF) return new UTF8Encoding(true);
        return new UTF8Encoding(false);
    }

    /// <summary>
    /// Replaces (or appends) the text between <c># >>> hyprism >>></c> and <c># &lt;&lt;&lt; hyprism &lt;&lt;&lt;</c> markers,
    /// so user content around it is never touched. Pass null to remove the block.
    /// </summary>
    public static void SetManagedBlock(string path, string? body, string comment = "#")
    {
        string open = $"{comment} >>> hyprism >>>", close = $"{comment} <<< hyprism <<<";
        var text = File.Exists(path) ? File.ReadAllText(path) : "";
        int a = text.IndexOf(open, StringComparison.Ordinal), b = text.IndexOf(close, StringComparison.Ordinal);
        if (a >= 0 && b > a) text = text[..a].TrimEnd() + text[(b + close.Length)..];
        text = text.TrimEnd();
        if (body is not null) text += $"\r\n\r\n{open}\r\n{body.Trim()}\r\n{close}\r\n";
        WriteText(path, text.TrimStart() + (body is null ? "\r\n" : ""), new UTF8Encoding(true));
    }
}

/// <summary>Typed reads from a module's settings object, falling back to the module's declared default.</summary>
public static class SettingsExtensions
{
    public static JsonNode? Raw(this JsonObject s, IModule m, string key) =>
        s[key] ?? m.Settings.FirstOrDefault(d => d.Key == key)?.Default;

    public static bool Bool(this JsonObject s, IModule m, string key) => s.Raw(m, key)?.GetValue<bool>() ?? false;
    public static double Num(this JsonObject s, IModule m, string key) =>
        s.Raw(m, key) is JsonValue v ? (v.TryGetValue<double>(out var d) ? d : double.Parse(v.ToJsonString().Trim('"'), System.Globalization.CultureInfo.InvariantCulture)) : 0;
    public static string Str(this JsonObject s, IModule m, string key) => s.Raw(m, key)?.ToString() ?? "";

    /// <summary>Non-empty, trimmed lines of a MultiLine setting.</summary>
    public static IEnumerable<string> Lines(this JsonObject s, IModule m, string key) =>
        s.Str(m, key).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#'));

    /// <summary>Settings with every declared default filled in.</summary>
    public static JsonObject WithDefaults(this IModule m, JsonObject s)
    {
        var o = (JsonObject)s.DeepClone();
        foreach (var d in m.Settings)
            if (d.Kind != SettingKind.Action && !o.ContainsKey(d.Key) && d.Default is not null) o[d.Key] = d.Default.DeepClone();
        return o;
    }
}
