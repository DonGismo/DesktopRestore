using DesktopRestore.Core;
using DesktopRestore.Core.Display;
using DesktopRestore.Core.Layout;

namespace DesktopRestore.Core.Tests;

internal static class TestData
{
    public static MonitorInfo Monitor(string model, int x, int y, int w, int h, bool primary = false, int dpi = 96, int taskbar = 48) => new()
    {
        DeviceName = $@"\\.\DISPLAY{x}",
        Model = model,
        Bounds = new Rect(x, y, w, h),
        WorkArea = new Rect(x, y, w, h - taskbar),
        Dpi = dpi,
        IsPrimary = primary,
    };

    public static DisplayConfiguration Config(params MonitorInfo[] monitors) => new(monitors);

    public static LayoutProfile Profile(DisplayConfiguration config, params (string Key, int X, int Y)[] icons)
    {
        var profile = new LayoutProfile
        {
            Id = config.Fingerprint,
            Name = config.DefaultName,
            Monitors = [.. config.Monitors],
            SavedAt = DateTime.Now,
        };
        foreach (var (key, x, y) in icons)
            profile.Icons[key] = new SavedIcon(x, y, Path.GetFileNameWithoutExtension(key));
        return profile;
    }
}
