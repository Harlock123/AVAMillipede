namespace AVAMillipede.Engine;

public sealed partial class GameEngine
{
    public const double ArcherSpeed = 260, AimSpeed = 650, ArrowSpeed = 1100;
    public const int ChainLength = 12, StartMushrooms = 46, Ddts = 4;
    public const double StartSeconds = 1.8, DyingSeconds = 1.6, ClearSeconds = 1.6, SlowSeconds = 5;
    public const int HeadPoints = 100, BodyPoints = 10, FirstExtraLife = 15000, ExtraLifeEvery = 15000;

    public int Wave { get; private set; } = 1;
    public int Score { get; private set; }
    public int Lives { get; private set; }
    public Plot[,] Garden { get; } = new Plot[Field.Cols, Field.Rows];
    public Archer Archer { get; } = new();
    public Arrow? Arrow { get; private set; }
    public List<Segment> Segments { get; } = [];
    public List<Bug> Bugs { get; } = [];
    public List<Cloud> Clouds { get; } = [];
    public List<Debris> Wreckage { get; } = [];
    public ShipPilot Pilot { get; } = new();
    public string Cause { get; private set; } = "";
    /// <summary>A swarm wave: no millipede, but bees, dragonflies or mosquitoes, each worth more than the last.</summary>
    public BugKind? Swarm { get; private set; }
    public int SwarmToCome { get; private set; }
    /// <summary>How much longer everything moves at half speed, the inchworm having been shot.</summary>
    public double Slow { get; private set; }
    /// <summary>Damaged mushrooms still to mend after a life is lost.</summary>
    public int Mending { get; private set; }
    /// <summary>For tests: no bugs come, and the millipede holds still.</summary>
    internal bool Calm { get; set; }

    private int _nextExtra, _swarmPoints, _tick, _extraHeads;
    private double _spiderIn, _earwigIn, _wormIn, _beetleIn, _flyIn, _headIn, _swarmIn, _mendClock;

    public int Speed => Wave >= 3 ? 3 : 2;

    private void NewGame()
    {
        _rng = new Random(_seed);
        Score = 0;
        Lives = 3;
        Wave = 1;
        _nextExtra = FirstExtraLife;
        Wreckage.Clear();
        for (int c = 0; c < Field.Cols; c++) for (int r = 0; r < Field.Rows; r++) Garden[c, r] = default;
        int placed = 0;
        while (placed < StartMushrooms)
        {
            int c = _rng.Next(Field.Cols), r = 1 + _rng.Next(Field.Rows - 3);
            if (Garden[c, r].Blocks) continue;
            Garden[c, r] = new Plot { Kind = PlotKind.Mushroom, Hp = 4 };
            placed++;
        }
        TopUpDdt();
        Archer.X = Field.GardenWidth / 2.0;
        Archer.Y = Field.CentreY(Field.Rows - 1);
        SetPhase(Phase.Start);
    }

    /// <summary>DDT canisters in the garden, up to four, well above the archer.</summary>
    private void TopUpDdt()
    {
        int have = 0;
        foreach (var p in Garden) if (p.Kind == PlotKind.Ddt) have++;
        for (int tries = 0; have < Ddts && tries < 200; tries++)
        {
            int c = 1 + _rng.Next(Field.Cols - 2), r = 4 + _rng.Next(Field.ZoneTop - 8);
            if (Garden[c, r].Blocks) continue;
            Garden[c, r] = new Plot { Kind = PlotKind.Ddt, Hp = 1 };
            have++;
        }
    }

    /// <summary>A wave: the millipede coming in at the top, or every fourth, a swarm.</summary>
    private void BeginWave(int wave)
    {
        Wave = wave;
        Segments.Clear();
        Bugs.Clear();
        Clouds.Clear();
        Arrow = null;
        Slow = 0;
        Swarm = wave >= 4 && wave % 4 == 0 ? (wave / 4 % 3) switch { 1 => BugKind.Bee, 2 => BugKind.Dragonfly, _ => BugKind.Mosquito } : null;
        if (Swarm is { } kind)
        {
            SwarmToCome = Math.Min(30, 16 + wave);
            _swarmPoints = 200;
            _swarmIn = 1;
            Say($"WAVE {wave}", $"A SWARM OF {kind.ToString().ToUpperInvariant()}S", StartSeconds + 0.3);
        }
        else
        {
            SwarmToCome = 0;
            StartMillipede();
            Say($"WAVE {wave}", null, StartSeconds + 0.3);
        }
        _spiderIn = 4; _earwigIn = 10; _wormIn = 14; _beetleIn = 18; _flyIn = 8; _headIn = 6;
        _extraHeads = Math.Min(8, 2 + wave);
        SetPhase(Phase.Start);
        Play(Sfx.WaveStart);
    }

