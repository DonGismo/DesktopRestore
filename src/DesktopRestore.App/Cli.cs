using System.Text.Json;
using DesktopRestore.Core;
using DesktopRestore.Core.Display;
using DesktopRestore.Core.Layout;
using DesktopRestore.Core.Settings;
using DesktopRestore.Core.Shell;

namespace DesktopRestore.App;

/// <summary>
/// Kommandozeile für Skripte und Diagnose:
///   --dump &lt;datei&gt;   aktuelle Monitore und Icon-Positionen als JSON schreiben
///   --save            aktuelle Anordnung für diesen Arbeitsplatz speichern
///   --restore         gespeicherte Anordnung für diesen Arbeitsplatz wiederherstellen
/// Ergebnis steht im Log, Exitcode 0 = Erfolg.
/// </summary>
internal static class Cli
{
    public static int Run(string[] args, AppPaths paths)
    {
        try
        {
            var settings = AppSettings.Load(paths.SettingsFile);
            var engine = new LayoutEngine(new LayoutStore(paths.ProfilesDir, settings.HistoryLimit), settings);
            switch (args[0].ToLowerInvariant())
            {
                case "--dump" when args.Length > 1:
                    var config = DisplayDetector.GetCurrent();
                    var snapshot = new DesktopIconService().Read();
                    var dump = new { config.Fingerprint, config.Monitors, snapshot.AutoArrange, snapshot.SnapToGrid, snapshot.Spacing, snapshot.Icons };
                    File.WriteAllText(args[1], JsonSerializer.Serialize(dump, new JsonSerializerOptions { WriteIndented = true }));
                    return 0;
                case "--save":
                    var saved = engine.SaveCurrent(null, force: true);
                    Log.Info($"CLI --save: {saved}");
                    return 0;
                case "--restore":
                    var result = engine.RestoreCurrent();
                    Log.Info($"CLI --restore: {result.Message}");
                    return result.Match != null ? 0 : 2;
                default:
                    Log.Warn($"Unbekannte Argumente: {string.Join(' ', args)}");
                    return 1;
            }
        }
        catch (Exception ex)
        {
            Log.Error("CLI fehlgeschlagen", ex);
            return 3;
        }
    }
}
