namespace DesktopRestore.Core;

/// <summary>Ablageorte im Benutzerprofil (%LocalAppData%\DesktopRestore).</summary>
public sealed class AppPaths
{
    public AppPaths(string root)
    {
        Root = root;
    }

    public static AppPaths Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DesktopRestore"));

    public string Root { get; }
    public string ProfilesDir => Path.Combine(Root, "profiles");
    public string LogDir => Path.Combine(Root, "logs");
    public string SettingsFile => Path.Combine(Root, "settings.json");
}