    /// <summary>A chain of twelve coming in at the top; from the second wave, some of it broken off as single heads.</summary>
    private void StartMillipede()
    {
        int singles = Math.Min(6, Wave - 1), length = ChainLength - singles, dir = _rng.Next(2) == 0 ? 1 : -1;
        int col = dir > 0 ? 3 : Field.Cols - 4;
        Segment? ahead = null;
        for (int i = 0; i < length; i++)
        {
            var s = new Segment { X = Field.CentreX(col) - dir * i * Field.Cell, Y = Field.CentreY(0), Dir = dir, Prev = ahead };
            Segments.Add(s);
            ahead = s;
        }
        for (int i = 0; i < singles; i++)
            Segments.Add(new Segment { X = Field.CentreX(2 + _rng.Next(Field.Cols - 4)), Y = Field.CentreY(0), Dir = _rng.Next(2) == 0 ? 1 : -1 });
    }

    private void Simulate(double dt, Controls c)
    {
        _tick++;
        MoveDebris(dt);
        switch (Phase)
        {
            case Phase.Start:
                MoveArcher(dt, c);
                if (PhaseTime >= StartSeconds) { SetPhase(Phase.Play); Music(Swarm == null); }
                break;
            case Phase.Play:
                MoveArcher(dt, c);
                Shoot(dt, c);
                Slow = Math.Max(0, Slow - dt);
                if (!Calm)
                {
                    // At half speed while slowed: the millipede moves only every other step.
                    if (Slow <= 0 || _tick % 2 == 0)
                        for (int i = 0; i < Speed; i++) foreach (var s in Segments) MoveSegment(s);
                    Spawn(dt);
                }
                MoveBugs(dt * (Slow > 0 ? 0.5 : 1));
                Smog(dt);
                Catch();
                if (Phase == Phase.Play && Segments.Count == 0 && SwarmToCome == 0 && !Bugs.Any(b => Swarm == b.Kind)) Clear();
                break;
            case Phase.Dying:
                // Each damaged mushroom mended in turn, for points.
                if ((_mendClock -= dt) <= 0 && PhaseTime > 0.6)
                {
                    _mendClock = 0.08;
                    if (MendOne()) break;
                }
                if (PhaseTime >= DyingSeconds && Mending == 0)
                {
                    if (Lives <= 0) { GameOver(); return; }
                    Respawn();
                }
                break;
            case Phase.Clear:
                if (PhaseTime >= ClearSeconds) { ShiftGarden(1); TopUpDdt(); BeginWave(Wave + 1); }
                break;
        }
        PhaseTime += dt;
    }

    // ───────────────────────────── the archer ─────────────────────────────

    private void MoveArcher(double dt, Controls c)
    {
        var a = Archer;
        if (!a.Alive) return;
        double dx, dy;
        if (c.AimX is { } ax && c.AimY is { } ay)
        {
            // The pointer, from screen to garden.
            double tx = ax - Field.Left, ty = ay - Field.Top, ex = tx - a.X, ey = ty - a.Y, d = Math.Sqrt(ex * ex + ey * ey), step = AimSpeed * dt;
            if (d < 0.5) return;
            dx = ex / d * Math.Min(step, d);
            dy = ey / d * Math.Min(step, d);
        }
        else
        {
            dx = c.Dx * ArcherSpeed * dt;
            dy = c.Dy * ArcherSpeed * dt;
        }
        double minY = Field.ZoneTop * Field.Cell + Archer.Radius, maxY = Field.GardenHeight - Archer.Radius;
        double nx = Math.Clamp(a.X + dx, Archer.Radius, Field.GardenWidth - Archer.Radius);
        if (!Blocked(nx, a.Y)) a.X = nx;
        double ny = Math.Clamp(a.Y + dy, minY, maxY);
        if (!Blocked(a.X, ny)) a.Y = ny;
    }

