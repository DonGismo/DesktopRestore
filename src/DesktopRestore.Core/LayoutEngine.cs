using DesktopRestore.Core.Display;
using DesktopRestore.Core.Layout;
using DesktopRestore.Core.Settings;
using DesktopRestore.Core.Shell;

namespace DesktopRestore.Core;

public sealed record RestoreResult(DisplayConfiguration Config, ProfileMatch? Match, PlacementResult? Placement, int Moved, string Message);

public enum SaveOutcome
{
    Saved,
    Unchanged,
    NoIcons,
}

public sealed record SaveResult(SaveOutcome Outcome, LayoutProfile? Profile);

/// <summary>Speichern und Wiederherstellen ohne UI – genutzt von Tray-App und Kommandozeile.</summary>
public sealed class LayoutEngine
{
    public LayoutEngine(LayoutStore store, AppSettings settings, DesktopIconService? icons = null)
    {
        Store = store;
        Settings = settings;
        Icons = icons ?? new DesktopIconService();
    }

    public LayoutStore Store { get; }
    public AppSettings Settings { get; set; }
    public DesktopIconService Icons { get; }

    /// <summary>Sucht das passende Profil zur aktuellen Monitor-Konfiguration und stellt es wieder her.</summary>
    public RestoreResult RestoreCurrent()
    {
        var config = DisplayDetector.GetCurrent();
        var match = ProfileMatcher.FindBest(config, Store.LoadAll());
        if (match == null)
            return new RestoreResult(config, null, null, 0, $"Kein gespeichertes Layout für {config.DefaultName}.");
        return ApplyProfile(match.Profile, config, match);
    }

    public RestoreResult ApplyProfile(LayoutProfile profile, DisplayConfiguration config, ProfileMatch? match = null)
    {
        var snapshot = Icons.Read();
        var placement = LayoutPlacer.Compute(profile, config, snapshot.Icons, snapshot.Spacing, Settings.NewIconZone);
        int moved = Icons.ApplyPositions(placement.Targets);

        var kind = match?.Kind == MatchKind.Similar ? " (ähnlicher Arbeitsplatz)" : "";
        var message = $"„{profile.Name}“{kind}: {placement.Restored} Icons wiederhergestellt";
        if (placement.NewIcons > 0)
            message += $", {placement.NewIcons} neue einsortiert";
        if (placement.Adjusted > 0)
            message += $", {placement.Adjusted} angepasst";
        Log.Info($"Wiederhergestellt {message}; gesetzt: {moved}; Monitore: {DisplayConfiguration.DescribeDetailed(config.Monitors)}");
        return new RestoreResult(config, match, placement, moved, message + ".");
    }

    /// <summary>Zählt Icons, die nach dem Wiederherstellen (noch) nicht an ihrer Zielposition liegen.</summary>
    public int CountDeviations(IReadOnlyDictionary<string, IconPosition> targets)
    {
        var snapshot = Icons.TryRead(out _);
        if (snapshot == null)
            return 0;
        int tolX = snapshot.Spacing.Width / 2, tolY = snapshot.Spacing.Height / 2;
        return snapshot.Icons.Count(i => targets.TryGetValue(i.Key, out var t) && (Math.Abs(t.X - i.X) > tolX || Math.Abs(t.Y - i.Y) > tolY));
    }

    /// <summary>
    /// Speichert die aktuelle Anordnung unter dem Fingerprint der aktuellen Monitor-Konfiguration.
    /// Unveränderte Layouts werden nicht erneut gespeichert (außer mit <paramref name="force"/>).
    /// </summary>
    public SaveResult SaveCurrent(string? nameHint, bool force)
    {
        var config = DisplayDetector.GetCurrent();
        var snapshot = Icons.Read();
        if (snapshot.Icons.Count == 0)
            return new SaveResult(SaveOutcome.NoIcons, null);

        var icons = new Dictionary<string, SavedIcon>(StringComparer.OrdinalIgnoreCase);
        foreach (var icon in snapshot.Icons)
            icons[icon.Key] = new SavedIcon(icon.X, icon.Y, icon.DisplayName);

        var existing = Store.Load(config.Fingerprint);
        if (existing != null && LayoutProfile.IconsEqual(existing.Icons, icons))
        {
            if (!force)
                return new SaveResult(SaveOutcome.Unchanged, existing);
        }

        var profile = new LayoutProfile
        {
            Id = config.Fingerprint,
            Name = existing?.Name ?? nameHint ?? config.DefaultName,
            Monitors = [.. config.Monitors],
            Icons = icons,
            SavedAt = DateTime.Now,
        };
        // Bei unverändertem Layout (force) keinen doppelten Historien-Eintrag erzeugen
        bool changed = existing == null || !LayoutProfile.IconsEqual(existing.Icons, icons);
        Store.Save(profile, archivePrevious: changed);
        Log.Info($"Gespeichert „{profile.Name}“ ({icons.Count} Icons, {config.Fingerprint})");
        return new SaveResult(changed ? SaveOutcome.Saved : SaveOutcome.Unchanged, profile);
    }
}
