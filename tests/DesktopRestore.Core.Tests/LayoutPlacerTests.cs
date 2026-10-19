using DesktopRestore.Core.Layout;
using DesktopRestore.Core.Settings;
using DesktopRestore.Core.Shell;
using static DesktopRestore.Core.Tests.TestData;

namespace DesktopRestore.Core.Tests;

public class LayoutPlacerTests
{
    private static readonly GridSpacing Spacing = new(80, 100);

    private static DesktopIcon Icon(string key, int x = 0, int y = 0) => new(key, Path.GetFileNameWithoutExtension(key), x, y);

    [Fact]
    public void SameConfiguration_RestoresExactPositions()
    {
        var config = Config(Monitor("A", 0, 0, 1920, 1080, primary: true), Monitor("B", 1920, 0, 1920, 1080));
        var profile = Profile(config, (@"C:\a.lnk", 100, 200), (@"C:\b.lnk", 2000, 300));

        var result = LayoutPlacer.Compute(profile, config, [Icon(@"C:\a.lnk"), Icon(@"C:\b.lnk")], Spacing, NewIconZone.RightColumn);

        Assert.Equal(new IconPosition(100, 200), result.Targets[@"C:\a.lnk"]);
        Assert.Equal(new IconPosition(2000, 300), result.Targets[@"C:\b.lnk"]);
        Assert.Equal(2, result.Restored);
        Assert.Equal(0, result.Adjusted);
    }

    [Fact]
    public void SameConfiguration_KeepsIconsNearRightEdge()
    {
        // Papierkorb in der letzten Rasterspalte eines 4K-Monitors (Zelle 240 breit, Symbol bei +72)
        var config = Config(Monitor("A", 0, 0, 3840, 2160, primary: true, dpi: 192, taskbar: 96));
        var profile = Profile(config, (@"::{645FF040-5081-101B-9F08-00AA002F954E}", 3672, 1802));

        var result = LayoutPlacer.Compute(profile, config, [Icon(@"::{645FF040-5081-101B-9F08-00AA002F954E}")], new GridSpacing(240, 200), NewIconZone.Keep);

        Assert.Equal(new IconPosition(3672, 1802), result.Targets.Values.Single());
        Assert.Equal(0, result.Adjusted);
    }

    [Fact]
    public void SavedPositionOutsideWorkArea_IsClamped()
    {
        var config = Config(Monitor("A", 0, 0, 1920, 1080, primary: true));
        var profile = Profile(config, (@"C:\a.lnk", 100, 1060)); // hinter der Taskleiste

        var result = LayoutPlacer.Compute(profile, config, [Icon(@"C:\a.lnk")], Spacing, NewIconZone.Keep);

        Assert.Equal(new IconPosition(100, 1032 - 100), result.Targets[@"C:\a.lnk"]);
        Assert.Equal(1, result.Adjusted);
    }

    [Fact]
    public void KeysAreCaseInsensitive()
    {
        var config = Config(Monitor("A", 0, 0, 1920, 1080, primary: true));
        var profile = Profile(config, (@"C:\Users\X\Desktop\A.lnk", 100, 200));

        var result = LayoutPlacer.Compute(profile, config, [Icon(@"c:\users\x\desktop\a.lnk")], Spacing, NewIconZone.Keep);

        Assert.Equal(new IconPosition(100, 200), result.Targets[@"c:\users\x\desktop\a.lnk"]);
    }

    [Fact]
    public void ShiftedSecondMonitor_MapsRelativeToMonitor()
    {
        var saved = Config(Monitor("A", 0, 0, 1920, 1080, primary: true), Monitor("B", 1920, 0, 1920, 1080));
        var now = Config(Monitor("A", 0, 0, 1920, 1080, primary: true), Monitor("B", 1920, -200, 1920, 1080));
        var profile = Profile(saved, (@"C:\b.lnk", 2000, 300));

        var result = LayoutPlacer.Compute(profile, now, [Icon(@"C:\b.lnk")], Spacing, NewIconZone.Keep);

        Assert.Equal(new IconPosition(2000, 100), result.Targets[@"C:\b.lnk"]);
        Assert.Equal(1, result.Adjusted);
    }

