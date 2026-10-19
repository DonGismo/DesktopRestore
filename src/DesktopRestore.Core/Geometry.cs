using System.Text.Json.Serialization;

namespace DesktopRestore.Core;

/// <summary>Rechteck in physischen Bildschirmkoordinaten.</summary>
public readonly record struct Rect(int X, int Y, int Width, int Height)
{
    [JsonIgnore] public int Right => X + Width;
    [JsonIgnore] public int Bottom => Y + Height;

    public bool Contains(int x, int y) => x >= X && x < Right && y >= Y && y < Bottom;

    /// <summary>Quadrierter Abstand eines Punkts zum Rechteck (0, wenn innen).</summary>
    public long DistanceSquared(int x, int y)
    {
        long dx = x < X ? X - x : x >= Right ? x - (Right - 1) : 0;
        long dy = y < Y ? Y - y : y >= Bottom ? y - (Bottom - 1) : 0;
        return dx * dx + dy * dy;
    }
}

/// <summary>Position eines Icons in Bildschirmkoordinaten (linke obere Ecke).</summary>
public readonly record struct IconPosition(int X, int Y);

/// <summary>Rasterabstand der Desktop-Icons.</summary>
public readonly record struct GridSpacing(int Width, int Height)
{
    public static GridSpacing Default => new(76, 100);
}
