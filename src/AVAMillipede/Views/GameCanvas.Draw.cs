using System.Globalization;
using Avalonia;
using Avalonia.Media;
using AVAMillipede.Engine;
using Effect = AVAMillipede.Engine.Effect;

namespace AVAMillipede.Views;

public sealed partial class GameCanvas
{
    private static readonly Dictionary<uint, IBrush> BrushCache = [];
    private static readonly Dictionary<(uint, double), Pen> PenCache = [];
    private static readonly Typeface Retro = new(new FontFamily("avares://AVAMillipede/Assets/Fonts#VT323"));

    public static IBrush Brush(uint argb)
    {
        if (!BrushCache.TryGetValue(argb, out var b))
            BrushCache[argb] = b = new SolidColorBrush(Color.FromUInt32(argb)).ToImmutable();
        return b;
    }

    private static Pen Pen(uint argb, double width = 1)
    {
        if (!PenCache.TryGetValue((argb, width), out var p))
            PenCache[(argb, width)] = p = new Pen(Brush(argb), width, lineCap: PenLineCap.Round);
        return p;
    }

    private const uint White = 0xFFF2F2FF, Yellow = 0xFFFFE14A, Red = 0xFFFF4A3A, Dim = 0xFF8A9AC8, Fire = 0xFFFF8A2A, Cyan = 0xFF6AE0FF;

