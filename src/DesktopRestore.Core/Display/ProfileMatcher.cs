using DesktopRestore.Core.Layout;

namespace DesktopRestore.Core.Display;

public enum MatchKind
{
    /// <summary>Gleiche Monitore, Auflösungen, Positionen und Skalierung.</summary>
    Exact,
    /// <summary>Gleiche Anzahl Monitore mit gleichen Auflösungen (z. B. baugleicher Monitor an anderem Platz).</summary>
    Similar,
}

public sealed record ProfileMatch(LayoutProfile Profile, MatchKind Kind, int Score);

/// <summary>Findet das passende gespeicherte Profil zur aktuellen Monitor-Konfiguration.</summary>
public static class ProfileMatcher
{
    public static ProfileMatch? FindBest(DisplayConfiguration current, IEnumerable<LayoutProfile> profiles)
    {
        ProfileMatch? best = null;
        foreach (var profile in profiles)
        {
            if (profile.Id == current.Fingerprint)
                return new ProfileMatch(profile, MatchKind.Exact, int.MaxValue);

            var score = SimilarityScore(current.Monitors, DisplayConfiguration.SortByPosition(profile.Monitors));
            if (score == null)
                continue;
            if (best == null || score > best.Score || (score == best.Score && profile.SavedAt > best.Profile.SavedAt))
                best = new ProfileMatch(profile, MatchKind.Similar, score.Value);
        }
        return best;
    }

    /// <summary>
    /// Bewertet die Ähnlichkeit zweier nach Position sortierter Monitorlisten.
    /// null = nicht vergleichbar (andere Anzahl oder Auflösungen).
    /// </summary>
    internal static int? SimilarityScore(IReadOnlyList<MonitorInfo> current, IReadOnlyList<MonitorInfo> saved)
    {
        if (current.Count == 0 || current.Count != saved.Count)
            return null;

        int score = 0;
        for (int i = 0; i < current.Count; i++)
        {
            var a = current[i];
            var b = saved[i];
            if (a.Bounds.Width != b.Bounds.Width || a.Bounds.Height != b.Bounds.Height)
                return null;
            if (string.Equals(a.Model, b.Model, StringComparison.OrdinalIgnoreCase)) score += 2;
            if (a.Bounds.X == b.Bounds.X && a.Bounds.Y == b.Bounds.Y) score += 2;
            if (a.Dpi == b.Dpi) score += 1;
            if (a.IsPrimary == b.IsPrimary) score += 1;
        }
        return score;
    }
}
