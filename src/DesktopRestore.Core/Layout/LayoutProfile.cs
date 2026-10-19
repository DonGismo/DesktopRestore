using DesktopRestore.Core.Display;

namespace DesktopRestore.Core.Layout;

public sealed record SavedIcon(int X, int Y, string? DisplayName);

/// <summary>Gespeicherte Icon-Anordnung für eine Monitor-Konfiguration.</summary>
public sealed class LayoutProfile
{
    /// <summary>Fingerprint der Monitor-Konfiguration (zugleich Dateiname).</summary>
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public List<MonitorInfo> Monitors { get; set; } = [];
    public Dictionary<string, SavedIcon> Icons { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime SavedAt { get; set; }

    public LayoutProfile Clone() => new()
    {
        Id = Id,
        Name = Name,
        Monitors = [.. Monitors],
        Icons = new Dictionary<string, SavedIcon>(Icons, StringComparer.OrdinalIgnoreCase),
        SavedAt = SavedAt,
    };

    /// <summary>Vergleicht zwei Anordnungen mit Toleranz in Pixeln.</summary>
    public static bool IconsEqual(IReadOnlyDictionary<string, SavedIcon> a, IReadOnlyDictionary<string, SavedIcon> b, int tolerance = 2)
    {
        if (a.Count != b.Count)
            return false;
        foreach (var (key, icon) in a)
        {
            if (!b.TryGetValue(key, out var other))
                return false;
            if (Math.Abs(icon.X - other.X) > tolerance || Math.Abs(icon.Y - other.Y) > tolerance)
                return false;
        }
        return true;
    }
}
