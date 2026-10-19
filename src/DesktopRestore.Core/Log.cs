namespace DesktopRestore.Core;

/// <summary>Einfaches, threadsicheres Datei-Log mit Rotation bei 1 MB.</summary>
public static class Log
{
    private const long MaxSize = 1024 * 1024;
    private static readonly object Sync = new();
    private static string? _file;

    public static string? FilePath => _file;

    public static void Init(string directory)
    {
        Directory.CreateDirectory(directory);
        _file = Path.Combine(directory, "desktoprestore.log");
    }

    public static void Info(string message) => Write("INFO ", message);
    public static void Warn(string message) => Write("WARN ", message);
    public static void Error(string message, Exception? ex = null) =>
        Write("ERROR", ex == null ? message : $"{message}: {ex}");

    private static void Write(string level, string message)
    {
        if (_file == null)
            return;
        lock (Sync)
        {
            try
            {
                var info = new FileInfo(_file);
                if (info.Exists && info.Length > MaxSize)
                    File.Move(_file, _file + ".1", overwrite: true);
                File.AppendAllText(_file, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {level} {message}{Environment.NewLine}");
            }
            catch (IOException)
            {
                // Logging darf die App nie stören.
            }
        }
    }
}