    /// <summary>Would the archer, here, be overlapping something growing?</summary>
    private bool Blocked(double x, double y)
    {
        for (int c = Field.ColOf(x - Archer.Radius); c <= Field.ColOf(x + Archer.Radius); c++)
            for (int r = Field.RowOf(y - Archer.Radius); r <= Field.RowOf(y + Archer.Radius); r++)
            {
                if (!Field.InCols(c) || !Field.InRows(r) || !Garden[c, r].Blocks) continue;
                double nx = Math.Clamp(x, c * Field.Cell + 3, (c + 1) * Field.Cell - 3), ny = Math.Clamp(y, r * Field.Cell + 3, (r + 1) * Field.Cell - 3);
                if ((nx - x) * (nx - x) + (ny - y) * (ny - y) < (Archer.Radius - 2) * (Archer.Radius - 2)) return true;
            }
        return false;
    }

    /// <summary>One arrow at a time, straight up, very fast; held, another goes as soon as the last is spent.</summary>
    private void Shoot(double dt, Controls c)
    {
        if (Arrow == null && c.Fire && Archer.Alive)
        {
            Arrow = new Arrow { X = Archer.X, Y = Archer.Y - 10 };
            Play(Sfx.Arrow);
        }
        if (Arrow is not { } a) return;
        // In small steps, so it can't skip past anything.
        double travel = ArrowSpeed * dt;
        while (travel > 0)
        {
            double step = Math.Min(6, travel);
            travel -= step;
            a.Y -= step;
            if (a.Y < 0) { Arrow = null; return; }
            if (HitAt(a.X, a.Y)) { Arrow = null; return; }
        }
    }

    /// <summary>Whatever the arrow has reached, if anything: hit, and the arrow is spent.</summary>
    private bool HitAt(double x, double y)
    {
        foreach (var s in Segments)
        {
            if (Math.Abs(s.X - x) > 10 || Math.Abs(s.Y - y) > 10) continue;
            KillSegment(s, 1);
            return true;
        }
        foreach (var b in Bugs)
        {
            if (b.Dead || Math.Abs(b.X - x) > b.Radius || Math.Abs(b.Y - y) > b.Radius) continue;
            KillBug(b, 1);
            return true;
        }
        int c = Field.ColOf(x), r = Field.RowOf(y);
        if (!Field.InCols(c) || !Field.InRows(r)) return false;
        ref var p = ref Garden[c, r];
        switch (p.Kind)
        {
            case PlotKind.Empty: return false;
            case PlotKind.Flower: Play(Sfx.Mushroom); return true;
            case PlotKind.Ddt:
                p = default;
                Clouds.Add(new Cloud { X = Field.CentreX(c), Y = Field.CentreY(r) });
                Play(Sfx.Ddt);
                return true;
            default:
                Play(Sfx.Mushroom);
                if (--p.Hp <= 0) { p = default; Award(1, x, y, false); }
                return true;
        }
    }

    // ───────────────────────────── the millipede ─────────────────────────────

    private void MoveSegment(Segment s)
    {
        if (s.Dropping)
        {
            s.Y += s.VDir;
            if (s.Y == s.TargetY)
            {
                s.Dropping = false;
                if (s.Diving)
                {
                    // Poisoned: straight on down to the bottom, then on as before.
                    if (s.Row < Field.Rows - 1) StartDrop(s);
                    else { s.Diving = false; s.VDir = -1; s.Dir = -s.Dir; }
                }
                else s.Dir = -s.Dir;
            }
            return;
        }
        bool atCentre = ((s.X - Field.Cell / 2) % Field.Cell + Field.Cell) % Field.Cell == 0;
        if (atCentre)
        {
            int col = s.Col, row = s.Row, next = col + s.Dir;
            bool outside = !Field.InCols(col);
            bool wall = !outside && !Field.InCols(next);
            bool blocked = !outside && Field.InCols(next) && Garden[next, row].Blocks;
            if (wall || blocked)
            {
                if (blocked && Garden[next, row].Kind == PlotKind.Poison) s.Diving = true;
                StartDrop(s);
                return;
            }
        }
        s.X += s.Dir;
    }

