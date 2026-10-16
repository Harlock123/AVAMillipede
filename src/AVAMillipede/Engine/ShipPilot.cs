namespace AVAMillipede.Engine;

/// <summary>
/// The computer archer, for the demonstration (and the tests). It keeps near the bottom of its zone and fires
/// all the time. It goes under whatever matters most: a bug in its zone or about to reach it, a DDT canister with
/// the millipede close to it, or else the millipede's lowest head. When the spider or a segment comes too close it
/// steps away from it first.
/// </summary>
public sealed class ShipPilot
{
    /// <summary>What it is up to, for the probe.</summary>
    public string Doing { get; private set; } = "";

    public Controls Decide(GameEngine g)
    {
        var a = g.Archer;
        if (g.Phase is not (Phase.Play or Phase.Start) || !a.Alive) { Doing = "waiting"; return default; }
        double zoneTop = Field.ZoneTop * Field.Cell;

        // Danger first: the spider or a segment close by.
        double ax = 0, ay = 0;
        foreach (var b in g.Bugs.Where(b => b.Kind is BugKind.Spider or BugKind.Beetle || b.Y > zoneTop - 120))
        {
            double dx = a.X - b.X, dy = a.Y - b.Y, d = Math.Sqrt(dx * dx + dy * dy);
            if (d < 95) { ax += dx / Math.Max(d, 1) * (95 - d); ay += dy / Math.Max(d, 1) * (95 - d); }
        }
        foreach (var s in g.Segments.Where(s => s.Y > zoneTop - 40))
        {
            double dx = a.X - s.X, dy = a.Y - s.Y, d = Math.Sqrt(dx * dx + dy * dy);
            if (d < 75) { ax += dx / Math.Max(d, 1) * (75 - d); ay += dy / Math.Max(d, 1) * (75 - d); }
        }
        if (Math.Abs(ax) + Math.Abs(ay) > 25)
        {
            Doing = "dodging";
            return new Controls(Math.Sign(Math.Round(ax / 10)), Math.Sign(Math.Round(ay / 10)), true);
        }

        // What to go under.
        double? tx = null;
        var bug = g.Bugs.Where(b => b.Y > zoneTop - 200 && b.Kind != BugKind.Spider || b.Kind == BugKind.Spider && b.Y > zoneTop - 40)
                        .OrderByDescending(b => b.Y).FirstOrDefault();
        if (bug != null) { tx = bug.X + bug.Vx * (a.Y - bug.Y) / GameEngine.ArrowSpeed; Doing = "bug"; }
        if (tx == null)
        {
            // A DDT canister the millipede is close to, with nothing in the way below it.
            for (int c = 0; c < Field.Cols && tx == null; c++)
                for (int r = 0; r < Field.ZoneTop; r++)
                {
                    if (g.Garden[c, r].Kind != PlotKind.Ddt) continue;
                    bool near = g.Segments.Count(s => Math.Abs(s.Col - c) <= 2 && Math.Abs(s.Row - r) <= 2) >= 2;
                    bool clear = Enumerable.Range(r + 1, Field.Rows - r - 1).All(rr => !g.Garden[c, rr].Blocks);
                    if (near && clear) { tx = Field.CentreX(c); Doing = "ddt"; break; }
                }
        }
        if (tx == null && g.Segments.Count > 0)
        {
            var head = g.Segments.Where(s => s.IsHead).OrderByDescending(s => s.Y).FirstOrDefault() ?? g.Segments[0];
            tx = head.X + head.Dir * 10;
            Doing = "millipede";
        }
        if (tx == null && g.Bugs.Count > 0) { var b = g.Bugs.OrderByDescending(b => b.Y).First(); tx = b.X; Doing = "bug"; }
        tx ??= Field.GardenWidth / 2.0;
        double want = Math.Clamp(tx.Value, 10, Field.GardenWidth - 10);
        int mx = Math.Abs(want - a.X) > 4 ? Math.Sign(want - a.X) : 0;
        // Keep low in the zone, unless a beetle is coming along the bottom: then up out of its way.
        bool beetle = g.Bugs.Any(b => b.Kind == BugKind.Beetle && b.Mode <= 1);
        double wantY = beetle ? zoneTop + Field.Cell * 1.5 : Field.GardenHeight - Field.Cell * 1.5;
        int my = Math.Abs(a.Y - wantY) > 6 ? Math.Sign(wantY - a.Y) : 0;
        return new Controls(mx, my, true);
    }
}
