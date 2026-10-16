namespace AVAMillipede.Engine;

/// <summary>
/// The controls: the eight directions, or (from the mouse, like the arcade's trackball) a point on the screen for
/// the archer to head for; and fire.
/// </summary>
public readonly record struct Controls(int Dx = 0, int Dy = 0, bool Fire = false, double? AimX = null, double? AimY = null);

/// <summary>
/// The garden: 30 columns by 32 rows of cells, portrait like the arcade screen, in the middle of the screen. The
/// bottom six rows are the archer's zone. Positions inside the garden are pixels from its top left.
/// </summary>
public static class Field
{
    public const double Step = 1.0 / 60;
    public const double Width = 1024, Height = 768;
    public const int Cell = 22, Cols = 30, Rows = 32, ZoneTop = 26;
    public const int GardenWidth = Cols * Cell, GardenHeight = Rows * Cell;
    /// <summary>Where the garden sits on the screen.</summary>
    public const double Left = (Width - GardenWidth) / 2, Top = 60;

    public static int ColOf(double x) => (int)Math.Floor(x / Cell);
    public static int RowOf(double y) => (int)Math.Floor(y / Cell);
    public static int CentreX(int col) => col * Cell + Cell / 2;
    public static int CentreY(int row) => row * Cell + Cell / 2;
    public static bool InCols(int c) => c is >= 0 and < Cols;
    public static bool InRows(int r) => r is >= 0 and < Rows;
}

public enum PlotKind { Empty, Mushroom, Poison, Flower, Ddt }

/// <summary>What's growing in one cell of the garden.</summary>
public struct Plot
{
    public PlotKind Kind;
    /// <summary>Hits left, for a mushroom: four when whole.</summary>
    public int Hp;
    public readonly bool Blocks => Kind != PlotKind.Empty;
}

/// <summary>
/// One segment of the millipede. Every segment follows the same rules (along the row; at a wall or a mushroom,
/// down a row and turn; through the archer's zone and back up it), so a chain that starts in line stays in line.
/// Shoot one and the segment behind it becomes a head.
/// </summary>
public sealed class Segment
{
    public int X, Y, Dir = 1, VDir = 1, TargetY;
    public bool Dropping, Diving, Dead;
    public Segment? Prev;
    public bool IsHead => Prev is null || Prev.Dead;
    public int Col => Field.ColOf(X);
    public int Row => Field.RowOf(Y);
}

public sealed class Archer
{
    public double X = Field.GardenWidth / 2.0, Y = Field.CentreY(Field.Rows - 1);
    public bool Alive = true;
    public const double Radius = 9;
}

public sealed class Arrow
{
    public double X, Y;
}

public enum BugKind
{
    /// <summary>Bounces about the archer's zone, eating mushrooms; worth more the closer it's shot.</summary>
    Spider,
    /// <summary>Scuttles across the garden, poisoning the mushrooms it passes: the millipede dives through them.</summary>
    Earwig,
    /// <summary>Crawls across slowly. Shoot it and everything slows down for a while.</summary>
    Inchworm,
    /// <summary>Comes down a side, along the bottom and up the other, turning mushrooms to flowers. Shot, the garden shifts down a row.</summary>
    Beetle,
    /// <summary>Drops straight down, leaving mushrooms behind.</summary>
    Bee,
    /// <summary>Zig-zags down, leaving mushrooms behind.</summary>
    Dragonfly,
    /// <summary>Bounces down at an angle. Shot, the garden shifts up a row.</summary>
    Mosquito,
}

public sealed class Bug
{
    public BugKind Kind;
    public double X, Y, Vx, Vy, T, Turn;
    public int Mode, LastRow = -1;
    public bool Dead;
    public double Radius => Kind switch { BugKind.Spider => 13, BugKind.Inchworm => 12, BugKind.Beetle => 12, _ => 10 };
}

/// <summary>A DDT cloud: whatever's in it while it lasts is killed, for triple points.</summary>
public sealed class Cloud
{
    public double X, Y, Age;
    public const double Life = 1.2, Radius = 2.7 * Field.Cell;
}

/// <summary>A fleck of something shot, flying out.</summary>
public sealed class Debris
{
    public double X, Y, Vx, Vy, Age, Life = 1;
    public uint Colour;
}

public enum EffectKind { Score, Burst, Word }

public sealed class Effect
{
    public EffectKind Kind;
    public double X, Y, Age, Life = 0.8, Size = 1;
    public string Text = "";
    public uint Colour = 0xFFFFFFFF;
    public bool Done => Age >= Life;
}
