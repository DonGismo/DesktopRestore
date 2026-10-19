using System.Runtime.InteropServices;
using static DesktopRestore.Core.Shell.ShellConstants;

namespace DesktopRestore.Core.Shell;

/// <summary>Ein Icon auf dem Desktop. <see cref="Key"/> ist der Parsing-Name (Dateipfad bzw. ::{GUID}).</summary>
public sealed record DesktopIcon(string Key, string DisplayName, int X, int Y);

public sealed record DesktopSnapshot(IReadOnlyList<DesktopIcon> Icons, bool AutoArrange, bool SnapToGrid, GridSpacing Spacing);

/// <summary>
/// Liest und setzt die Positionen der Desktop-Icons über die Shell-Schnittstelle IFolderView2.
/// Alle Koordinaten nach außen sind physische Bildschirmkoordinaten. Muss auf einem STA-Thread aufgerufen werden.
/// </summary>
public sealed class DesktopIconService
{
    /// <summary>Liest alle Icons. Liefert null, wenn der Desktop (noch) nicht verfügbar ist.</summary>
    public DesktopSnapshot? TryRead(out string? error)
    {
        using var view = DesktopView.TryOpen(out error);
        if (view == null)
            return null;

        var icons = new List<DesktopIcon>();
        view.ForEachItem((pidl, key) =>
        {
            if (view.FolderView.GetItemPosition(pidl, out var pt) != 0)
                return;
            var screen = view.ToScreen(pt);
            var name = view.GetName(pidl, SHGDN_NORMAL) ?? key;
            icons.Add(new DesktopIcon(key, name, screen.X, screen.Y));
        });

        bool autoArrange = view.FolderView.GetAutoArrange() == 0;
        bool snapToGrid = view.FolderView.GetCurrentFolderFlags(out uint flags) == 0 && (flags & FWF_SNAPTOGRID) != 0;
        return new DesktopSnapshot(icons, autoArrange, snapToGrid, view.GetSpacing());
    }

    public DesktopSnapshot Read() =>
        TryRead(out var error) ?? throw new InvalidOperationException(error ?? "Desktop nicht verfügbar.");

    /// <summary>
    /// Setzt die Icons auf die angegebenen Bildschirmpositionen. Schaltet "Automatisch anordnen" ab,
    /// weil Explorer die Positionen sonst sofort wieder überschreibt. Liefert die Anzahl gesetzter Icons.
    /// </summary>
    public int ApplyPositions(IReadOnlyDictionary<string, IconPosition> targets)
    {
        using var view = DesktopView.TryOpen(out var error)
            ?? throw new InvalidOperationException(error ?? "Desktop nicht verfügbar.");

        if (view.FolderView.GetAutoArrange() == 0)
        {
            Log.Info("'Symbole automatisch anordnen' ist aktiv und wird deaktiviert.");
            view.FolderView.SetCurrentFolderFlags(FWF_AUTOARRANGE, 0);
        }

        var pidls = new List<IntPtr>();
        var points = new List<NativePoint>();
        try
        {
            int count = view.ItemCount();
            for (int i = 0; i < count; i++)
            {
                if (view.FolderView.Item(i, out var pidl) != 0 || pidl == IntPtr.Zero)
                    continue;
                var key = view.GetName(pidl, SHGDN_FORPARSING);
                if (key != null && targets.TryGetValue(key, out var target))
                {
                    pidls.Add(pidl);
                    points.Add(view.ToClient(target));
                }
                else
                {
                    Marshal.FreeCoTaskMem(pidl);
                }
            }

            if (pidls.Count == 0)
                return 0;

            int hr = view.FolderView.SelectAndPositionItems((uint)pidls.Count, pidls.ToArray(), points.ToArray(), SVSI_POSITIONITEM);
            if (hr == 0)
                return pidls.Count;

            Log.Warn($"SelectAndPositionItems (Batch) fehlgeschlagen: 0x{hr:X8}, setze einzeln.");
            int moved = 0;
            for (int i = 0; i < pidls.Count; i++)
            {
                if (view.FolderView.SelectAndPositionItems(1, [pidls[i]], [points[i]], SVSI_POSITIONITEM) == 0)
                    moved++;
            }
            return moved;
        }
        finally
        {
            foreach (var pidl in pidls)
                Marshal.FreeCoTaskMem(pidl);
        }
    }

    /// <summary>Kapselt die COM-Objekte einer geöffneten Desktop-Ansicht.</summary>
    private sealed class DesktopView : IDisposable
    {
        private readonly List<object> _comObjects = [];

        public IFolderView2 FolderView { get; private set; } = null!;
        private IShellFolder _folder = null!;
        private IntPtr _listView;

        public static DesktopView? TryOpen(out string? error)
        {
            var view = new DesktopView();
            try
            {
                error = view.Open();
                if (error == null)
                    return view;
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }
            view.Dispose();
            return null;
        }

