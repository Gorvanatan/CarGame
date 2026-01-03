namespace CarGame.Game;

public sealed class GameState
{
    // Selected player car sprite (filename in Resources/Raw)
    public string SelectedCarSprite { get; set; } = "yellowcar.png";

    // View size
    public double ViewWidth { get; set; }
    public double ViewHeight { get; set; }
    public double LaneWidth { get; set; }

    // Gameplay
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

    // World speed (pixels/sec)
    public double ScrollSpeed { get; set; } = 520;

    public PlayerCar Player { get; } = new();
    public List<Entity> Entities { get; } = new();

    public string LivesText => new string('❤', Math.Max(0, Lives));
}