    /// <summary>Down a row (or up, working back through the archer's zone).</summary>
    private static void StartDrop(Segment s)
    {
        int row = s.Row;
        if (s.Diving) s.VDir = 1;
        int target = row + s.VDir;
        if (target >= Field.Rows) { s.VDir = -1; target = row - 1; }
        else if (s.VDir < 0 && target < Field.ZoneTop) { s.VDir = 1; target = row + 1; }
        s.TargetY = Field.CentreY(target);
        s.Dropping = true;
    }

    /// <summary>A segment shot: a mushroom where it was, and the one behind it a head.</summary>
    private void KillSegment(Segment s, int times)
    {
        bool head = s.IsHead;
        s.Dead = true;
        foreach (var behind in Segments.Where(x => x.Prev == s)) behind.Prev = null;
        Segments.Remove(s);
        int c = s.Col, r = s.Row;
        if (Field.InCols(c) && Field.InRows(r) && !Garden[c, r].Blocks) Garden[c, r] = new Plot { Kind = PlotKind.Mushroom, Hp = 4 };
        Award((head ? HeadPoints : BodyPoints) * times, s.X, s.Y, times > 1);
        Burst(s.X, s.Y, Palette.Body, 8);
        Play(head ? Sfx.Head : Sfx.Segment);
    }

    // ───────────────────────────── the bugs ─────────────────────────────

    private int ZoneMushrooms()
    {
        int n = 0;
        for (int c = 0; c < Field.Cols; c++) for (int r = Field.ZoneTop; r < Field.Rows; r++) if (Garden[c, r].Blocks) n++;
        return n;
    }

    private void Spawn(double dt)
    {
        if (Swarm is { } kind)
        {
            if (SwarmToCome > 0 && (_swarmIn -= dt) <= 0)
            {
                _swarmIn = Math.Max(0.25, 0.6 - 0.02 * Wave);
                SwarmToCome--;
                Add(kind);
            }
            if (!Bugs.Any(b => b.Kind == BugKind.Spider) && (_spiderIn -= dt) <= 0) { _spiderIn = 6; Add(BugKind.Spider); }
            return;
        }
        if (!Bugs.Any(b => b.Kind == BugKind.Spider) && (_spiderIn -= dt) <= 0) { _spiderIn = 4 + _rng.NextDouble() * 4; Add(BugKind.Spider); }
        if (Wave >= 2 && (_earwigIn -= dt) <= 0) { _earwigIn = 12 + _rng.NextDouble() * 8; Add(BugKind.Earwig); }
        if ((_wormIn -= dt) <= 0) { _wormIn = 16 + _rng.NextDouble() * 10; Add(BugKind.Inchworm); }
        if (Wave >= 3 && (_beetleIn -= dt) <= 0) { _beetleIn = 15 + _rng.NextDouble() * 8; Add(BugKind.Beetle); }
        if ((_flyIn -= dt) <= 0)
        {
            _flyIn = 6 + _rng.NextDouble() * 6;
            if (ZoneMushrooms() < 6 + Wave || Wave >= 4) Add(Wave >= 6 && _rng.Next(3) == 0 ? BugKind.Mosquito : Wave >= 4 && _rng.Next(2) == 0 ? BugKind.Dragonfly : BugKind.Bee);
        }
        // Once the millipede has reached the archer's zone, new heads come in from the sides now and then (so many a wave).
        if (_extraHeads > 0 && Segments.Count < ChainLength && Segments.Any(s => s.Row >= Field.ZoneTop) && (_headIn -= dt) <= 0)
        {
            _extraHeads--;
            _headIn = Math.Max(2.5, 6 - 0.4 * Wave);
            bool left = _rng.Next(2) == 0;
            int row = Field.ZoneTop + _rng.Next(Field.Rows - Field.ZoneTop);
            Segments.Add(new Segment { X = left ? Field.CentreX(0) : Field.CentreX(Field.Cols - 1), Y = Field.CentreY(row), Dir = left ? 1 : -1, VDir = -1 });
        }
    }

