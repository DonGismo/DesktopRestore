using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DesktopRestore.Core.Layout;

public sealed record HistoryEntry(string FilePath, DateTime SavedAt, int IconCount);

/// <summary>
/// Speichert Profile als JSON unter profiles\&lt;id&gt;.json, ältere Stände unter profiles\&lt;id&gt;.history\.
/// </summary>
public sealed class LayoutStore
{
    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private const string HistorySuffix = ".history";

    public LayoutStore(string profilesDir, int historyLimit = 10)
    {
        ProfilesDir = profilesDir;
        HistoryLimit = historyLimit;
        Directory.CreateDirectory(profilesDir);
    }

    public string ProfilesDir { get; }
    public int HistoryLimit { get; set; }

    public IReadOnlyList<LayoutProfile> LoadAll()
    {
        var result = new List<LayoutProfile>();
        foreach (var file in Directory.EnumerateFiles(ProfilesDir, "*.json"))
        {
            var profile = TryReadFile(file);
            if (profile != null)
                result.Add(profile);
        }
        return result.OrderByDescending(p => p.SavedAt).ToList();
    }

    public LayoutProfile? Load(string id) => TryReadFile(ProfilePath(id));

    /// <summary>
    /// Speichert das Profil. Mit <paramref name="archivePrevious"/> wandert der bisherige Stand in die Historie.
    /// </summary>
    public void Save(LayoutProfile profile, bool archivePrevious = true)
    {
        var path = ProfilePath(profile.Id);
        if (archivePrevious && File.Exists(path))
        {
            var previous = TryReadFile(path);
            if (previous != null)
                Archive(previous);
        }
        WriteAtomic(path, profile);
    }

    public void Rename(string id, string name)
    {
        var profile = Load(id) ?? throw new FileNotFoundException($"Profil {id} nicht gefunden.");
        profile.Name = name;
        Save(profile, archivePrevious: false);
    }

    public void Delete(string id)
    {
        var path = ProfilePath(id);
        if (File.Exists(path))
            File.Delete(path);
        var history = HistoryDir(id);
        if (Directory.Exists(history))
            Directory.Delete(history, recursive: true);
    }

    public IReadOnlyList<HistoryEntry> GetHistory(string id)
    {
        var dir = HistoryDir(id);
        if (!Directory.Exists(dir))
            return [];
        return Directory.EnumerateFiles(dir, "*.json")
            .Select(f => (File: f, Profile: TryReadFile(f)))
            .Where(x => x.Profile != null)
            .Select(x => new HistoryEntry(x.File, x.Profile!.SavedAt, x.Profile.Icons.Count))
            .OrderByDescending(h => h.SavedAt)
            .ToList();
    }

    public LayoutProfile? LoadHistory(HistoryEntry entry) => TryReadFile(entry.FilePath);

    /// <summary>Exportiert alle Profile inkl. Historie als ZIP.</summary>
    public void Export(string zipPath)
    {
        if (File.Exists(zipPath))
            File.Delete(zipPath);
        ZipFile.CreateFromDirectory(ProfilesDir, zipPath, CompressionLevel.Optimal, includeBaseDirectory: false);
    }

    /// <summary>Importiert Profile aus einem ZIP-Export. Gleichnamige Profile werden überschrieben.</summary>
    public int Import(string zipPath)
    {
        var root = Path.GetFullPath(ProfilesDir) + Path.DirectorySeparatorChar;
        int imported = 0;
        using var archive = ZipFile.OpenRead(zipPath);
        foreach (var entry in archive.Entries)
        {
            if (!entry.FullName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                continue;
            var target = Path.GetFullPath(Path.Combine(ProfilesDir, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                continue; // Schutz vor Pfaden außerhalb des Profilordners

            using (var stream = entry.Open())
            {
                if (Deserialize(stream) == null)
                    continue;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
            if (Path.GetDirectoryName(target)!.Equals(Path.GetFullPath(ProfilesDir).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                imported++;
        }
        return imported;
    }

    private void Archive(LayoutProfile previous)
    {
        var dir = HistoryDir(previous.Id);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, $"{previous.SavedAt:yyyyMMdd-HHmmss-fff}.json");
        WriteAtomic(file, previous);

        var old = Directory.EnumerateFiles(dir, "*.json").OrderByDescending(f => Path.GetFileName(f), StringComparer.Ordinal).Skip(HistoryLimit);
        foreach (var f in old)
            File.Delete(f);
    }

    private string ProfilePath(string id) => Path.Combine(ProfilesDir, SafeId(id) + ".json");
    private string HistoryDir(string id) => Path.Combine(ProfilesDir, SafeId(id) + HistorySuffix);

    private static string SafeId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains(".."))
            throw new ArgumentException($"Ungültige Profil-ID: {id}");
        return id;
    }

    private static void WriteAtomic(string path, LayoutProfile profile)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(profile, JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }

    private static LayoutProfile? TryReadFile(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            using var stream = File.OpenRead(path);
            return Deserialize(stream);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Log.Warn($"Profil {path} konnte nicht gelesen werden: {ex.Message}");
            return null;
        }
    }

    private static LayoutProfile? Deserialize(Stream stream)
    {
        try
        {
            var profile = JsonSerializer.Deserialize<LayoutProfile>(stream, JsonOptions);
            if (profile == null || string.IsNullOrEmpty(profile.Id))
                return null;
            // Vergleich der Pfade unabhängig von Groß-/Kleinschreibung
            profile.Icons = new Dictionary<string, SavedIcon>(profile.Icons ?? [], StringComparer.OrdinalIgnoreCase);
            profile.Monitors ??= [];
            return profile;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
