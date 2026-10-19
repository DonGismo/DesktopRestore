using DesktopRestore.Core;

namespace DesktopRestore.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var paths = AppPaths.Default;
        Log.Init(paths.LogDir);

        if (args.Length > 0)
            return Cli.Run(args, paths);

        using var mutex = new Mutex(true, @"Local\DesktopRestore.SingleInstance", out bool created);
        if (!created)
        {
            MessageBox.Show("DesktopRestore läuft bereits (Symbol im Infobereich der Taskleiste).",
                "DesktopRestore", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        SynchronizationContext.SetSynchronizationContext(new WindowsFormsSynchronizationContext());
        Application.ThreadException += (_, e) => Log.Error("Unbehandelter Fehler", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Error("Unbehandelter Fehler", e.ExceptionObject as Exception);

        Log.Info($"Start, Version {Application.ProductVersion}");
        using var context = new TrayAppContext(paths);
        Application.Run(context);
        Log.Info("Beendet");
        return 0;
    }
}
