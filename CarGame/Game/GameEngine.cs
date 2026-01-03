using Microsoft.Maui.Storage;

namespace CarGame.Game;

public sealed class GameEngine
{
    public GameState State { get; } = new();

    // Raised when the player collects a coin (used for sound effects/UI)
    public event Action? CoinCollected;

    // Raised when the player takes damage (loses a life but is still alive)
    public event Action? PlayerDamaged;

    // Raised when the player dies (lives reach 0)
    public event Action? PlayerDied;

    private readonly Random _rng = new();

    private double _enemySpawnT;
    private double _coinSpawnT;
    private double _fuelSpawnT;

    // Minimum vertical spacing between spawned objects (enemies/coins/fuel).
    // Increase this if things feel too "stacked".
    private const double MinSpawnGapY = 240;
    private const double SpawnTopBufferY = 450;

    // Prevents an ever-growing "invisible queue" of spawns far above the top.
    // If there are already lots of entities waiting above the screen, we delay rare spawns (like fuel).
    private const int MaxQueuedAboveTop = 8;

    private int CountQueuedAboveTop()
    {
        int count = 0;
        for (int i = 0; i < State.Entities.Count; i++)
        {
            if (State.Entities[i].Y < 0) count++;
        }
        return count;
    }

    // prevents losing multiple lives in a single overlap
    private double _hitCooldown;

    public GameEngine() => Reset();

    public void Reset()
    {
        // Preserve player selection across resets
        var selectedCar = State.SelectedCarSprite;

        State.Entities.Clear();

        State.Lives = 3;
        State.IsGameOver = false;
        State.IsPaused = false;

        State.ScorePrecise = 0;
        State.CoinsThisRun = 0;
        State.BgScroll = 0;

        State.ScrollSpeed = 520;
        State.PointsPerSecond = 5;

        State.Player.TargetLane = 1;

        _enemySpawnT = 0.6;
        _coinSpawnT = 1.2;
        _fuelSpawnT = 12.0;

        _hitCooldown = 0;

        // Load persisted high score
        State.HighScore = Preferences.Default.Get("highscore", 0);
        State.IsNewHighScore = false;

        // Restore selection (fallback to default)
        State.SelectedCarSprite = string.IsNullOrWhiteSpace(selectedCar) ? "yellowcar.png" : selectedCar;
    }

    public void TryMovePlayerLane(int delta)
    {
        if (State.IsGameOver) return;
        State.Player.TargetLane = Math.Clamp(State.Player.TargetLane + delta, 0, 2);
    }

    public void Resize(double width, double height)
    {
        // Avoid thrashing if size hasn't really changed
        if (Math.Abs(State.ViewWidth - width) < 0.5 && Math.Abs(State.ViewHeight - height) < 0.5)
            return;

        State.ViewWidth = width;
        State.ViewHeight = height;
        State.LaneWidth = width / 3.0;

        // Size player relative to lane width, but cap it so it doesn't get huge on wide windows.
        var desiredW = State.LaneWidth * 0.58;
        var maxW = State.ViewHeight * 0.22; // looks reasonable on both phone + desktop

        State.Player.Width = Math.Min(desiredW, maxW);
        State.Player.Height = State.Player.Width * 1.6;

        // Place player near bottom
        State.Player.Y = height - State.Player.Height - 100;

        // Snap to lane on resize
        var targetX = LaneCenterX(State.Player.TargetLane) - State.Player.Width / 2;
        State.Player.X = targetX;
    }

    private double LaneCenterX(int lane) => State.LaneWidth * (lane + 0.5);

    private double GetSpawnY(double entityHeight)
    {
        // Spawn above the top of the screen to give reaction time...
        var y = -SpawnTopBufferY - entityHeight;

        // ...but if there are already entities queued above the top (negative Y),
        // push the new spawn further up to keep a minimum gap between sprites.
        double minY = double.PositiveInfinity;
        for (int i = 0; i < State.Entities.Count; i++)
        {
            var e = State.Entities[i];
            if (e.Y < 0 && e.Y < minY) minY = e.Y;
        }

        if (minY != double.PositiveInfinity)
        {
            var spacedY = minY - MinSpawnGapY - entityHeight;
            if (spacedY < y) y = spacedY;
        }

        return y;
    }