    private Bug Add(BugKind kind)
    {
        var b = new Bug { Kind = kind };
        bool left = _rng.Next(2) == 0;
        double w = Field.GardenWidth;
        switch (kind)
        {
            case BugKind.Spider:
                b.X = left ? -10 : w + 10; b.Y = Field.ZoneTop * Field.Cell + 30; b.Vx = (left ? 1 : -1) * (90 + 6 * Wave); b.Vy = 120;
                break;
            case BugKind.Earwig:
                b.X = left ? -10 : w + 10; b.Y = Field.CentreY(3 + _rng.Next(16)); b.Vx = (left ? 1 : -1) * 110;
                break;
            case BugKind.Inchworm:
                b.X = left ? -10 : w + 10; b.Y = Field.CentreY(4 + _rng.Next(16)); b.Vx = (left ? 1 : -1) * 45;
                break;
            case BugKind.Beetle:
                b.X = left ? Field.CentreX(0) : Field.CentreX(Field.Cols - 1); b.Y = Field.ZoneTop * Field.Cell - 20; b.Vy = 90; b.Turn = left ? 1 : -1;
                break;
            case BugKind.Bee:
                b.X = Field.CentreX(_rng.Next(Field.Cols)); b.Y = -10; b.Vy = 220 + 10 * Wave;
                break;
            case BugKind.Dragonfly:
                b.X = Field.CentreX(2 + _rng.Next(Field.Cols - 4)); b.Y = -10; b.Vy = 110; b.Vx = (left ? 1 : -1) * 170;
                break;
            case BugKind.Mosquito:
                b.X = _rng.NextDouble() * w; b.Y = -10; b.Vy = 140; b.Vx = (left ? 1 : -1) * 170;
                break;
        }
        Bugs.Add(b);
        if (kind == BugKind.Spider) Play(Sfx.Spider);
        if (kind == BugKind.Earwig) Play(Sfx.Earwig);
        return b;
    }

    private void MoveBugs(double dt)
    {
        double w = Field.GardenWidth, h = Field.GardenHeight;
        foreach (var b in Bugs)
        {
            b.T += dt;
            if (Calm) continue;
            switch (b.Kind)
            {
                case BugKind.Spider:
                    // Bounces about the zone and a little above, eating the mushrooms it runs into.
                    if ((b.Turn -= dt) <= 0) { b.Turn = 0.3 + _rng.NextDouble() * 0.7; b.Vy = (_rng.Next(2) == 0 ? -1 : 1) * (110 + 8 * Wave); }
                    if (b.Y < (Field.ZoneTop - 3) * Field.Cell) b.Vy = Math.Abs(b.Vy);
                    if (b.Y > h - 10) b.Vy = -Math.Abs(b.Vy);
                    b.X += b.Vx * dt; b.Y += b.Vy * dt;
                    if (b.X < -30 || b.X > w + 30) b.Dead = true;
                    Eat(b);
                    break;
                case BugKind.Earwig:
                    b.X += b.Vx * dt;
                    Mark(b, PlotKind.Poison);
                    if (b.X < -20 || b.X > w + 20) b.Dead = true;
                    break;
                case BugKind.Inchworm:
                    b.X += b.Vx * dt;
                    if (b.X < -20 || b.X > w + 20) b.Dead = true;
                    break;
                case BugKind.Beetle:
                    // Down its side, along the bottom, up the other side and away.
                    if (b.Mode == 0) { b.Y += b.Vy * dt; if (b.Y >= h - 12) { b.Y = h - 12; b.Mode = 1; } }
                    else if (b.Mode == 1) { b.X += b.Turn * 100 * dt; if (b.X <= Field.CentreX(0) || b.X >= Field.CentreX(Field.Cols - 1)) b.Mode = 2; }
                    else { b.Y -= 90 * dt; if (b.Y < Field.ZoneTop * Field.Cell - 30) b.Dead = true; }
                    Mark(b, PlotKind.Flower);
                    break;
                case BugKind.Bee:
                    b.Y += b.Vy * dt;
                    Seed(b, 0.12);
                    if (b.Y > h + 10) b.Dead = true;
                    break;
                case BugKind.Dragonfly:
                    if ((b.Turn -= dt) <= 0) { b.Turn = 0.45; b.Vx = -b.Vx; }
                    b.X = Math.Clamp(b.X + b.Vx * dt, 8, w - 8); b.Y += b.Vy * dt;
                    Seed(b, 0.08);
                    if (b.Y > h + 10) b.Dead = true;
                    break;
                case BugKind.Mosquito:
                    b.X += b.Vx * dt; b.Y += b.Vy * dt;
                    if (b.X < 8 || b.X > w - 8) b.Vx = -b.Vx;
                    if (b.Y > h + 10) b.Dead = true;
                    break;
            }
        }
        Bugs.RemoveAll(b => b.Dead);
    }