    private static void Text(DrawingContext dc, string text, Point at, double size, uint colour, bool center = false, bool right = false, bool shadow = true)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(colour));
        double x = center ? at.X - ft.Width / 2 : right ? at.X - ft.Width : at.X;
        if (shadow) dc.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Retro, size, Brush(0xC0000000)), new Point(x + 2, at.Y - ft.Height / 2 + 2));
        dc.DrawText(ft, new Point(x, at.Y - ft.Height / 2));
    }

    private static void Poly(DrawingContext dc, uint fill, uint? edge, double width, params Point[] pts)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(pts[0], true);
            foreach (var p in pts.Skip(1)) c.LineTo(p);
            c.EndFigure(true);
        }
        dc.DrawGeometry(fill == 0 ? null : Brush(fill), edge is { } e ? Pen(e, width) : null, g);
    }

    private static uint Hash(int x, int y, int salt)
    {
        uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(salt * 83492791);
        h ^= h >> 13;
        h *= 0x5bd1e995;
        h ^= h >> 15;
        return h;
    }

    private static uint Alpha(uint colour, double a) => (uint)(Math.Clamp(a, 0, 1) * 255) << 24 | (colour & 0x00FFFFFF);

    private bool InGame => Engine.State is GameState.Playing or GameState.Attract or GameState.Paused or GameState.GameOver;

    private void DrawScreen(DrawingContext dc)
    {
        var g = Engine;
        dc.FillRectangle(Brush(0xFF05080A), new Rect(0, 0, Field.Width, Field.Height));
        DrawHedges(dc);
        var pal = g.Palette;
        if (InGame)
        {
            var garden = new Rect(Field.Left, Field.Top, Field.GardenWidth, Field.GardenHeight);
            dc.FillRectangle(Brush(0xFF070A0C), garden);
            // The archer's zone, faintly marked.
            dc.FillRectangle(Brush(0x10FFFFFF), new Rect(Field.Left, Field.Top + Field.ZoneTop * Field.Cell, Field.GardenWidth, (Field.Rows - Field.ZoneTop) * Field.Cell));
            using (dc.PushClip(garden))
            using (dc.PushTransform(Matrix.CreateTranslation(Field.Left, Field.Top)))
            {
                for (int c = 0; c < Field.Cols; c++)
                    for (int r = 0; r < Field.Rows; r++)
                    {
                        var p = g.Garden[c, r];
                        if (p.Kind != PlotKind.Empty) DrawPlot(dc, p, Field.CentreX(c), Field.CentreY(r), pal, g.Phase == Phase.Dying);
                    }
                foreach (var cl in g.Clouds) DrawCloud(dc, cl);
                foreach (var s in g.Segments) DrawSegment(dc, s, pal);
                foreach (var b in g.Bugs) DrawBug(dc, b.Kind, b.X, b.Y, b.T, b.Vx, 1);
                if (g.Arrow is { } a)
                {
                    dc.DrawLine(Pen(0xFFFFF0C0, 2), new Point(a.X, a.Y), new Point(a.X, a.Y + 14));
                    Poly(dc, 0xFFF2F2FF, null, 0, new Point(a.X, a.Y - 4), new Point(a.X + 3, a.Y + 1), new Point(a.X - 3, a.Y + 1));
                }
                foreach (var d in g.Wreckage)
                {
                    double t = Math.Clamp(1 - d.Age / d.Life, 0, 1);
                    dc.FillRectangle(Brush(Alpha(d.Colour, t)), new Rect(d.X - 2, d.Y - 2, 4, 4));
                }
                if (g.Archer.Alive) DrawArcher(dc, g.Archer.X, g.Archer.Y, 1, g.Arrow == null);
            }
            dc.DrawRectangle(null, Pen(Alpha(pal.Stem, 0.6), 2), garden.Inflate(2));
            if (g.Slow > 0) Text(dc, "SLOWED", new Point(Field.Left + Field.GardenWidth / 2, Field.Top + 14), 22, 0xFF8AFF4A, center: true);
        }
        foreach (var e in g.Effects) DrawEffect(dc, e);
        DrawHud(dc);
        switch (g.State)
        {
            case GameState.Title: DrawTitle(dc); break;
            case GameState.HighScores: DrawHighScores(dc); break;
            case GameState.EnterInitials: DrawInitials(dc); break;
            default: DrawMessages(dc); break;
        }
        if (g.State == GameState.Paused)
        {
            dc.FillRectangle(Brush(0x90000000), new Rect(0, 0, Field.Width, Field.Height));
            Text(dc, "PAUSED", new Point(Field.Width / 2, 340), 80, Yellow, center: true);
            Text(dc, "ANY KEY TO CARRY ON - ESC TO QUIT", new Point(Field.Width / 2, 400), 26, White, center: true);
        }
        if (g.State == GameState.GameOver)
            Text(dc, "GAME OVER", new Point(Field.Width / 2, 360), 100, Red, center: true);
        if (g.State == GameState.Attract)
            Text(dc, "DEMONSTRATION - PRESS ENTER TO PLAY", new Point(Field.Width / 2, Field.Height - 14), 24, Yellow, center: true);
    }

    /// <summary>Hedges down both sides of the garden: leaves in two greens, and a few berries.</summary>
    private static void DrawHedges(DrawingContext dc)
    {
        foreach (var (x0, x1) in new[] { (0.0, Field.Left - 6), (Field.Left + Field.GardenWidth + 6, Field.Width) })
        {
            dc.FillRectangle(Brush(0xFF0A1A0E), new Rect(x0, Field.Top, x1 - x0, Field.GardenHeight));
            for (int i = 0; i < 150; i++)
            {
                uint h = Hash(i, (int)x0, 3);
                double x = x0 + h % (uint)Math.Max(1, x1 - x0), y = Field.Top + (h >> 9) % (uint)Field.GardenHeight, r = 6 + (h >> 4) % 7;
                dc.DrawEllipse(Brush((h >> 3) % 3 == 0 ? 0xFF1E4A22 : 0xFF153A1A), null, new Point(x, y), r, r * 0.7);
                if ((h >> 7) % 23 == 0) dc.DrawEllipse(Brush(0xFFB02A3A), null, new Point(x + 2, y), 2.5, 2.5);
            }
        }
    }

    /// <summary>
    /// A mushroom (its cap smaller as it's shot down), a poisoned one (purple, spotted), a flower (out of reach of the
    /// arrows) or a DDT canister. Being mended, damaged ones blink.
    /// </summary>
    private void DrawPlot(DrawingContext dc, Plot p, double x, double y, (uint Cap, uint Stem, uint Body, uint Head) pal, bool mending)
    {
        switch (p.Kind)
        {
            case PlotKind.Mushroom or PlotKind.Poison:
            {
                double k = 0.45 + 0.55 * p.Hp / 4;
                uint cap = p.Kind == PlotKind.Poison ? 0xFFB04AFF : pal.Cap;
                if (mending && p.Hp < 4 && (int)(_clock * 10) % 2 == 0) cap = 0xFFFFFFFF;
                dc.FillRectangle(Brush(pal.Stem), new Rect(x - 3, y, 6, 9));
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(x - 10 * k, y + 1), true);
                    c.ArcTo(new Point(x + 10 * k, y + 1), new Size(10 * k, 9 * k), 0, false, SweepDirection.Clockwise);
                    c.EndFigure(true);
                }
                dc.DrawGeometry(Brush(cap), Pen(Mix(cap, 0xFF000000, 0.4), 1), g);
                if (p.Kind == PlotKind.Poison)
                    for (int i = 0; i < 3; i++) dc.DrawEllipse(Brush(0xFFF0E0FF), null, new Point(x - 5 + i * 5, y - 3 - (i % 2) * 2), 1.5, 1.5);
                break;
            }
            case PlotKind.Flower:
                dc.DrawLine(Pen(0xFF4AAA4A, 2), new Point(x, y + 10), new Point(x, y));
                for (int i = 0; i < 5; i++)
                {
                    double a = i * Math.PI * 2 / 5;
                    dc.DrawEllipse(Brush(pal.Head), null, new Point(x + Math.Cos(a) * 5, y - 2 + Math.Sin(a) * 5), 3.5, 3.5);
                }
                dc.DrawEllipse(Brush(0xFFFFFFFF), null, new Point(x, y - 2), 2.5, 2.5);
                break;
            case PlotKind.Ddt:
                dc.DrawRectangle(Brush(0xFF2A6A3A), Pen(0xFF8AFF8A, 1.2), new Rect(x - 10, y - 8, 20, 17), 3, 3);
                dc.FillRectangle(Brush(0xFF8AFF8A), new Rect(x - 4, y - 11, 8, 3));
                Text(dc, "DDT", new Point(x, y + 1), 13, 0xFFE8FFE8, center: true, shadow: false);
                break;
        }
    }

    private void DrawCloud(DrawingContext dc, Cloud cl)
    {
        double t = cl.Age / Cloud.Life, r = Cloud.Radius * (0.5 + 0.5 * Math.Min(1, t * 3));
        for (int i = 0; i < 7; i++)
        {
            double a = i * Math.PI * 2 / 7 + _clock, d = r * 0.45;
            dc.DrawEllipse(Brush(Alpha(0xFFB0FF6A, 0.35 * (1 - t))), null, new Point(cl.X + Math.Cos(a) * d, cl.Y + Math.Sin(a) * d), r * 0.55, r * 0.55);
        }
        dc.DrawEllipse(Brush(Alpha(0xFFE8FFB0, 0.4 * (1 - t))), null, new Point(cl.X, cl.Y), r * 0.5, r * 0.5);
    }

    /// <summary>A segment: a round body with little legs scurrying; a head has eyes and feelers.</summary>
    private void DrawSegment(DrawingContext dc, Segment s, (uint Cap, uint Stem, uint Body, uint Head) pal)
    {
        double x = s.X, y = s.Y, step = Math.Sin(_clock * 18 + x * 0.3) * 2;
        uint body = s.IsHead ? pal.Head : pal.Body;
        for (int i = -1; i <= 1; i += 2)
        {
            dc.DrawLine(Pen(Mix(body, 0xFF000000, 0.3), 1.5), new Point(x - 5, y), new Point(x - 7 + step * i, y + 9 * i));
            dc.DrawLine(Pen(Mix(body, 0xFF000000, 0.3), 1.5), new Point(x + 5, y), new Point(x + 7 - step * i, y + 9 * i));
        }
        dc.DrawEllipse(Brush(body), Pen(Mix(body, 0xFF000000, 0.45), 1.2), new Point(x, y), 9, 8);
        if (s.IsHead)
        {
            int f = s.Dropping ? 0 : s.Dir;
            dc.DrawEllipse(Brush(0xFFFFFFFF), null, new Point(x + f * 3 - 3, y - 2), 2.4, 2.4);
            dc.DrawEllipse(Brush(0xFFFFFFFF), null, new Point(x + f * 3 + 3, y - 2), 2.4, 2.4);
            dc.DrawEllipse(Brush(0xFF101010), null, new Point(x + f * 4 - 3, y - 2), 1.2, 1.2);
            dc.DrawEllipse(Brush(0xFF101010), null, new Point(x + f * 4 + 3, y - 2), 1.2, 1.2);
            dc.DrawLine(Pen(body, 1.2), new Point(x - 3, y - 7), new Point(x - 6 + f * 3, y - 13));
            dc.DrawLine(Pen(body, 1.2), new Point(x + 3, y - 7), new Point(x + 6 + f * 3, y - 13));
        }
        else dc.DrawEllipse(Brush(Alpha(0xFFFFFFFF, 0.3)), null, new Point(x - 2, y - 3), 3, 2);
    }

    /// <summary>The bugs, each its own way: legs, pincers, a looping body, a shell, stripes and wings, long legs.</summary>
    private void DrawBug(DrawingContext dc, BugKind kind, double x, double y, double t, double vx, double k)
    {
        uint col = GameEngine.BugColour(kind);
        int f = vx < 0 ? -1 : 1;
        switch (kind)
        {
            case BugKind.Spider:
                for (int i = 0; i < 4; i++)
                {
                    double w = Math.Sin(t * 16 + i) * 2;
                    foreach (int side in new[] { -1, 1 })
                    {
                        var knee = new Point(x + side * (8 + i * 2) * k, y - (6 - i * 2) * k + w);
                        dc.DrawLine(Pen(col, 1.6), new Point(x, y), knee);
                        dc.DrawLine(Pen(col, 1.6), knee, new Point(x + side * (13 + i * 2) * k, y + (4 + i * 2) * k - w));
                    }
                }
                dc.DrawEllipse(Brush(Mix(col, 0xFF000000, 0.3)), Pen(col, 1.2), new Point(x, y), 7 * k, 6 * k);
                dc.DrawEllipse(Brush(Red), null, new Point(x - 2.5 * k, y - 2 * k), 1.6 * k, 1.6 * k);
                dc.DrawEllipse(Brush(Red), null, new Point(x + 2.5 * k, y - 2 * k), 1.6 * k, 1.6 * k);
                break;
            case BugKind.Earwig:
                dc.DrawEllipse(Brush(col), Pen(Mix(col, 0xFF000000, 0.4), 1), new Point(x, y), 11 * k, 5 * k);
                dc.DrawLine(Pen(col, 2), new Point(x - f * 10 * k, y), new Point(x - f * 17 * k, y - 4 * k));
                dc.DrawLine(Pen(col, 2), new Point(x - f * 10 * k, y), new Point(x - f * 17 * k, y + 4 * k));
                for (int i = -1; i <= 1; i++) dc.DrawLine(Pen(Mix(col, 0xFF000000, 0.3), 1.2), new Point(x + i * 5 * k, y), new Point(x + i * 5 * k + Math.Sin(t * 20 + i) * 2, y + 8 * k));
                dc.DrawEllipse(Brush(0xFF101010), null, new Point(x + f * 8 * k, y - 1.5 * k), 1.5 * k, 1.5 * k);
                break;
            case BugKind.Inchworm:
            {
                double hump = (Math.Sin(t * 5) + 1) * 4 * k;
                var g = new StreamGeometry();
                using (var c = g.Open())
                {
                    c.BeginFigure(new Point(x - 12 * k, y + 4 * k), false);
                    c.QuadraticBezierTo(new Point(x, y - 6 * k - hump), new Point(x + 12 * k, y + 4 * k));
                }
                dc.DrawGeometry(null, Pen(col, 6 * k, PenLineCap.Round), g);
                dc.DrawEllipse(Brush(0xFF101010), null, new Point(x + f * 12 * k, y + 2 * k), 1.5 * k, 1.5 * k);
                break;
            }
            case BugKind.Beetle:
                for (int i = -1; i <= 1; i++)
                    foreach (int side in new[] { -1, 1 })
                        dc.DrawLine(Pen(Mix(col, 0xFF000000, 0.3), 1.4), new Point(x, y + i * 4 * k), new Point(x + side * 12 * k, y + i * 5 * k + Math.Sin(t * 14 + i) * 2));
                dc.DrawEllipse(Brush(col), Pen(Mix(col, 0xFF000000, 0.45), 1.2), new Point(x, y), 9 * k, 11 * k);
                dc.DrawLine(Pen(Mix(col, 0xFF000000, 0.45), 1.2), new Point(x, y - 9 * k), new Point(x, y + 11 * k));
                dc.DrawEllipse(Brush(0xFF203040), null, new Point(x, y - 11 * k), 5 * k, 4 * k);
                break;
            case BugKind.Bee:
            {
                double flap = Math.Abs(Math.Sin(t * 30)) * 7 * k;
                dc.DrawEllipse(Brush(0xA0FFFFFF), null, new Point(x - 6 * k, y - 6 * k), 5 * k, flap + 1);
                dc.DrawEllipse(Brush(0xA0FFFFFF), null, new Point(x + 6 * k, y - 6 * k), 5 * k, flap + 1);
                dc.DrawEllipse(Brush(col), Pen(0xFF5A4A00, 1), new Point(x, y), 7 * k, 9 * k);
                for (int i = -1; i <= 1; i++) dc.FillRectangle(Brush(0xFF2A2000), new Rect(x - 6 * k, y + i * 4 * k - 1, 12 * k, 2.2));
                break;
            }
            case BugKind.Dragonfly:
            {
                double flap = Math.Sin(t * 28) * 3;
                foreach (int side in new[] { -1, 1 })
                {
                    dc.DrawEllipse(Brush(Alpha(col, 0.5)), null, new Point(x + side * 9 * k, y - 4 * k + flap), 9 * k, 3 * k);
                    dc.DrawEllipse(Brush(Alpha(col, 0.5)), null, new Point(x + side * 8 * k, y + 2 * k - flap), 8 * k, 2.5 * k);
                }
                dc.DrawLine(Pen(col, 3 * k), new Point(x, y - 8 * k), new Point(x, y + 14 * k));
                dc.DrawEllipse(Brush(0xFF2A5AFF), null, new Point(x, y - 8 * k), 3.5 * k, 3.5 * k);
                break;
            }
            default:
            {
                // The mosquito: long dangling legs and a needle.
                for (int i = -1; i <= 1; i++)
                {
                    dc.DrawLine(Pen(col, 1.1), new Point(x, y), new Point(x - 12 * k, y + (8 + i * 4) * k));
                    dc.DrawLine(Pen(col, 1.1), new Point(x, y), new Point(x + 12 * k, y + (8 + i * 4) * k));
                }
                dc.DrawEllipse(Brush(Alpha(0xFFFFFFFF, 0.5)), null, new Point(x - 6 * k, y - 5 * k), 6 * k, 2.5 * k);
                dc.DrawEllipse(Brush(Alpha(0xFFFFFFFF, 0.5)), null, new Point(x + 6 * k, y - 5 * k), 6 * k, 2.5 * k);
                dc.DrawEllipse(Brush(col), null, new Point(x, y), 3.5 * k, 6 * k);
                dc.DrawLine(Pen(col, 1.4), new Point(x, y + 5 * k), new Point(x, y + 13 * k));
                break;
            }
        }
    }

    /// <summary>The archer: a gnome in a tall pointed hat, a beard, and a bow raised to fire upwards.</summary>
    private void DrawArcher(DrawingContext dc, double x, double y, double k, bool nocked)
    {
        Poly(dc, 0xFFD02A2A, 0xFF6A1010, 1, new Point(x - 7 * k, y - 4 * k), new Point(x + 1 * k, y - 22 * k), new Point(x + 7 * k, y - 4 * k));
        dc.DrawEllipse(Brush(0xFFFFD0A0), null, new Point(x, y - 2 * k), 6 * k, 5 * k);
        Poly(dc, 0xFFF2F2F2, null, 0, new Point(x - 6 * k, y), new Point(x + 6 * k, y), new Point(x, y + 9 * k));
        dc.DrawRectangle(Brush(0xFF2A6AD0), null, new Rect(x - 6 * k, y + 2 * k, 12 * k, 8 * k), 2, 2);
        var bow = new StreamGeometry();
        using (var c = bow.Open())
        {
            c.BeginFigure(new Point(x - 10 * k, y - 6 * k), false);
            c.QuadraticBezierTo(new Point(x, y - 18 * k), new Point(x + 10 * k, y - 6 * k));
        }
        dc.DrawGeometry(null, Pen(0xFFC08040, 2.2 * k), bow);
        dc.DrawLine(Pen(0xFFE0E0E0, 1), new Point(x - 10 * k, y - 6 * k), new Point(x + 10 * k, y - 6 * k));
        if (nocked) dc.DrawLine(Pen(0xFFFFF0C0, 1.6), new Point(x, y - 4 * k), new Point(x, y - 20 * k));
    }

    private void DrawEffect(DrawingContext dc, Effect e)
    {
        double t = e.Age / e.Life;
        var c = new Point(e.X, e.Y);
        switch (e.Kind)
        {
            case EffectKind.Burst:
            {
                double r = (6 + 30 * t) * e.Size;
                dc.DrawEllipse(null, Pen(Alpha(e.Colour, 1 - t), 2.5), c, r, r);
                break;
            }
            case EffectKind.Score:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 24), 20, e.Colour, center: true);
                break;
            case EffectKind.Word:
                Text(dc, e.Text, new Point(c.X, c.Y - t * 30), 34, e.Colour, center: true);
                break;
        }
    }

    // ───────────────────────────── the score bar, and the screens ─────────────────────────────

    private void DrawHud(DrawingContext dc)
    {
        var g = Engine;
        Text(dc, "SCORE", new Point(16, 16), 22, Dim);
        Text(dc, g.Score.ToString("D6"), new Point(16, 46), 36, Yellow);
        Text(dc, "HI", new Point(Field.Width - 16, 16), 22, Dim, right: true);
        Text(dc, Math.Max(g.HighScores.TopScore, g.Score).ToString("D6"), new Point(Field.Width - 16, 46), 30, Dim, right: true);
        if (!InGame) return;
        Text(dc, g.Swarm is { } kind ? $"WAVE {g.Wave}: {kind.ToString().ToUpperInvariant()} SWARM" : $"WAVE {g.Wave}", new Point(512, 22), 28, g.Palette.Head, center: true);
        int spare = Math.Max(0, g.Lives - (g.Archer.Alive ? 1 : 0));
        for (int i = 0; i < Math.Min(spare, 6); i++) DrawArcher(dc, 220 + i * 26, 44, 0.8, false);
    }

    private void DrawMessages(DrawingContext dc)
    {
        var g = Engine;
        if (g.Message is not { } msg) return;
        double cx = Field.Width / 2, cy = 330;
        var (w, h) = (Math.Max(msg.Length * 26, (g.SubMessage?.Length ?? 0) * 15) + 60, g.SubMessage != null ? 106.0 : 70.0);
        dc.FillRectangle(Brush(0xC0080A20), new Rect(cx - w / 2, cy - 46, w, h), 6);
        Text(dc, msg, new Point(cx, cy - 12), msg.Length > 14 ? 42 : 54, g.Phase == Phase.Dying ? Red : Yellow, center: true);
        if (g.SubMessage is { } sub) Text(dc, sub, new Point(cx, cy + 34), 26, White, center: true);
    }

    private void DrawTitle(DrawingContext dc)
    {
        double cx = Field.Width / 2;
        dc.FillRectangle(Brush(0xFF070A0C), new Rect(Field.Left, Field.Top, Field.GardenWidth, Field.GardenHeight));
        Text(dc, "AVA MILLIPEDE", new Point(cx, 112), 92, 0xFF8AFF4A, center: true);
        Text(dc, "AFTER THE 1982 ARCADE CLASSIC", new Point(cx, 170), 26, Dim, center: true);
        // A millipede crossing, under the title.
        var pal = GameEngine.Palettes[(int)(_clock / 3) % GameEngine.Palettes.Length];
        for (int i = 0; i < 9; i++)
        {
            double x = cx - 160 + ((_clock * 60 + i * 22) % 320);
            DrawSegment(dc, new Segment { X = (int)x, Y = 222, Dir = 1, Prev = i == 8 ? null : new Segment() }, pal);
        }
        Text(dc, "SHOOT THE MILLIPEDE BEFORE IT REACHES YOU - EACH PIECE TURNS TO A MUSHROOM", new Point(cx, 266), 20, White, center: true);
        Text(dc, "SHOOT A DDT CANISTER: ALL IN THE CLOUD FOR TRIPLE POINTS", new Point(cx, 292), 20, 0xFF8AFF8A, center: true);
        (BugKind Kind, string Points, string Note)[] legend =
        [
            (BugKind.Spider, "300-900", ""), (BugKind.Earwig, "1000", "POISONS"), (BugKind.Inchworm, "100", "SLOWS ALL"), (BugKind.Beetle, "300", "FLOWERS"),
            (BugKind.Bee, "200", ""), (BugKind.Dragonfly, "500", ""), (BugKind.Mosquito, "400", "SHIFTS UP"),
        ];
        for (int i = 0; i < legend.Length; i++)
        {
            double x = Field.Left + 30 + i * 100;
            DrawBug(dc, legend[i].Kind, x, 352, _clock + i, 1, 1.6);
            Text(dc, legend[i].Kind.ToString().ToUpperInvariant(), new Point(x, 396), 18, Dim, center: true);
            Text(dc, legend[i].Points, new Point(x, 418), 18, Yellow, center: true);
            if (legend[i].Note.Length > 0) Text(dc, legend[i].Note, new Point(x, 440), 15, 0xFF8AE0FF, center: true);
        }
        Text(dc, "EVERY FOURTH WAVE A SWARM - EACH ONE WORTH MORE THAN THE LAST", new Point(cx, 488), 20, White, center: true);
        DrawArcher(dc, cx, 560, 2.2, true);
        if ((int)(_clock * 2) % 2 == 0) Text(dc, "PRESS ENTER OR START", new Point(cx, 626), 42, Yellow, center: true);
        Text(dc, "MOUSE OR ARROWS: MOVE    BUTTON OR SPACE: FIRE", new Point(cx, 676), 21, Dim, center: true);
        Text(dc, $"P: PAUSE    M: SOUND    EXTRA LIFE EVERY {GameEngine.ExtraLifeEvery}", new Point(cx, 704), 21, Dim, center: true);
    }

    private void DrawHighScores(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "TOP GARDENERS", new Point(cx, 140), 68, 0xFF8AFF4A, center: true);
        Text(dc, "RANK NAME    SCORE    WAVE", new Point(cx, 204), 30, Dim, center: true);
        var entries = g.HighScores.Entries;
        for (int i = 0; i < entries.Count; i++)
        {
            var e = entries[i];
            bool mine = i == g.NewEntryIndex && (int)(_clock * 4) % 2 == 0;
            Text(dc, $"{i + 1,2}.  {e.Initials}  {e.Score,8}   {e.Stage,4}", new Point(cx, 248 + i * 42), 34, mine ? Red : i == 0 ? Yellow : White, center: true);
        }
        Text(dc, "PRESS ENTER TO PLAY", new Point(cx, 710), 30, White, center: true);
    }

    private void DrawInitials(DrawingContext dc)
    {
        var g = Engine;
        double cx = Field.Width / 2;
        Text(dc, "A NEW HIGH SCORE!", new Point(cx, 200), 64, Yellow, center: true);
        Text(dc, g.Score.ToString(), new Point(cx, 270), 52, White, center: true);
        Text(dc, "ENTER YOUR INITIALS", new Point(cx, 350), 34, Dim, center: true);
        if (g.Initials is { } entry)
        {
            string s = entry.Initials;
            for (int i = 0; i < s.Length; i++)
            {
                bool cur = i == entry.Cursor && (int)(_clock * 3) % 2 == 0;
                Text(dc, s[i].ToString(), new Point(cx - 60 + i * 60, 440), 80, cur ? Red : Yellow, center: true);
                dc.FillRectangle(Brush(i == entry.Cursor ? Red : Dim), new Rect(cx - 82 + i * 60, 480, 44, 4));
            }
        }
        Text(dc, "TYPE, OR UP/DOWN AND SPACE; ENTER WHEN DONE", new Point(cx, 560), 26, White, center: true);
    }

    /// <summary>A colour part of the way to another.</summary>
    private static uint Mix(uint a, uint b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        uint Ch(int shift) => (uint)Math.Round(((a >> shift) & 0xFF) * (1 - t) + ((b >> shift) & 0xFF) * t);
        return Ch(24) << 24 | Ch(16) << 16 | Ch(8) << 8 | Ch(0);
    }

    private static Pen Pen(uint argb, double width, PenLineCap cap) => new(Brush(argb), width, lineCap: cap);
}