    public void Update(double dt)
    {
        if (dt <= 0) return;
        if (State.ViewWidth <= 0 || State.ViewHeight <= 0) return;
        if (State.IsGameOver) return;
        if (State.IsPaused) return;

        // Score increases the longer you survive
        State.ScorePrecise += dt * State.PointsPerSecond;

        // Background scroll (lane dashes)
        State.BgScroll += State.ScrollSpeed * dt;

        // Optional gentle difficulty ramp
        State.ScrollSpeed += dt * 2.0;

        // Hit cooldown
        _hitCooldown = Math.Max(0, _hitCooldown - dt);

        // Smooth lane movement
        var targetX = LaneCenterX(State.Player.TargetLane) - State.Player.Width / 2;
        State.Player.X = Lerp(State.Player.X, targetX, 18 * dt);

        // Spawn timers
        _enemySpawnT -= dt;
        _coinSpawnT -= dt;
        _fuelSpawnT -= dt;

        if (_enemySpawnT <= 0)
        {
            SpawnEnemy();
            _enemySpawnT = RandomRange(0.55, 1.05);
        }

        if (_coinSpawnT <= 0)
        {
            SpawnCoin();
            _coinSpawnT = RandomRange(0.9, 1.7);
        }

        if (_fuelSpawnT <= 0)
        {
            // Rare fuel can: spawn it even at full lives so you can actually see it.
            // If the spawn queue above the screen is too large, retry sooner instead of pushing it miles up.
            if (CountQueuedAboveTop() < MaxQueuedAboveTop)
            {
                SpawnFuel();
                _fuelSpawnT = RandomRange(12.0, 20.0);
            }
            else
            {
                _fuelSpawnT = 3.0; // retry soon
            }
        }

        // Move entities downward
        for (int i = State.Entities.Count - 1; i >= 0; i--)
        {
            var e = State.Entities[i];
            e.Y += State.ScrollSpeed * dt;

            // Remove off-screen
            if (e.Y > State.ViewHeight + 250)
                State.Entities.RemoveAt(i);
        }

        // Collisions
        for (int i = State.Entities.Count - 1; i >= 0; i--)
        {
            var e = State.Entities[i];
            if (!Intersects(State.Player.X, State.Player.Y, State.Player.Width, State.Player.Height,
                            e.X, e.Y, e.Width, e.Height))
                continue;

            switch (e.Kind)
            {
                case EntityKind.Enemy:
                    if (_hitCooldown <= 0)
                    {
                        State.Entities.RemoveAt(i);
                        State.Lives--;
                        _hitCooldown = 0.5;

                        if (State.Lives <= 0)
                        {
                            PlayerDied?.Invoke();
                            State.IsGameOver = true;

                            // High score save
                            if (State.Score > State.HighScore)
                            {
                                State.HighScore = State.Score;
                                State.IsNewHighScore = true;
                                Preferences.Default.Set("highscore", State.HighScore);
                            }
                        }
                        else
                        {
                            PlayerDamaged?.Invoke();
                        }
                    }
                    break;

                case EntityKind.Coin:
                    State.Entities.RemoveAt(i);
                    State.ScorePrecise += 10; // +10 per coin
                    State.CoinsThisRun += 1;
                    CoinCollected?.Invoke();
                    break;

                case EntityKind.Fuel:
                    State.Entities.RemoveAt(i);
                    State.Lives = Math.Min(3, State.Lives + 1);
                    break;
            }
        }
    }

    private void SpawnEnemy()
    {
        int lane = _rng.Next(0, 3);
        var e = Entity.Make(EntityKind.Enemy, LaneCenterX(lane), 0, State);
        e.Y = GetSpawnY(e.Height);
        State.Entities.Add(e);
    }

    private void SpawnCoin()
    {
        int lane = _rng.Next(0, 3);
        var e = Entity.Make(EntityKind.Coin, LaneCenterX(lane), 0, State);
        e.Y = GetSpawnY(e.Height);
        State.Entities.Add(e);
    }

    private void SpawnFuel()
    {
        int lane = _rng.Next(0, 3);
        var e = Entity.Make(EntityKind.Fuel, LaneCenterX(lane), 0, State);
        e.Y = GetSpawnY(e.Height);
        State.Entities.Add(e);
    }

    private static bool Intersects(double ax, double ay, double aw, double ah,
                                   double bx, double by, double bw, double bh)
    {
        return ax < bx + bw &&
               ax + aw > bx &&
               ay < by + bh &&
               ay + ah > by;
    }

    private static double RandomRange(double a, double b)
        => a + Random.Shared.NextDouble() * (b - a);

    private static double Lerp(double a, double b, double t)
        => a + (b - a) * Math.Clamp(t, 0, 1);
}
