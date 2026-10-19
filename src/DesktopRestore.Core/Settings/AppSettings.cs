using System.Text.Json;
using DesktopRestore.Core.Layout;

namespace DesktopRestore.Core.Settings;

public enum NewIconZone
{
    /// <summary>Neue Icons bleiben, wo Explorer sie hingelegt hat.</summary>
    Keep,
    RightColumn,
    LeftColumn,
}

public sealed class AppSettings
{
    /// <summary>Intervall für automatisches Speichern in Minuten (0 = aus).</summary>
    public int AutoSaveMinutes { get; set; } = 5;
    public int HistoryLimit { get; set; } = 10;
    public NewIconZone NewIconZone { get; set; } = NewIconZone.RightColumn;
    public bool RestoreOnStartup { get; set; } = true;
    public bool RestoreOnDisplayChange { get; set; } = true;
    public bool ShowNotifications { get; set; } = true;
    public bool Paused { get; set; }
    /// <summary>Wartezeit nach einem Monitorwechsel, bis Explorer fertig umsortiert hat.</summary>
    public int DisplayChangeDelaySeconds { get; set; } = 3;
    /// <summary>So lange muss der Desktop unverändert sein, bevor beim Start wiederhergestellt wird.</summary>
    public int StartupStableSeconds { get; set; } = 2;
    /// <summary>Nach einem Monitorwechsel wird so lange nicht automatisch gespeichert.</summary>
    public int AutoSaveQuietSeconds { get; set; } = 60;

    public static AppSettings Load(string path)
    {
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), LayoutStore.JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            Log.Warn($"Einstellungen konnten nicht gelesen werden, verwende Standardwerte: {ex.Message}");
        }
        return new AppSettings();
    }

    public void Save(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, LayoutStore.JsonOptions));
        File.Move(tmp, path, overwrite: true);
    }
}
