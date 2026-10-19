using DesktopRestore.Core.Display;
using static DesktopRestore.Core.Tests.TestData;

namespace DesktopRestore.Core.Tests;

public class ProfileMatcherTests
{
    private static readonly DisplayConfiguration Office = Config(
        Monitor("DEL1234", 0, 0, 2560, 1440, primary: true),
        Monitor("DEL1234", 2560, 0, 2560, 1440));

    private static readonly DisplayConfiguration Laptop = Config(Monitor("BOE0A1B", 0, 0, 1920, 1200, primary: true, dpi: 144));

    [Fact]
    public void Fingerprint_IsIndependentOfEnumerationOrder()
    {
        var reversed = Config(Office.Monitors[1], Office.Monitors[0]);
        Assert.Equal(Office.Fingerprint, reversed.Fingerprint);
    }

    [Fact]
    public void Fingerprint_ChangesWithScaling()
    {
        var scaled = Config(Monitor("BOE0A1B", 0, 0, 1920, 1200, primary: true, dpi: 120));
        Assert.NotEqual(Laptop.Fingerprint, scaled.Fingerprint);
    }

    [Fact]
    public void FindBest_ReturnsExactMatch()
    {
        var match = ProfileMatcher.FindBest(Office, [Profile(Laptop), Profile(Office)]);
        Assert.NotNull(match);
        Assert.Equal(MatchKind.Exact, match.Kind);
        Assert.Equal(Office.Fingerprint, match.Profile.Id);
    }

    [Fact]
    public void FindBest_ReturnsSimilar_ForSameResolutionsDifferentModel()
    {
        var otherOffice = Config(
            Monitor("HPN3344", 0, 0, 2560, 1440, primary: true),
            Monitor("HPN3344", 2560, 0, 2560, 1440));

        var match = ProfileMatcher.FindBest(otherOffice, [Profile(Laptop), Profile(Office)]);
        Assert.NotNull(match);
        Assert.Equal(MatchKind.Similar, match.Kind);
        Assert.Equal(Office.Fingerprint, match.Profile.Id);
    }

    [Fact]
    public void FindBest_ReturnsNull_WhenResolutionOrCountDiffers()
    {
        var home = Config(Monitor("DEL1234", 0, 0, 3840, 2160, primary: true));
        Assert.Null(ProfileMatcher.FindBest(home, [Profile(Laptop), Profile(Office)]));
    }

    [Fact]
    public void FindBest_PrefersHigherScore()
    {
        var a = Config(Monitor("AAA0001", 0, 0, 1920, 1080, primary: true));
        var b = Config(Monitor("BBB0002", 0, 0, 1920, 1080, primary: true, dpi: 120));
        var current = Config(Monitor("AAA0001", 0, 0, 1920, 1080, primary: true, dpi: 144));

        var match = ProfileMatcher.FindBest(current, [Profile(b), Profile(a)]);
        Assert.Equal(a.Fingerprint, match!.Profile.Id);
    }
}
