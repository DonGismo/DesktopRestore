using System.IO.Compression;
using DesktopRestore.Core.Layout;
using static DesktopRestore.Core.Tests.TestData;

namespace DesktopRestore.Core.Tests;

public sealed class LayoutStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "DesktopRestoreTests", Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    private static LayoutProfile Sample(int x, DateTime savedAt)
    {
        var profile = Profile(Config(Monitor("A", 0, 0, 1920, 1080, primary: true)), (@"C:\Users\X\Desktop\Datei.lnk", x, 10));
        profile.SavedAt = savedAt;
        return profile;
    }

    [Fact]
    public void SaveAndLoad_RoundTrips()
    {
        var store = new LayoutStore(Path.Combine(_dir, "profiles"));
        var profile = Sample(100, new DateTime(2026, 1, 1, 8, 0, 0));
        store.Save(profile);

        var loaded = store.Load(profile.Id);
        Assert.NotNull(loaded);
        Assert.Equal(profile.Name, loaded.Name);
        Assert.Equal(profile.Monitors, loaded.Monitors);
        Assert.True(LayoutProfile.IconsEqual(profile.Icons, loaded.Icons));
        // Schlüssel nach dem Laden ohne Beachtung der Groß-/Kleinschreibung
        Assert.True(loaded.Icons.ContainsKey(@"c:\users\x\desktop\datei.lnk"));
    }

    [Fact]
    public void Save_ArchivesPreviousState_AndPrunesHistory()
    {
        var store = new LayoutStore(Path.Combine(_dir, "profiles"), historyLimit: 3);
        var start = new DateTime(2026, 1, 1, 8, 0, 0);
        for (int i = 0; i < 6; i++)
            store.Save(Sample(100 + i, start.AddMinutes(i)));

        var history = store.GetHistory(Sample(0, start).Id);
        Assert.Equal(3, history.Count);
        Assert.Equal(start.AddMinutes(4), history[0].SavedAt);
        Assert.Equal(104, store.LoadHistory(history[0])!.Icons.Values.Single().X);
        Assert.Equal(105, store.Load(Sample(0, start).Id)!.Icons.Values.Single().X);
    }

    [Fact]
    public void Rename_DoesNotCreateHistory()
    {
        var store = new LayoutStore(Path.Combine(_dir, "profiles"));
        var profile = Sample(100, DateTime.Now);
        store.Save(profile);
        store.Rename(profile.Id, "Büro");

        Assert.Equal("Büro", store.Load(profile.Id)!.Name);
        Assert.Empty(store.GetHistory(profile.Id));
    }

    [Fact]
    public void Delete_RemovesProfileAndHistory()
    {
        var store = new LayoutStore(Path.Combine(_dir, "profiles"));
        store.Save(Sample(1, DateTime.Now.AddMinutes(-1)));
        store.Save(Sample(2, DateTime.Now));
        var id = Sample(0, DateTime.Now).Id;

        store.Delete(id);

        Assert.Null(store.Load(id));
        Assert.Empty(store.GetHistory(id));
        Assert.Empty(store.LoadAll());
    }

    [Fact]
    public void ExportImport_RoundTrips()
    {
        var source = new LayoutStore(Path.Combine(_dir, "a"));
        source.Save(Sample(1, DateTime.Now.AddMinutes(-1)));
        source.Save(Sample(2, DateTime.Now));
        var zip = Path.Combine(_dir, "export.zip");
        source.Export(zip);

        var target = new LayoutStore(Path.Combine(_dir, "b"));
        int count = target.Import(zip);

        var id = Sample(0, DateTime.Now).Id;
        Assert.Equal(1, count);
        Assert.Equal(2, target.Load(id)!.Icons.Values.Single().X);
        Assert.Single(target.GetHistory(id));
    }

    [Fact]
    public void Import_IgnoresEntriesOutsideProfileFolder()
    {
        var zip = Path.Combine(_dir, "evil.zip");
        Directory.CreateDirectory(_dir);
        using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
        {
            var entry = archive.CreateEntry("../evil.json");
            using var writer = new StreamWriter(entry.Open());
            writer.Write("""{"Id":"evil","Name":"x"}""");
        }

        var store = new LayoutStore(Path.Combine(_dir, "profiles"));
        Assert.Equal(0, store.Import(zip));
        Assert.False(File.Exists(Path.Combine(_dir, "evil.json")));
    }
}