    [Fact]
    public void MissingSecondMonitor_MovesIconsOntoPrimaryAndKeepsThemVisible()
    {
        var saved = Config(Monitor("A", 0, 0, 1920, 1080, primary: true), Monitor("B", 1920, 0, 1920, 1080));
        var now = Config(Monitor("A", 0, 0, 1920, 1080, primary: true));
        var profile = Profile(saved, (@"C:\b.lnk", 3800, 1000));

        var target = LayoutPlacer.Compute(profile, now, [Icon(@"C:\b.lnk")], Spacing, NewIconZone.Keep).Targets[@"C:\b.lnk"];

        Assert.InRange(target.X, 0, 1920 - Spacing.Width);
        Assert.InRange(target.Y, 0, 1080 - 48 - Spacing.Height);
    }

    [Fact]
    public void NewIcons_GoToFreeCellsInRightColumn()
    {
        var config = Config(Monitor("A", 0, 0, 1920, 1080, primary: true));
        // Oberste Zelle der rechten Spalte ist belegt
        var profile = Profile(config, (@"C:\a.lnk", 1920 - 80, 0));

        var result = LayoutPlacer.Compute(profile, config,
            [Icon(@"C:\a.lnk"), Icon(@"C:\neu2.lnk"), Icon(@"C:\neu1.lnk")], Spacing, NewIconZone.RightColumn);

        Assert.Equal(2, result.NewIcons);
        Assert.Equal(new IconPosition(1840, 100), result.Targets[@"C:\neu1.lnk"]);
        Assert.Equal(new IconPosition(1840, 200), result.Targets[@"C:\neu2.lnk"]);
    }

    [Fact]
    public void NewIcons_AlignToExistingGridOffset()
    {
        // 4K bei 200 %: Zellen 240×200, Explorer meldet das Symbol bei (72, 2) innerhalb der Zelle
        var config = Config(Monitor("A", 0, 0, 3840, 2160, primary: true, dpi: 192, taskbar: 96));
        var profile = Profile(config, (@"C:\a.lnk", 72, 2), (@"C:\b.lnk", 72, 202));

        var result = LayoutPlacer.Compute(profile, config,
            [Icon(@"C:\a.lnk"), Icon(@"C:\b.lnk"), Icon(@"C:\neu.lnk")], new GridSpacing(240, 200), NewIconZone.RightColumn);

        Assert.Equal(new IconPosition(15 * 240 + 72, 2), result.Targets[@"C:\neu.lnk"]);
    }

    [Fact]
    public void NewIcons_LeftColumn()
    {
        var config = Config(Monitor("A", 0, 0, 1920, 1080, primary: true));
        var profile = Profile(config);

        var result = LayoutPlacer.Compute(profile, config, [Icon(@"C:\neu.lnk")], Spacing, NewIconZone.LeftColumn);

        Assert.Equal(new IconPosition(0, 0), result.Targets[@"C:\neu.lnk"]);
    }

    [Fact]
    public void NewIcons_KeepZone_LeavesThemUntouched()
    {
        var config = Config(Monitor("A", 0, 0, 1920, 1080, primary: true));
        var profile = Profile(config);

        var result = LayoutPlacer.Compute(profile, config, [Icon(@"C:\neu.lnk", 500, 500)], Spacing, NewIconZone.Keep);

        Assert.Empty(result.Targets);
        Assert.Equal(0, result.NewIcons);
    }

    [Fact]
    public void ColumnFull_ContinuesInNextColumn()
    {
        var config = Config(Monitor("A", 0, 0, 1920, 348, primary: true)); // Arbeitsbereich 300 px hoch => 3 Zeilen
        var profile = Profile(config);
        var icons = Enumerable.Range(1, 4).Select(i => Icon($@"C:\n{i}.lnk")).ToList();

        var result = LayoutPlacer.Compute(profile, config, icons, Spacing, NewIconZone.RightColumn);

        Assert.Equal(new IconPosition(1760, 0), result.Targets[@"C:\n4.lnk"]);
    }
}
