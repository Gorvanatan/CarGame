namespace CarGame.Game;

public sealed class GameState
{
    // Render scale ("camera zoom"). 1.0 = normal. < 1.0 = zoomed out (more road visible).
    // Set by the renderer (GameDrawable) so the engine can keep speeds feeling consistent.
    public double RenderScale { get; set; } = 1.0;

    // Selected player car sprite (filename in Resources/Raw)
    public string SelectedCarSprite { get; set; } = "yellowcar.png";

    // View size
    public double ViewWidth { get; set; }
    public double ViewHeight { get; set; }
    public double LaneWidth { get; set; }

    // Gameplay
    // MaxLives can be upgraded (persisted via Preferences and loaded by the engine)
    public int MaxLives { get; set; } = 3;
    public int Lives { get; set; } = 3;
    public bool IsGameOver { get; set; } = false;
    public bool IsPaused { get; set; } = false;

    // Score
    public double ScorePrecise { get; set; } = 0; // smooth scoring
    public int Score => (int)ScorePrecise;
    public double PointsPerSecond { get; set; } = 5;

    // Coins collected in the current run (used for unlockables / total coins)
    public int CoinsThisRun { get; set; } = 0;

    // High score (persisted)
    public int HighScore { get; set; } = 0;
    public bool IsNewHighScore { get; set; } = false;

    // Background scroll (for dashed lane lines)
    public double BgScroll { get; set; } = 0;

    // Invincibility
    public bool IsInvincible { get; set; } = false;
    public double InvincibleRemaining { get; set; } = 0;
    public int InvincibleSecondsLeft => (int)Math.Ceiling(Math.Max(0, InvincibleRemaining));

    // Default invincibility duration when collecting a star (can be upgraded)
    public double InvincibilityDuration { get; set; } = 10.0;

    // World speed (pixels/sec)
    public double ScrollSpeed { get; set; } = 520;

    public PlayerCar Player { get; } = new();
    public List<Entity> Entities { get; } = new();

    public string LivesText => new string('❤', Math.Max(0, Lives));
}