    private void Eat(Bug b)
    {
        int c = Field.ColOf(b.X), r = Field.RowOf(b.Y);
        if (Field.InCols(c) && Field.InRows(r) && Garden[c, r].Kind is PlotKind.Mushroom or PlotKind.Poison) Garden[c, r] = default;
    }

    /// <summary>The mushrooms a bug passes over turn: poisoned (the earwig) or into flowers (the beetle).</summary>
    private void Mark(Bug b, PlotKind kind)
    {
        int c = Field.ColOf(b.X), r = Field.RowOf(b.Y);
        if (Field.InCols(c) && Field.InRows(r) && Garden[c, r].Kind is PlotKind.Mushroom or PlotKind.Poison && Garden[c, r].Kind != kind)
            Garden[c, r] = new Plot { Kind = kind, Hp = 4 };
    }

    /// <summary>Now and then, as it comes into a new row, a mushroom left behind.</summary>
    private void Seed(Bug b, double chance)
    {
        int c = Field.ColOf(b.X), r = Field.RowOf(b.Y);
        if (r == b.LastRow || !Field.InCols(c) || r < 1 || r >= Field.Rows - 1) return;
        b.LastRow = r;
        if (!Garden[c, r].Blocks && _rng.NextDouble() < chance) Garden[c, r] = new Plot { Kind = PlotKind.Mushroom, Hp = 4 };
    }

    private void KillBug(Bug b, int times)
    {
        b.Dead = true;
        int points;
        switch (b.Kind)
        {
            case BugKind.Spider:
                // Worth more the closer it was.
                double d = Math.Sqrt((b.X - Archer.X) * (b.X - Archer.X) + (b.Y - Archer.Y) * (b.Y - Archer.Y));
                points = d < 60 ? 900 : d < 120 ? 600 : 300;
                break;
            case BugKind.Earwig: points = 1000; break;
            case BugKind.Inchworm:
                points = 100;
                Slow = SlowSeconds;
                Play(Sfx.Inchworm);
                break;
            case BugKind.Beetle:
                points = 300;
                ShiftGarden(1);
                Play(Sfx.Beetle);
                break;
            case BugKind.Mosquito:
                points = 400;
                ShiftGarden(-1);
                break;
            case BugKind.Dragonfly: points = 500; break;
            default: points = 200; break;
        }
        if (Swarm == b.Kind)
        {
            // In a swarm, each one worth a hundred more than the last.
            points = _swarmPoints;
            _swarmPoints = Math.Min(1000, _swarmPoints + 100);
            Play(Sfx.Swarm);
        }
        else Play(Sfx.Kill);
        Award(points * times, b.X, b.Y, times > 1);
        Burst(b.X, b.Y, BugColour(b.Kind), 12);
    }

    public static uint BugColour(BugKind kind) => kind switch
    {
        BugKind.Spider => 0xFFFF5AD8, BugKind.Earwig => 0xFFFFB347, BugKind.Inchworm => 0xFF8AFF4A, BugKind.Beetle => 0xFF5CE1FF,
        BugKind.Bee => 0xFFFFE14A, BugKind.Dragonfly => 0xFF6AE0FF, _ => 0xFFFF8A8A,
    };

    /// <summary>
    /// The whole garden moves a row: down (the beetle shot, or a wave beaten), the bottom row lost; or up (the
    /// mosquito), the top row lost.
    /// </summary>
    private void ShiftGarden(int by)
    {
        var copy = (Plot[,])Garden.Clone();
        for (int c = 0; c < Field.Cols; c++)
            for (int r = 0; r < Field.Rows; r++)
            {
                int from = r - by;
                Garden[c, r] = from >= 0 && from < Field.Rows ? copy[c, from] : default;
            }
        // Nothing grows on top of the archer.
        int ac = Field.ColOf(Archer.X), ar = Field.RowOf(Archer.Y);
        for (int c = ac - 1; c <= ac + 1; c++) for (int r = ar - 1; r <= ar + 1; r++) if (Field.InCols(c) && Field.InRows(r)) Garden[c, r] = default;
    }

