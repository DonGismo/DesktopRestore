using System.Runtime.InteropServices;

namespace DesktopRestore.App;

/// <summary>
/// Unsichtbares Top-Level-Fenster, das System-Broadcasts empfängt (Nachrichten-only-Fenster bekommen diese nicht).
/// </summary>
internal sealed class DisplayChangeWindow : NativeWindow, IDisposable
{
    private const int WM_CLOSE = 0x0010;
    private const int WM_SETTINGCHANGE = 0x001A;
    private const int WM_DISPLAYCHANGE = 0x007E;
    private const int SPI_SETWORKAREA = 0x002F;

    private readonly int _taskbarCreated = RegisterWindowMessage("TaskbarCreated");

    public DisplayChangeWindow()
    {
        CreateHandle(new CreateParams { Caption = "DesktopRestore" });
    }

    /// <summary>Auflösung oder Monitoranzahl hat sich geändert.</summary>
    public event Action? DisplayChanged;

    /// <summary>Arbeitsbereich (Taskleiste, Skalierung) hat sich geändert.</summary>
    public event Action? WorkAreaChanged;

    /// <summary>Explorer wurde neu gestartet.</summary>
    public event Action? ExplorerRestarted;

    /// <summary>WM_CLOSE, z. B. vom Installer bei Update/Deinstallation.</summary>
    public event Action? CloseRequested;

    protected override void WndProc(ref Message m)
    {
        switch (m.Msg)
        {
            case WM_DISPLAYCHANGE:
                DisplayChanged?.Invoke();
                break;
            case WM_SETTINGCHANGE when m.WParam == SPI_SETWORKAREA:
                WorkAreaChanged?.Invoke();
                break;
            case WM_CLOSE:
                CloseRequested?.Invoke();
                return;
            default:
                if (m.Msg == _taskbarCreated)
                    ExplorerRestarted?.Invoke();
                break;
        }
        base.WndProc(ref m);
    }

    public void Dispose() => DestroyHandle();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int RegisterWindowMessage(string lpString);
}
