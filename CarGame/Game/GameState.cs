namespace CarGame.Game;

public sealed class GameState
{
    // render scale ("camera zoom"). 1.0 = normal. < 1.0 = zoomed out (more road visible).
    // set by the renderer (GameDrawable) so the engine can keep speeds feeling consistent.
    public double RenderScale { get; set; } = 1.0;

    // selected player car sprite (filename in Resources/Raw)
    public string SelectedCarSprite { get; set; } = "yellowcar.png";

    // view size
    public double ViewWidth { get; set; }
    public double ViewHeight { get; set; }
    public double LaneWidth { get; set; }

    // road layout (road is centered, grass shoulders on both sides)
    public double RoadLeft { get; set; }
    public double RoadWidth { get; set; }
    public double ShoulderWidth { get; set; }
    public double RoadRight => RoadLeft + RoadWidth;

    // gameplay
    // maxLives can be upgraded (persisted via Preferences and loaded by the engine)
    public int MaxLives { get; set; } = 3;
    public int Lives { get; set; } = 3;
    public bool IsGameOver { get; set; } = false;
    public bool IsPaused { get; set; } = false;

    // score
    public double ScorePrecise { get; set; } = 0; // smooth scoring
    public int Score => (int)ScorePrecise;
    public double PointsPerSecond { get; set; } = 5;

    // coins collected in the current run (used for unlockables / total coins)
    public int CoinsThisRun { get; set; } = 0;

    // time alive in the current run (seconds)
    public double TimeAlive { get; set; } = 0;

    // high score (persisted)
    public int HighScore { get; set; } = 0;
    public bool IsNewHighScore { get; set; } = false;

    // background scroll (for dashed lane lines)
    public double BgScroll { get; set; } = 0;

    // invincibility
    public bool IsInvincible { get; set; } = false;
    public double InvincibleRemaining { get; set; } = 0;
    public int InvincibleSecondsLeft => (int)Math.Ceiling(Math.Max(0, InvincibleRemaining));

    // default invincibility duration when collecting a star (can be upgraded)
    // base = 6s, upgradable in +2s steps (6 -> 8 -> 10 -> 12)
    public double InvincibilityDuration { get; set; } = 6.0;

    // world speed (pixels/sec)
    public double ScrollSpeed { get; set; } = 520;

    public PlayerCar Player { get; } = new();
    public List<Entity> Entities { get; } = new();

    public string LivesText => new string('❤', Math.Max(0, Lives));
}
