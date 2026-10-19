using System.Runtime.InteropServices;

namespace DesktopRestore.Core.Shell;

// Minimale COM-Deklarationen für den Zugriff auf die Desktop-Ansicht des Explorers.
// Nicht benötigte Methoden sind als Platzhalter deklariert, damit die vtable-Reihenfolge stimmt.

[StructLayout(LayoutKind.Sequential)]
internal struct NativePoint
{
    public int X;
    public int Y;

    public NativePoint(int x, int y)
    {
        X = x;
        Y = y;
    }
}

[ComImport, Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85"), InterfaceType(ComInterfaceType.InterfaceIsDual)]
internal interface IShellWindows
{
    void _get_Count();
    void _Item();
    void _NewEnum();
    void _Register();
    void _RegisterPending();
    void _Revoke();
    void _OnNavigate();
    void _OnActivated();

    [PreserveSig]
    int FindWindowSW(
        [In, MarshalAs(UnmanagedType.Struct)] ref object pvarLoc,
        [In, MarshalAs(UnmanagedType.Struct)] ref object? pvarLocRoot,
        int swClass,
        out int phwnd,
        int swfwOptions,
        [MarshalAs(UnmanagedType.IDispatch)] out object? ppdispOut);
}

[ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IComServiceProvider
{
    [PreserveSig]
    int QueryService(ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object? ppvObject);
}

[ComImport, Guid("000214E2-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellBrowser
{
    void _GetWindow();
    void _ContextSensitiveHelp();
    void _InsertMenusSB();
    void _SetMenuSB();
    void _RemoveMenusSB();
    void _SetStatusTextSB();
    void _EnableModelessSB();
    void _TranslateAcceleratorSB();
    void _BrowseObject();
    void _GetViewStateStream();
    void _GetControlWindow();
    void _SendControlMsg();

    [PreserveSig]
    int QueryActiveShellView([MarshalAs(UnmanagedType.IUnknown)] out object? ppshv);
}

[ComImport, Guid("1AF3A467-214F-4298-908E-06B03E0B39F9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IFolderView2
{
    // IFolderView
    [PreserveSig] int GetCurrentViewMode(out uint pViewMode);
    [PreserveSig] int SetCurrentViewMode(uint viewMode);
    [PreserveSig] int GetFolder(ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object? ppv);
    [PreserveSig] int Item(int iItemIndex, out IntPtr ppidl);
    [PreserveSig] int ItemCount(uint uFlags, out int pcItems);
    [PreserveSig] int Items(uint uFlags, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object? ppv);
    [PreserveSig] int GetSelectionMarkedItem(out int piItem);
    [PreserveSig] int GetFocusedItem(out int piItem);
    [PreserveSig] int GetItemPosition(IntPtr pidl, out NativePoint ppt);
    [PreserveSig] int GetSpacing(ref NativePoint ppt);
    [PreserveSig] int GetDefaultSpacing(out NativePoint ppt);
    [PreserveSig] int GetAutoArrange();
    [PreserveSig] int SelectItem(int iItem, uint dwFlags);
    [PreserveSig]
    int SelectAndPositionItems(
        uint cidl,
        [In, MarshalAs(UnmanagedType.LPArray)] IntPtr[] apidl,
        [In, MarshalAs(UnmanagedType.LPArray)] NativePoint[] apt,
        uint dwFlags);

    // IFolderView2
    void _SetGroupBy();
    void _GetGroupBy();
    void _SetViewProperty();
    void _GetViewProperty();
    void _SetTileViewProperties();
    void _SetExtendedTileViewProperties();
    void _SetText();
    [PreserveSig] int SetCurrentFolderFlags(uint dwMask, uint dwFlags);
    [PreserveSig] int GetCurrentFolderFlags(out uint pdwFlags);
}

[ComImport, Guid("000214E6-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IShellFolder
{
    void _ParseDisplayName();
    void _EnumObjects();
    void _BindToObject();
    void _BindToStorage();
    void _CompareIDs();
    void _CreateViewObject();
    void _GetAttributesOf();
    void _GetUIObjectOf();

    [PreserveSig]
    int GetDisplayNameOf(IntPtr pidl, uint uFlags, IntPtr pName);
}

internal static class ShellConstants
{
    public static readonly Guid CLSID_ShellWindows = new("9BA05972-F6A8-11CF-A442-00A0C90A8F39");
    public static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    public static readonly Guid IID_IShellBrowser = new("000214E2-0000-0000-C000-000000000046");
    public static readonly Guid IID_IShellFolder = new("000214E6-0000-0000-C000-000000000046");

    public const int CSIDL_DESKTOP = 0;
    public const int SWC_DESKTOP = 8;
    public const int SWFO_NEEDDISPATCH = 1;

    public const uint SVGIO_ALLVIEW = 0x2;
    public const uint SVSI_POSITIONITEM = 0x80;

    public const uint FWF_AUTOARRANGE = 0x1;
    public const uint FWF_SNAPTOGRID = 0x80000;

    public const uint SHGDN_NORMAL = 0x0;
    public const uint SHGDN_FORPARSING = 0x8000;
}

internal static class NativeMethods
{
    [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
    public static extern int StrRetToStrW(IntPtr pstr, IntPtr pidl, [MarshalAs(UnmanagedType.LPWStr)] out string ppsz);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string? lpszClass, string? lpszWindow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClientToScreen(IntPtr hWnd, ref NativePoint lpPoint);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ScreenToClient(IntPtr hWnd, ref NativePoint lpPoint);
}
