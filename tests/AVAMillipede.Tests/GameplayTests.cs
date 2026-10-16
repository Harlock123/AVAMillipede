using AVAMillipede.Engine;

namespace AVAMillipede.Tests;

public class GameplayTests
{
    private static string TempFolder() => Path.Combine(Path.GetTempPath(), "avamillipede-tests-" + Guid.NewGuid().ToString("N"));

    /// <summary>A wave in play, its garden and millipede cleared away but one segment far off, calm: nothing moves on its own.</summary>
    private static GameEngine Game(int wave = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true, Calm = true };
        g.Go(wave);
        g.ClearGarden();
        g.Segments.Clear();
        g.PutSegment(0, 0, 1);
        return g;
    }

    /// <summary>A real game, with plenty of lives.</summary>
    private static GameEngine Real(int wave = 1, int seed = 7)
    {
        var g = new GameEngine(seed, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Go(wave);
        g.SetLives(99);
        return g;
    }

    private static readonly Controls FireButton = new(Fire: true);
    private static double X(int col) => Field.CentreX(col);

    /// <summary>The archer under a column, at the bottom; one arrow, and time for it to get there.</summary>
    private static void ShootFrom(GameEngine g, int col, int after = 45)
    {
        g.PutArcher(X(col), Field.CentreY(Field.Rows - 1));
        g.Run(1, FireButton);
        g.Idle(after);
    }

    // ───────────────────────────── the archer ─────────────────────────────

    [Fact]
    public void The_archer_stays_in_its_zone_and_mushrooms_stop_it()
    {
        var g = Game();
        g.Run(200, new Controls(Dy: -1));
        Assert.True(g.Archer.Y >= Field.ZoneTop * Field.Cell);
        g.PutArcher(X(10), Field.CentreY(Field.Rows - 1));
        g.Grow(12, Field.Rows - 1, PlotKind.Mushroom);
        g.Run(60, new Controls(Dx: 1));
        Assert.True(g.Archer.X < X(12) - 10, $"{g.Archer.X}");
    }

    [Fact]
    public void The_mouse_moves_the_archer_towards_the_pointer()
    {
        var g = Game();
        double tx = Field.Left + 100, ty = Field.Top + Field.CentreY(Field.Rows - 3);
        g.Run(60, new Controls(AimX: tx, AimY: ty));
        Assert.Equal(100, g.Archer.X, 1);
        Assert.Equal(Field.CentreY(Field.Rows - 3), g.Archer.Y, 1);
    }

    [Fact]
    public void One_arrow_at_a_time_and_a_mushroom_takes_four()
    {
        var g = Game();
        g.Grow(8, 10, PlotKind.Mushroom);
        g.PutArcher(X(8), Field.CentreY(Field.Rows - 1));
        g.Run(1, FireButton);
        Assert.NotNull(g.Arrow);
        double y = g.Arrow!.Y;
        g.Run(1, FireButton);
        Assert.True(g.Arrow!.Y < y);
        g.Idle(45);
        Assert.Equal(3, g.Garden[8, 10].Hp);
        for (int i = 0; i < 3; i++) ShootFrom(g, 8);
        Assert.Equal(PlotKind.Empty, g.Garden[8, 10].Kind);
        Assert.Equal(1, g.Score);
    }

    // ───────────────────────────── the millipede ─────────────────────────────

    [Fact]
    public void A_segment_moves_along_turns_down_at_a_mushroom_and_shot_becomes_one()
    {
        var g = Real();
        g.Segments.Clear();
        g.Bugs.Clear();
        g.ClearGarden();
        var s = g.PutSegment(5, 4, 1);
        g.Grow(9, 4, PlotKind.Mushroom);
        for (int i = 0; i < 120 && s.Row == 4; i++) g.Tick(default);
        Assert.Equal(8, s.Col);
        for (int i = 0; i < 60 && s.Dropping; i++) g.Tick(default);
        Assert.Equal(5, s.Row);
        Assert.Equal(-1, s.Dir);
    }

    [Fact]
    public void Shooting_a_segment_leaves_a_mushroom_and_the_one_behind_becomes_a_head()
    {
        var g = Game();
        g.Segments.Clear();
        var front = g.PutSegment(10, 8, 1);
        var back = g.PutSegment(9, 8, 1);
        back.Prev = front;
        Assert.False(back.IsHead);
        ShootFrom(g, 10);
        Assert.DoesNotContain(front, g.Segments);
        Assert.True(back.IsHead);
        Assert.Equal(PlotKind.Mushroom, g.Garden[10, 8].Kind);
        Assert.Equal(GameEngine.HeadPoints, g.Score);
        ShootFrom(g, 9);
        Assert.Equal(GameEngine.HeadPoints * 2, g.Score);
    }

    [Fact]
    public void A_poisoned_mushroom_sends_the_millipede_diving()
    {
        var g = Real();
        g.Segments.Clear();
        g.Bugs.Clear();
        g.ClearGarden();
        var s = g.PutSegment(5, 3, 1);
        g.Grow(7, 3, PlotKind.Poison);
        for (int i = 0; i < 600 && s.Row < Field.Rows - 1; i++) g.Tick(default);
        Assert.Equal(Field.Rows - 1, s.Row);
    }

    // ───────────────────────────── DDT ─────────────────────────────

    [Fact]
    public void A_ddt_canister_shot_kills_all_in_its_cloud_for_triple_points()
    {
        var g = Game();
        g.Segments.Clear();
        g.Grow(12, 10, PlotKind.Ddt);
        var s = g.PutSegment(13, 10, 1);
        var spider = g.PutBug(BugKind.Earwig, X(11), Field.CentreY(9));
        g.PutSegment(0, 2, 1);
        ShootFrom(g, 12);
        Assert.DoesNotContain(s, g.Segments);
        Assert.DoesNotContain(spider, g.Bugs);
        Assert.Equal(PlotKind.Empty, g.Garden[12, 10].Kind);
        Assert.Equal(GameEngine.HeadPoints * 3 + 1000 * 3, g.Score);
    }

    // ───────────────────────────── the bugs ─────────────────────────────

    [Fact]
    public void The_spider_is_worth_more_the_closer_it_is_shot()
    {
        var g = Game();
        g.PutArcher(X(10), Field.CentreY(Field.Rows - 1));
        g.PutBug(BugKind.Spider, X(10), Field.CentreY(Field.Rows - 3));
        g.Run(1, FireButton);
        g.Idle(20);
        Assert.Equal(900, g.Score);
        g.PutBug(BugKind.Spider, X(10), Field.CentreY(Field.ZoneTop - 2));
        g.Run(1, FireButton);
        g.Idle(30);
        Assert.Equal(900 + 300, g.Score);
    }

    [Fact]
    public void An_earwig_poisons_and_a_beetle_turns_mushrooms_to_flowers_the_arrows_cannot_hurt()
    {
        var g = Real(3);
        g.Segments.Clear();
        g.Bugs.Clear();
        g.ClearGarden();
        g.PutSegment(0, 0, 1);
        var e = g.PutBug(BugKind.Earwig, X(2), Field.CentreY(8));
        e.Vx = 110;
        g.Grow(6, 8, PlotKind.Mushroom);
        var b = g.PutBug(BugKind.Beetle, X(Field.Cols - 1), Field.CentreY(Field.Rows - 1));
        b.Mode = 1; b.Turn = -1;
        g.Grow(Field.Cols - 6, Field.Rows - 1, PlotKind.Mushroom);
        g.PutArcher(X(3), Field.CentreY(Field.ZoneTop + 1));
        g.Idle(90);
        Assert.Equal(PlotKind.Poison, g.Garden[6, 8].Kind);
        Assert.Equal(PlotKind.Flower, g.Garden[Field.Cols - 6, Field.Rows - 1].Kind);
    }

    [Fact]
    public void Shooting_the_inchworm_slows_everything()
    {
        var g = Game();
        g.PutBug(BugKind.Inchworm, X(10), Field.CentreY(12));
        ShootFrom(g, 10);
        Assert.True(g.Slow > 0);
        Assert.Equal(100, g.Score);
    }

    [Fact]
    public void The_beetle_shot_shifts_the_garden_down_and_the_mosquito_up()
    {
        var g = Game();
        g.Grow(4, 10, PlotKind.Mushroom);
        g.PutBug(BugKind.Beetle, X(10), Field.CentreY(Field.ZoneTop - 1));
        ShootFrom(g, 10);
        Assert.Equal(PlotKind.Empty, g.Garden[4, 10].Kind);
        Assert.Equal(PlotKind.Mushroom, g.Garden[4, 11].Kind);
        g.PutBug(BugKind.Mosquito, X(10), Field.CentreY(14));
        ShootFrom(g, 10);
        Assert.Equal(PlotKind.Mushroom, g.Garden[4, 10].Kind);
    }

    [Fact]
    public void Bees_drop_down_leaving_mushrooms()
    {
        var g = Real(2);
        g.Segments.Clear();
        g.Bugs.Clear();
        g.ClearGarden();
        g.PutSegment(0, 0, 1);
        var bee = g.PutBug(BugKind.Bee, X(15), -5);
        bee.Vy = 240;
        g.PutArcher(X(2), Field.CentreY(Field.Rows - 1));
        g.Idle(200);
        int grown = 0;
        for (int r = 0; r < Field.Rows; r++) if (g.Garden[15, r].Kind == PlotKind.Mushroom) grown++;
        Assert.True(grown >= 1);
    }

    [Fact]
    public void Every_fourth_wave_is_a_swarm_each_worth_more_than_the_last()
    {
        var g = Game(8);
        Assert.Equal(BugKind.Dragonfly, g.Swarm);
        int before = g.Score;
        g.PutBug(BugKind.Dragonfly, X(10), Field.CentreY(10));
        ShootFrom(g, 10);
        g.PutBug(BugKind.Dragonfly, X(10), Field.CentreY(10));
        ShootFrom(g, 10);
        Assert.Equal(200 + 300, g.Score - before);
        Assert.Null(Game(3).Swarm);
        Assert.Equal(BugKind.Bee, Game(4).Swarm);
        Assert.Equal(BugKind.Mosquito, Game(12).Swarm);
    }

    // ───────────────────────────── losing and winning ─────────────────────────────

    [Fact]
    public void Caught_the_damaged_mushrooms_are_mended_then_back_in_and_the_last_life_ends_it()
    {
        var g = Game();
        g.SetLives(2);
        g.Grow(5, 5, PlotKind.Mushroom);
        g.Garden[5, 5].Hp = 1;
        g.Grow(6, 6, PlotKind.Flower);
        g.PutArcher(X(10), Field.CentreY(Field.Rows - 1));
        g.PutBug(BugKind.Spider, X(10), Field.CentreY(Field.Rows - 1));
        g.Idle(1);
        Assert.Equal(Phase.Dying, g.Phase);
        Assert.Equal("spider", g.Cause);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 30);
        Assert.Equal(Phase.Play, g.Phase);
        Assert.Equal(4, g.Garden[5, 5].Hp);
        Assert.Equal(PlotKind.Mushroom, g.Garden[6, 6].Kind);
        Assert.Equal(10, g.Score);
        g.PutBug(BugKind.Spider, g.Archer.X, g.Archer.Y);
        g.Idle(1);
        g.Idle((int)(GameEngine.DyingSeconds * 60) + 30);
        Assert.Equal(GameState.GameOver, g.State);
    }

    [Fact]
    public void The_millipede_gone_the_garden_shifts_down_and_the_next_wave_comes_with_new_colours()
    {
        var g = Game();
        g.Grow(3, 3, PlotKind.Mushroom);
        var colours = g.Palette;
        g.Segments.Clear();
        g.Idle(2);
        Assert.Equal(Phase.Clear, g.Phase);
        g.Idle((int)(GameEngine.ClearSeconds * 60) + 2);
        Assert.Equal(2, g.Wave);
        Assert.Equal(PlotKind.Mushroom, g.Garden[3, 4].Kind);
        Assert.NotEqual(colours, g.Palette);
        Assert.Equal(GameEngine.ChainLength, g.Segments.Count);
        Assert.Equal(1, g.Segments.Count(s => s.Prev == null) - 1);
        int ddt = 0; foreach (var p in g.Garden) if (p.Kind == PlotKind.Ddt) ddt++;
        Assert.Equal(GameEngine.Ddts, ddt);
    }

    [Fact]
    public void An_extra_life_every_15000()
    {
        var g = Game();
        int lives = g.Lives;
        g.SetScore(GameEngine.FirstExtraLife - 50);
        g.PutBug(BugKind.Earwig, X(10), Field.CentreY(10));
        ShootFrom(g, 10);
        Assert.Equal(lives + 1, g.Lives);
    }

    // ───────────────────────────── the whole game ─────────────────────────────

    [Fact]
    public void The_title_runs_a_demonstration_then_the_high_scores()
    {
        var g = new GameEngine(5, HighScoreTable.Load(TempFolder())) { Silent = true };
        g.Idle((int)(GameEngine.TitleIdleSeconds * 60) + 2);
        Assert.Equal(GameState.Attract, g.State);
        g.Idle((int)(GameEngine.AttractSeconds * 60) + 2);
        Assert.Equal(GameState.HighScores, g.State);
        g.Idle((int)(GameEngine.HighScoreSeconds * 60) + 2);
        Assert.Equal(GameState.Title, g.State);
    }

    [Fact]
    public void The_computer_archer_clears_the_first_wave()
    {
        var g = Real();
        for (int i = 0; i < 60 * 150 && g.Wave < 2 && g.State == GameState.Playing; i++) g.Tick(g.Pilot.Decide(g));
        Assert.True(g.Wave >= 2, $"still on wave {g.Wave}, {g.Segments.Count} segments left");
    }

    [Fact]
    public void The_same_seed_plays_the_same_game()
    {
        string Play()
        {
            var g = Real(2, seed: 3);
            for (int i = 0; i < 60 * 40; i++) g.Tick(g.Pilot.Decide(g));
            int mush = 0; foreach (var p in g.Garden) if (p.Blocks) mush++;
            return $"{g.Score} {g.Wave} {g.Lives} {g.Archer.X:0.00} {g.Segments.Count} {mush} {g.Phase}";
        }
        Assert.Equal(Play(), Play());
    }

    [Fact]
    public void Pausing_stops_everything()
    {
        var g = Real();
        g.Run(30, new Controls(Dx: 1));
        double x = g.Archer.X;
        g.Pause();
        g.Idle(120);
        Assert.Equal(x, g.Archer.X);
        g.Resume();
        Assert.Equal(GameState.Playing, g.State);
    }
}