    // ───────────────────────────── DDT ─────────────────────────────

    private void Smog(double dt)
    {
        foreach (var cl in Clouds)
        {
            cl.Age += dt;
            double r2 = Cloud.Radius * Cloud.Radius;
            foreach (var s in Segments.ToList())
                if ((s.X - cl.X) * (s.X - cl.X) + (s.Y - cl.Y) * (s.Y - cl.Y) < r2) KillSegment(s, 3);
            foreach (var b in Bugs.Where(b => !b.Dead).ToList())
                if ((b.X - cl.X) * (b.X - cl.X) + (b.Y - cl.Y) * (b.Y - cl.Y) < r2) KillBug(b, 3);
            if (Archer.Alive && (Archer.X - cl.X) * (Archer.X - cl.X) + (Archer.Y - cl.Y) * (Archer.Y - cl.Y) < r2 * 0.6) Die("ddt");
        }
        Clouds.RemoveAll(c => c.Age >= Cloud.Life);
        Bugs.RemoveAll(b => b.Dead);
    }

    // ───────────────────────────── losing and winning ─────────────────────────────

    private void Catch()
    {
        var a = Archer;
        if (!a.Alive || Phase != Phase.Play) return;
        foreach (var s in Segments)
            if (Math.Abs(s.X - a.X) < 15 && Math.Abs(s.Y - a.Y) < 15) { Die("millipede"); return; }
        foreach (var b in Bugs)
            if (Math.Abs(b.X - a.X) < b.Radius + 5 && Math.Abs(b.Y - a.Y) < b.Radius + 5) { Die(b.Kind.ToString().ToLowerInvariant()); return; }
    }

    private void Die(string cause)
    {
        if (!Archer.Alive) return;
        Archer.Alive = false;
        Arrow = null;
        Cause = cause;
        Lives--;
        Music(false);
        Play(Sfx.Death);
        Burst(Archer.X, Archer.Y, 0xFFFFE14A, 30);
        Mending = 0;
        foreach (var p in Garden) if (p.Kind == PlotKind.Flower || (p.Kind is PlotKind.Mushroom or PlotKind.Poison && p.Hp < 4)) Mending++;
        _mendClock = 0;
        SetPhase(Phase.Dying);
        if (Lives <= 0) Say("GAME OVER", null, DyingSeconds + 1);
    }

    /// <summary>One damaged mushroom (or a flower, or a poisoned one) made whole again, for five points.</summary>
    private bool MendOne()
    {
        for (int r = 0; r < Field.Rows; r++)
            for (int c = 0; c < Field.Cols; c++)
            {
                ref var p = ref Garden[c, r];
                if (!(p.Kind == PlotKind.Flower || (p.Kind is PlotKind.Mushroom or PlotKind.Poison && p.Hp < 4))) continue;
                p = new Plot { Kind = PlotKind.Mushroom, Hp = 4 };
                Mending = Math.Max(0, Mending - 1);
                Award(5, Field.CentreX(c), Field.CentreY(r), false);
                Play(Sfx.Restore);
                return true;
            }
        Mending = 0;
        return false;
    }

    /// <summary>
    /// Back in, the archer at the bottom; what's left of the millipede starts again from the top, and the
    /// bugs are gone.
    /// </summary>
    private void Respawn()
    {
        var a = Archer;
        a.Alive = true;
        a.X = Field.GardenWidth / 2.0;
        a.Y = Field.CentreY(Field.Rows - 1);
        for (int c = Field.ColOf(a.X) - 1; c <= Field.ColOf(a.X) + 1; c++) Garden[c, Field.Rows - 1] = default;
        Bugs.Clear();
        Clouds.Clear();
        int n = Segments.Count;
        if (n > 0)
        {
            Segments.Clear();
            Segment? ahead = null;
            int dir = _rng.Next(2) == 0 ? 1 : -1, col = dir > 0 ? 3 : Field.Cols - 4;
            for (int i = 0; i < n; i++)
            {
                var s = new Segment { X = Field.CentreX(col) - dir * i * Field.Cell, Y = Field.CentreY(0), Dir = dir, Prev = ahead };
                Segments.Add(s);
                ahead = s;
            }
        }
        _spiderIn = 4;
        SetPhase(Phase.Play);
        Music(Swarm == null);
    }

