using System.Runtime.InteropServices;

namespace DesktopRestore.Core.Display;

/// <summary>Ermittelt die aktiven Monitore inkl. Modell-ID, Arbeitsbereich und DPI.</summary>
public static class DisplayDetector
{
    public static DisplayConfiguration GetCurrent()
    {
        var monitors = new List<MonitorInfo>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, _, _, _) =>
        {
            var info = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (!GetMonitorInfo(hMonitor, ref info))
                return true;

            int dpi = 96;
            if (GetDpiForMonitor(hMonitor, MDT_EFFECTIVE_DPI, out uint dpiX, out _) == 0 && dpiX > 0)
                dpi = (int)dpiX;

            monitors.Add(new MonitorInfo
            {
                DeviceName = info.szDevice,
                Model = GetMonitorModel(info.szDevice),
                Bounds = ToRect(info.rcMonitor),
                WorkArea = ToRect(info.rcWork),
                Dpi = dpi,
                IsPrimary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0,
            });
            return true;
        }, IntPtr.Zero);

        return new DisplayConfiguration(monitors);
    }

    /// <summary>
    /// Liest die Geräte-Schnittstelle des Monitors (\\?\DISPLAY#GSM5B7F#...) und gibt den Hardware-Teil zurück.
    /// </summary>
    private static string GetMonitorModel(string adapterDevice)
    {
        var device = new DISPLAY_DEVICE { cb = Marshal.SizeOf<DISPLAY_DEVICE>() };
        for (uint i = 0; EnumDisplayDevices(adapterDevice, i, ref device, EDD_GET_DEVICE_INTERFACE_NAME); i++)
        {
            if ((device.StateFlags & DISPLAY_DEVICE_ACTIVE) != 0)
            {
                var parts = device.DeviceID.Split('#');
                return parts.Length > 1 ? parts[1] : device.DeviceID;
            }
            device.cb = Marshal.SizeOf<DISPLAY_DEVICE>();
        }
        return "Unbekannt";
    }

    private static Rect ToRect(RECT r) => new(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);

    private const int MDT_EFFECTIVE_DPI = 0;
    private const uint MONITORINFOF_PRIMARY = 1;
    private const uint EDD_GET_DEVICE_INTERFACE_NAME = 1;
    private const int DISPLAY_DEVICE_ACTIVE = 1;

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, IntPtr lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFOEX
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DISPLAY_DEVICE
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public int StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFOEX lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplayDevicesW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayDevices(string lpDevice, uint iDevNum, ref DISPLAY_DEVICE lpDisplayDevice, uint dwFlags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);
}
