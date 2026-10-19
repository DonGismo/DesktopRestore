using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace DesktopRestore.Core.Display;

/// <summary>Ein aktiver Monitor. <see cref="Model"/> ist die Hardware-ID aus der EDID (z. B. "GSM5B7F").</summary>
public sealed record MonitorInfo
{
    public string DeviceName { get; init; } = "";
    public string Model { get; init; } = "";
    public Rect Bounds { get; init; }
    public Rect WorkArea { get; init; }
    public int Dpi { get; init; } = 96;
    public bool IsPrimary { get; init; }

    [JsonIgnore] public int ScalePercent => (int)Math.Round(Dpi * 100.0 / 96);

    internal string FingerprintPart =>
        $"{Model}@{Bounds.X},{Bounds.Y}:{Bounds.Width}x{Bounds.Height}/{Dpi}{(IsPrimary ? "*" : "")}";
}

/// <summary>Die aktuelle Monitor-Konfiguration, also der "Arbeitsplatz".</summary>
public sealed class DisplayConfiguration
{
    public DisplayConfiguration(IEnumerable<MonitorInfo> monitors)
    {
        Monitors = SortByPosition(monitors);
        Fingerprint = ComputeFingerprint(Monitors);
    }

    /// <summary>Monitore sortiert nach Position (links nach rechts, oben nach unten).</summary>
    public IReadOnlyList<MonitorInfo> Monitors { get; }

    public string Fingerprint { get; }

    public MonitorInfo? Primary => Monitors.FirstOrDefault(m => m.IsPrimary) ?? Monitors.FirstOrDefault();

    public static List<MonitorInfo> SortByPosition(IEnumerable<MonitorInfo> monitors) =>
        monitors.OrderBy(m => m.Bounds.X).ThenBy(m => m.Bounds.Y).ToList();

    public static string ComputeFingerprint(IEnumerable<MonitorInfo> monitors)
    {
        var text = string.Join(";", SortByPosition(monitors).Select(m => m.FingerprintPart));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        return Convert.ToHexString(hash, 0, 8).ToLowerInvariant();
    }

    /// <summary>Lesbarer Standardname, z. B. "2 Monitore (2560×1440, 1920×1080)".</summary>
    public string DefaultName => DescribeMonitors(Monitors);

    public static string DescribeMonitors(IReadOnlyCollection<MonitorInfo> monitors)
    {
        var sizes = string.Join(", ", monitors.Select(m => $"{m.Bounds.Width}×{m.Bounds.Height}"));
        return monitors.Count == 1 ? $"1 Monitor ({sizes})" : $"{monitors.Count} Monitore ({sizes})";
    }

    public static string DescribeDetailed(IEnumerable<MonitorInfo> monitors) =>
        string.Join("; ", monitors.Select(m =>
            $"{m.Model} {m.Bounds.Width}×{m.Bounds.Height} @({m.Bounds.X},{m.Bounds.Y}) {m.ScalePercent}%{(m.IsPrimary ? " primär" : "")}"));
}