    private void Clear()
    {
        Music(false);
        Play(Sfx.WaveClear);
        SetPhase(Phase.Clear);
    }

    private void Award(int points, double x, double y, bool triple)
    {
        Score += points;
        if (points >= 50)
            Effects.Add(new Effect { Kind = EffectKind.Score, X = Field.Left + x, Y = Field.Top + y - 12, Text = triple ? $"{points}!" : points.ToString(), Life = 0.8, Colour = triple ? 0xFF8AFF4A : 0xFFFFE14A });
        while (Score >= _nextExtra)
        {
            _nextExtra += ExtraLifeEvery;
            Lives++;
            Play(Sfx.ExtraLife);
        }
    }

    private void Burst(double x, double y, uint colour, int n)
    {
        for (int i = 0; i < n; i++)
        {
            double a = _rng.NextDouble() * Math.PI * 2, v = 40 + _rng.NextDouble() * 160;
            Wreckage.Add(new Debris { X = x, Y = y, Vx = Math.Cos(a) * v, Vy = Math.Sin(a) * v, Life = 0.3 + _rng.NextDouble() * 0.5, Colour = colour });
        }
    }

    private void MoveDebris(double dt)
    {
        foreach (var d in Wreckage)
        {
            d.Age += dt;
            d.X += d.Vx * dt;
            d.Y += d.Vy * dt;
        }
        Wreckage.RemoveAll(d => d.Age >= d.Life);
    }

    /// <summary>The colours of the wave: they change each time.</summary>
    public (uint Cap, uint Stem, uint Body, uint Head) Palette => Palettes[(Wave - 1) % Palettes.Length];

    public static readonly (uint Cap, uint Stem, uint Body, uint Head)[] Palettes =
    [
        (0xFFFF5A3A, 0xFF5CE1FF, 0xFF8AFF4A, 0xFFFFE14A),
        (0xFFFFE14A, 0xFFFF5AD8, 0xFF5CE1FF, 0xFFFF8A3A),
        (0xFF8AFF4A, 0xFFFFB347, 0xFFFF5AD8, 0xFFF2F2FF),
        (0xFF5CE1FF, 0xFFFF5A3A, 0xFFFFE14A, 0xFF8AFF4A),
        (0xFFFF5AD8, 0xFF8AFF4A, 0xFFFFB347, 0xFF5CE1FF),
        (0xFFFFB347, 0xFFB08AFF, 0xFF6AFFD8, 0xFFFF5A3A),
    ];

    // ───────────────────────────── test hooks ─────────────────────────────

    internal void Run(int ticks, Controls c)
    {
        for (int i = 0; i < ticks; i++) Tick(c);
    }

    internal void Idle(int ticks) => Run(ticks, default);

    /// <summary>Straight into a wave, past its number.</summary>
    internal void Go(int wave = 1)
    {
        StartGame();
        if (wave != 1) BeginWave(wave);
        SetPhase(Phase.Play);
        Message = SubMessage = null;
    }

    internal void ClearGarden()
    {
        for (int c = 0; c < Field.Cols; c++) for (int r = 0; r < Field.Rows; r++) Garden[c, r] = default;
    }

    internal void Grow(int c, int r, PlotKind kind) => Garden[c, r] = new Plot { Kind = kind, Hp = 4 };

    internal Bug PutBug(BugKind kind, double x, double y)
    {
        var b = new Bug { Kind = kind, X = x, Y = y };
        Bugs.Add(b);
        return b;
    }

    internal Segment PutSegment(int col, int row, int dir)
    {
        var s = new Segment { X = Field.CentreX(col), Y = Field.CentreY(row), Dir = dir };
        Segments.Add(s);
        return s;
    }

    internal void PutArcher(double x, double y) { Archer.X = x; Archer.Y = y; }
    internal void SetLives(int n) => Lives = n;
    internal void SetScore(int n) => Score = n;
}