        private string? Open()
        {
            var type = Type.GetTypeFromCLSID(CLSID_ShellWindows, throwOnError: true)!;
            var shellWindows = Track(Activator.CreateInstance(type))!;

            object loc = CSIDL_DESKTOP;
            object? root = null;
            int hr = ((IShellWindows)shellWindows).FindWindowSW(ref loc, ref root, SWC_DESKTOP, out int desktopHwnd, SWFO_NEEDDISPATCH, out var disp);
            if (hr != 0 || disp == null)
                return $"Desktop-Fenster nicht gefunden (0x{hr:X8}).";
            Track(disp);

            var sid = SID_STopLevelBrowser;
            var iid = IID_IShellBrowser;
            hr = ((IComServiceProvider)disp).QueryService(ref sid, ref iid, out var browser);
            if (hr != 0 || browser == null)
                return $"IShellBrowser nicht verfügbar (0x{hr:X8}).";
            Track(browser);

            hr = ((IShellBrowser)browser).QueryActiveShellView(out var shellView);
            if (hr != 0 || shellView == null)
                return $"Desktop-Ansicht nicht verfügbar (0x{hr:X8}).";
            Track(shellView);

            FolderView = (IFolderView2)shellView;

            var iidFolder = IID_IShellFolder;
            hr = FolderView.GetFolder(ref iidFolder, out var folder);
            if (hr != 0 || folder == null)
                return $"Desktop-Ordner nicht verfügbar (0x{hr:X8}).";
            _folder = (IShellFolder)Track(folder)!;

            _listView = FindDesktopListView(new IntPtr(desktopHwnd));
            if (_listView == IntPtr.Zero)
                return "Desktop-Listenansicht nicht gefunden.";
            return null;
        }

        /// <summary>
        /// Sucht SHELLDLL_DefView\SysListView32 unter Progman bzw. – wenn ein Hintergrund-Wechsel aktiv war –
        /// unter einem der WorkerW-Fenster.
        /// </summary>
        private static IntPtr FindDesktopListView(IntPtr desktopHwnd)
        {
            static IntPtr ListViewOf(IntPtr parent)
            {
                var defView = NativeMethods.FindWindowEx(parent, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (defView == IntPtr.Zero)
                    return IntPtr.Zero;
                var listView = NativeMethods.FindWindowEx(defView, IntPtr.Zero, "SysListView32", null);
                return listView != IntPtr.Zero ? listView : defView;
            }

            if (desktopHwnd != IntPtr.Zero && ListViewOf(desktopHwnd) is var lv && lv != IntPtr.Zero)
                return lv;
            var progman = NativeMethods.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Progman", null);
            if (ListViewOf(progman) is var lv2 && lv2 != IntPtr.Zero)
                return lv2;
            for (var worker = NativeMethods.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "WorkerW", null);
                 worker != IntPtr.Zero;
                 worker = NativeMethods.FindWindowEx(IntPtr.Zero, worker, "WorkerW", null))
            {
                if (ListViewOf(worker) is var lv3 && lv3 != IntPtr.Zero)
                    return lv3;
            }
            return IntPtr.Zero;
        }

        private object? Track(object? comObject)
        {
            if (comObject != null)
                _comObjects.Add(comObject);
            return comObject;
        }

        public int ItemCount() => FolderView.ItemCount(SVGIO_ALLVIEW, out int count) == 0 ? count : 0;

        public void ForEachItem(Action<IntPtr, string> action)
        {
            int count = ItemCount();
            for (int i = 0; i < count; i++)
            {
                if (FolderView.Item(i, out var pidl) != 0 || pidl == IntPtr.Zero)
                    continue;
                try
                {
                    var key = GetName(pidl, SHGDN_FORPARSING);
                    if (!string.IsNullOrEmpty(key))
                        action(pidl, key);
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pidl);
                }
            }
        }

        public string? GetName(IntPtr pidl, uint flags)
        {
            // STRRET ist auf x64 272 Byte groß.
            var strret = Marshal.AllocCoTaskMem(512);
            try
            {
                if (_folder.GetDisplayNameOf(pidl, flags, strret) != 0)
                    return null;
                return NativeMethods.StrRetToStrW(strret, pidl, out var name) == 0 ? name : null;
            }
            finally
            {
                Marshal.FreeCoTaskMem(strret);
            }
        }

        public GridSpacing GetSpacing()
        {
            var pt = new NativePoint();
            if (FolderView.GetSpacing(ref pt) == 0 && pt.X > 0 && pt.Y > 0)
                return new GridSpacing(pt.X, pt.Y);
            return GridSpacing.Default;
        }

        public IconPosition ToScreen(NativePoint client)
        {
            NativeMethods.ClientToScreen(_listView, ref client);
            return new IconPosition(client.X, client.Y);
        }

        public NativePoint ToClient(IconPosition screen)
        {
            var pt = new NativePoint(screen.X, screen.Y);
            NativeMethods.ScreenToClient(_listView, ref pt);
            return pt;
        }

        public void Dispose()
        {
            for (int i = _comObjects.Count - 1; i >= 0; i--)
            {
                if (Marshal.IsComObject(_comObjects[i]))
                    Marshal.ReleaseComObject(_comObjects[i]);
            }
            _comObjects.Clear();
        }
    }
}
