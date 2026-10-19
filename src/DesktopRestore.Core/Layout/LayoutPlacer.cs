using DesktopRestore.Core.Display;
using DesktopRestore.Core.Shell;
using DesktopRestore.Core.Settings;

namespace DesktopRestore.Core.Layout;

public sealed record PlacementResult(
    Dictionary<string, IconPosition> Targets,
    int Restored,
    int Adjusted,
    int NewIcons);

/// <summary>
/// Berechnet die Zielpositionen: gespeicherte Icons werden auf die aktuellen Monitore abgebildet und
/// in den sichtbaren Bereich geholt, neue Icons kommen in die konfigurierte Einsortier-Zone.
/// </summary>
public static class LayoutPlacer
{
    public static PlacementResult Compute(
        LayoutProfile profile,
        DisplayConfiguration current,
        IReadOnlyList<DesktopIcon> icons,
        GridSpacing spacing,
        NewIconZone zone)
    {
        var savedMonitors = DisplayConfiguration.SortByPosition(profile.Monitors);
        var targets = new Dictionary<string, IconPosition>(StringComparer.OrdinalIgnoreCase);
        var newIcons = new List<DesktopIcon>();
        int adjusted = 0;

        foreach (var icon in icons)
        {
            if (!profile.Icons.TryGetValue(icon.Key, out var saved))
            {
                newIcons.Add(icon);
                continue;
            }
            var original = new IconPosition(saved.X, saved.Y);
            var mapped = MapPoint(original, savedMonitors, current);
            var clamped = Clamp(mapped, current.Monitors, spacing, strict: mapped != original);
            if (clamped != original)
                adjusted++;
            targets[icon.Key] = clamped;
        }

        int restored = targets.Count;
        int placedNew = 0;
        if (zone != NewIconZone.Keep && current.Primary is { } primary)
        {
            var occupied = targets.Values.ToList();
            var offset = GridOffset(occupied.Count > 0 ? occupied : icons.Select(i => new IconPosition(i.X, i.Y)), primary.WorkArea, spacing);
            foreach (var icon in newIcons.OrderBy(i => i.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            {
                var cell = FindFreeCell(primary.WorkArea, spacing, zone, occupied, offset);
                if (cell == null)
                    break;
                targets[icon.Key] = cell.Value;
                occupied.Add(cell.Value);
                placedNew++;
            }
        }

        return new PlacementResult(targets, restored, adjusted, placedNew);
    }

    /// <summary>
    /// Bildet einen Punkt vom gespeicherten Monitor auf den Monitor gleichen Index (nach Position sortiert) ab,
    /// relativ und proportional. Bei identischer Konfiguration bleibt der Punkt unverändert.
    /// </summary>
    internal static IconPosition MapPoint(IconPosition p, IReadOnlyList<MonitorInfo> saved, DisplayConfiguration current)
    {
        if (saved.Count == 0 || current.Monitors.Count == 0)
            return p;

        int index = IndexOfMonitor(saved, p);
        var src = saved[index].Bounds;
        var dst = index < current.Monitors.Count
            ? current.Monitors[index].Bounds
            : (current.Primary ?? current.Monitors[0]).Bounds;

        if (src == dst)
            return p;

        long x = dst.X + (long)(p.X - src.X) * dst.Width / Math.Max(1, src.Width);
        long y = dst.Y + (long)(p.Y - src.Y) * dst.Height / Math.Max(1, src.Height);
        return new IconPosition((int)x, (int)y);
    }

    /// <summary>
    /// Holt den Punkt in den Arbeitsbereich des nächstgelegenen Monitors (Icon komplett sichtbar).
    /// Ohne <paramref name="strict"/> bleiben Punkte im Arbeitsbereich unverändert – sie stammen von Explorer selbst.
    /// </summary>
    internal static IconPosition Clamp(IconPosition p, IReadOnlyList<MonitorInfo> monitors, GridSpacing spacing, bool strict = true)
    {
        if (monitors.Count == 0)
            return p;
        var monitor = monitors[IndexOfMonitor(monitors, p)];
        var wa = monitor.WorkArea;
        if (!strict && wa.Contains(p.X, p.Y))
            return p;
        int x = Math.Clamp(p.X, wa.X, Math.Max(wa.X, wa.Right - spacing.Width));
        int y = Math.Clamp(p.Y, wa.Y, Math.Max(wa.Y, wa.Bottom - spacing.Height));
        return new IconPosition(x, y);
    }

    private static int IndexOfMonitor(IReadOnlyList<MonitorInfo> monitors, IconPosition p)
    {
        int best = 0;
        long bestDistance = long.MaxValue;
        for (int i = 0; i < monitors.Count; i++)
        {
            long d = monitors[i].Bounds.DistanceSquared(p.X, p.Y);
            if (d < bestDistance)
            {
                best = i;
                bestDistance = d;
            }
        }
        return best;
    }

    /// <summary>
    /// Versatz der Icon-Position innerhalb einer Rasterzelle (Explorer zentriert das Symbol in der Zelle),
    /// ermittelt als häufigster Rest der vorhandenen Positionen.
    /// </summary>
    internal static IconPosition GridOffset(IEnumerable<IconPosition> positions, Rect workArea, GridSpacing spacing)
    {
        static int Mod(int a, int m) => ((a % m) + m) % m;
        var inside = positions.Where(p => workArea.Contains(p.X, p.Y)).ToList();
        if (inside.Count == 0)
            return new IconPosition(0, 0);
        int x = inside.GroupBy(p => Mod(p.X - workArea.X, spacing.Width)).OrderByDescending(g => g.Count()).First().Key;
        int y = inside.GroupBy(p => Mod(p.Y - workArea.Y, spacing.Height)).OrderByDescending(g => g.Count()).First().Key;
        return new IconPosition(x, y);
    }

    /// <summary>Sucht spaltenweise von oben nach unten die erste freie Rasterzelle.</summary>
    internal static IconPosition? FindFreeCell(Rect workArea, GridSpacing spacing, NewIconZone zone, IReadOnlyList<IconPosition> occupied, IconPosition offset = default)
    {
        int columns = Math.Max(1, workArea.Width / spacing.Width);
        int rows = Math.Max(1, workArea.Height / spacing.Height);
        for (int c = 0; c < columns; c++)
        {
            int column = zone == NewIconZone.LeftColumn ? c : columns - 1 - c;
            int x = workArea.X + column * spacing.Width + offset.X;
            for (int r = 0; r < rows; r++)
            {
                int y = workArea.Y + r * spacing.Height + offset.Y;
                bool taken = occupied.Any(o =>
                    Math.Abs(o.X - x) < spacing.Width / 2 + 1 && Math.Abs(o.Y - y) < spacing.Height / 2 + 1);
                if (!taken)
                    return new IconPosition(x, y);
            }
        }
        return null;
    }
}
