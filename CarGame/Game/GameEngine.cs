namespace CarGame.Game;

public sealed class GameEngine
{
    public GameState State { get; } = new();

    private readonly Random _rng = new();

    private double _enemySpawnT;
    private double _coinSpawnT;
    private double _fuelSpawnT;

    // prevents losing multiple lives in a single overlap
    private double _hitCooldown;

    public GameEngine() => Reset();

    public void Reset()
    {
        State.Entities.Clear();

        State.Lives = 3;
        State.IsGameOver = false;

        State.ScorePrecise = 0;
        State.BgScroll = 0;

        State.ScrollSpeed = 520;
        State.PointsPerSecond = 5;

        State.Player.TargetLane = 1;

        _enemySpawnT = 0.6;
        _coinSpawnT = 1.2;
        _fuelSpawnT = 12.0;

        _hitCooldown = 0;
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

        // Size player relative to lane width
        State.Player.Width = State.LaneWidth * 0.58;
        State.Player.Height = State.Player.Width * 1.6;

        // Place player near bottom
        State.Player.Y = height - State.Player.Height - 30;

        // Snap to lane on resize
        var targetX = LaneCenterX(State.Player.TargetLane) - State.Player.Width / 2;
        State.Player.X = targetX;
    }

    private double LaneCenterX(int lane) => State.LaneWidth * (lane + 0.5);

    public void Update(double dt)
    {
        if (dt <= 0) return;
        if (State.ViewWidth <= 0 || State.ViewHeight <= 0) return;
        if (State.IsGameOver) return;

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
            // Rare heart restore; only useful if you're below max lives
            if (State.Lives < 3) SpawnFuel();
            _fuelSpawnT = RandomRange(12.0, 20.0);
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
                            State.IsGameOver = true;
                    }
                    break;

                case EntityKind.Coin:
                    State.Entities.RemoveAt(i);
                    State.ScorePrecise += 10; // +10 per coin
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
        State.Entities.Add(Entity.Make(EntityKind.Enemy, LaneCenterX(lane), -160, State));
    }

    private void SpawnCoin()
    {
        int lane = _rng.Next(0, 3);
        State.Entities.Add(Entity.Make(EntityKind.Coin, LaneCenterX(lane), -120, State));
    }

    private void SpawnFuel()
    {
        int lane = _rng.Next(0, 3);
        State.Entities.Add(Entity.Make(EntityKind.Fuel, LaneCenterX(lane), -150, State));
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
