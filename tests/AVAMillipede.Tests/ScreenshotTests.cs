using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AVAMillipede.Engine;
using AVAMillipede.Views;
using Controls = AVAMillipede.Engine.Controls;

namespace AVAMillipede.Tests;

/// <summary>
/// Draws the game in a real (headless) window at the moments worth showing. Set MILLIPEDE_SHOTS to a folder to
/// save them as PNGs: the README's screenshots come from here.
/// </summary>
public sealed class ScreenshotTests
{
    private static (Window W, GameCanvas Canvas, GameEngine G) Open(int seed = 11)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(Path.Combine(Path.GetTempPath(), "avamillipede-ui-" + Guid.NewGuid().ToString("N")))) { Silent = true };
        var canvas = new GameCanvas(g, new SoundManager(silent: true), startTimer: false);
        var w = new Window { Width = 1024, Height = 768, Content = canvas };
        w.Show();
        Pump();
        return (w, canvas, g);
    }

    private static void Pump()
    {
        for (int i = 0; i < 10; i++) Dispatcher.UIThread.RunJobs();
    }

    private static void Shot(Window w, GameCanvas c, string name)
    {
        c.InvalidateVisual();
        Pump();
        var frame = w.CaptureRenderedFrame();
        Assert.NotNull(frame);
        var dir = Environment.GetEnvironmentVariable("MILLIPEDE_SHOTS");
        if (string.IsNullOrEmpty(dir)) return;
        Directory.CreateDirectory(dir);
        frame.Save(Path.Combine(dir, $"AVAMillipede-{name}.png"), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    /// <summary>Lets the computer pilot play until something worth a picture is on screen.</summary>
    private static bool PlayUntil(GameEngine g, GameCanvas c, Func<GameEngine, bool> done, int maxSeconds = 150, bool holdFire = false)
    {
        for (int t = 0; t < 60 * maxSeconds; t++)
        {
            if (done(g)) { c.Advance(0.016); return true; }
            var move = g.Pilot.Decide(g);
            g.Tick(holdFire ? move with { Fire = false } : move);
            if (t % 3 == 0) c.Advance(1 / 20.0);
        }
        return false;
    }
    [AvaloniaFact]
    public void The_title_and_the_high_scores()
    {
        var (w, c, g) = Open();
        g.Idle(30);
        c.Advance(2.1);
        Shot(w, c, "title");
        g.HighScores.Insert("ACE", 76400, 9);
        g.HighScores.Insert("AVA", 51250, 7);
        g.Idle((int)(60 * (GameEngine.TitleIdleSeconds + GameEngine.AttractSeconds)) + 10);
        Shot(w, c, "high-scores");
    }

    /// <summary>A wave, and the computer at work in it until something worth showing.</summary>
    private static void Wave(int wave, string name, Func<GameEngine, bool> busy, int seed = 3, int seconds = 90)
    {
        var (w, c, g) = Open(seed);
        g.Go(wave);
        g.SetLives(20);
        Assert.True(PlayUntil(g, c, g => g.Phase == Phase.Play && g.Archer.Alive && busy(g), seconds));
        Shot(w, c, name);
    }

    [AvaloniaFact]
    public void The_millipede() => Wave(1, "millipede", g => g.Segments.Count >= 8 && g.Segments.Average(s => s.Y) > 200 && g.Arrow != null);

    [AvaloniaFact]
    public void A_ddt_cloud() => Wave(2, "ddt", g => g.Clouds.Any(c => c.Age > 0.2 && c.Age < 0.6), seconds: 150);

    [AvaloniaFact]
    public void The_spider() => Wave(2, "spider", g => g.Bugs.Any(b => b.Kind == BugKind.Spider && Math.Abs(b.Y - g.Archer.Y) < 110 && b.X > 120 && b.X < 540) && g.Segments.Count >= 6);

    [AvaloniaFact]
    public void Earwig_and_beetle() => Wave(3, "beetle", g => g.Bugs.Any(b => b.Kind == BugKind.Beetle && b.Mode == 1) && g.Garden.Cast<Plot>().Any(p => p.Kind == PlotKind.Poison), seconds: 150);

    [AvaloniaFact]
    public void A_swarm() => Wave(4, "swarm", g => g.Bugs.Count(b => b.Kind == BugKind.Bee) >= 5);

    [AvaloniaFact]
    public void Later_colours() => Wave(6, "wave6", g => g.Segments.Count >= 6 && g.Bugs.Count >= 1 && g.Segments.Average(s => s.Y) > 250, seconds: 150);
}
