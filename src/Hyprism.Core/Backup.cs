using System.Text.Json;

namespace Hyprism.Core;

/// <summary>
/// Keeps the user's original files. The first time Hyprism is about to write a file, its pre-Hyprism copy
/// is saved under backups\original (never overwritten afterwards) and the current copy under backups\last
/// (overwritten on every write, used by safe mode to undo one step).
/// </summary>
public static class Backup
{
    static readonly string Root = Path.Combine(Store.Root, "backups");
    static readonly string IndexPath = Path.Combine(Root, "index.json");
    // moduleId -> original path -> backup file name, or null when the file didn't exist before Hyprism.
    static Dictionary<string, Dictionary<string, string?>> index = LoadIndex();
    static readonly object Gate = new();

    static Dictionary<string, Dictionary<string, string?>> LoadIndex()
    {
        try { return File.Exists(IndexPath) ? JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, string?>>>(File.ReadAllText(IndexPath)) ?? [] : []; }
        catch (JsonException) { return []; }
    }

    /// <summary>Call before every write to a file Hyprism doesn't own.</summary>
    public static void BeforeWrite(string moduleId, string path)
    {
        lock (Gate)
        {
            // Stable across runs (string.GetHashCode is randomized per process).
            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(path.ToLowerInvariant())))[..8];
            var name = $"{hash}_{Path.GetFileName(path)}";
            var files = index.TryGetValue(moduleId, out var f) ? f : index[moduleId] = new(StringComparer.OrdinalIgnoreCase);
            if (!files.ContainsKey(path))
            {
                var dir = Path.Combine(Root, "original", moduleId);
                Directory.CreateDirectory(dir);
                if (File.Exists(path)) { File.Copy(path, Path.Combine(dir, name), true); files[path] = name; }
                else files[path] = null;
                Directory.CreateDirectory(Root);
                File.WriteAllText(IndexPath, JsonSerializer.Serialize(index, new JsonSerializerOptions { WriteIndented = true }));
            }
            if (File.Exists(path))
            {
                var last = Path.Combine(Root, "last", moduleId);
                Directory.CreateDirectory(last);
                File.Copy(path, Path.Combine(last, name), true);
            }
        }
    }

    /// <summary>Restores every file of a module to its pre-Hyprism state (deleting files Hyprism created).</summary>
    public static void RestoreOriginal(string moduleId) => Restore(moduleId, "original");

    /// <summary>Undo the most recent write of each of a module's files.</summary>
    public static void RestoreLast(string moduleId) => Restore(moduleId, "last");

    static void Restore(string moduleId, string kind)
    {
        lock (Gate)
        {
            if (!index.TryGetValue(moduleId, out var files)) return;
            foreach (var (path, name) in files)
            {
                var src = name is null ? null : Path.Combine(Root, kind, moduleId, name);
                if (src is not null && File.Exists(src)) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.Copy(src, path, true); }
                else if (kind == "original" && name is null && File.Exists(path)) File.Delete(path);
            }
        }
    }

    public static IEnumerable<string> ModulesWithBackups() { lock (Gate) return index.Keys.ToList(); }
}
