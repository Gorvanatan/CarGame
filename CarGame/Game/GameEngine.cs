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

    // Raised when invincibility starts/ends (used for music/UI)
    public event Action? InvincibilityStarted;
    public event Action? InvincibilityEnded;

    // Raised when the player collects a fuel can (used for sound effects/UI)
    public event Action? FuelCollected;

    private readonly Random _rng = new();

    private double _enemySpawnT;
    private double _coinSpawnT;
    private double _fuelSpawnT;
    private double _starSpawnT;
    private double _treeSpawnT;

    // --- Spawn rules (no "queue" above the screen) ---
    // Caps prevent the game from flooding the screen if timers get small.
    private const int MaxEnemiesActive = 2;
    private const int MaxCoinsActive = 3;
    private const int MaxFuelActive = 1;
    private const int MaxStarsActive = 1;
    private const int MaxTreesActive = 4;

    // Minimum spacing for ENEMIES per lane: don't spawn another enemy in a lane
    // if an enemy in that lane is still within this many pixels of the top.
    private const double EnemyMinGapFromTop = 250;

    // prevents losing multiple lives in a single overlap
    private double _hitCooldown;

    // Persisted upgrade keys (set by Shop)
    private const string PrefMaxHealth = "max_health"; // int (default 3)
    private const string PrefInvincibilityDurationSeconds = "invincibility_duration_seconds"; // int (default 6)

    public GameEngine() => Reset();

    public void Reset()
    {
        // Preserve player selection across resets
        var selectedCar = State.SelectedCarSprite;

        State.Entities.Clear();

        // Load upgrades
        State.MaxLives = Math.Clamp(Preferences.Default.Get(PrefMaxHealth, 3), 3, 6);
        // Base = 6s, upgradable in +2s steps up to 12s.
        State.InvincibilityDuration = Math.Clamp(Preferences.Default.Get(PrefInvincibilityDurationSeconds, 6), 6, 12);

        State.Lives = State.MaxLives;
        State.IsGameOver = false;
        State.IsPaused = false;

        State.ScorePrecise = 0;
        State.CoinsThisRun = 0;
        State.TimeAlive = 0;
        State.BgScroll = 0;

        State.IsInvincible = false;
        State.InvincibleRemaining = 0;

        // Start slower to give the player more time to react when cars appear.
        // Difficulty still ramps up over time in Update().
        State.ScrollSpeed = 100;
        State.PointsPerSecond = 5;

        State.Player.TargetLane = 1;

        _enemySpawnT = 5.0;
        _coinSpawnT = 6.0;
        _fuelSpawnT = 12.0;
        _starSpawnT = 16.0;
        _treeSpawnT = 2.5;

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

        // Road is centered with grass shoulders on both sides.
        // Shoulder size is a % of screen width, clamped so it looks good on phone + desktop.
        State.ShoulderWidth = Math.Clamp(width * 0.18, 40, width * 0.28);
        State.RoadLeft = State.ShoulderWidth;
        State.RoadWidth = Math.Max(0, width - (State.ShoulderWidth * 2));
        State.LaneWidth = State.RoadWidth / 3.0;

        // Size player relative to lane width, but cap it so it doesn't get huge on wide windows.
        var desiredW = State.LaneWidth * 0.24;
        var maxW = State.ViewHeight * 0.11; // looks reasonable on both phone + desktop

        State.Player.Width = Math.Min(desiredW, maxW);
        State.Player.Height = State.Player.Width * 1.6;

        // Place player near bottom
        // Place player a bit lower to increase the visible reaction distance.
        State.Player.Y = height - State.Player.Height - 40;

        // Snap to lane on resize
        var targetX = LaneCenterX(State.Player.TargetLane) - State.Player.Width / 2;
        State.Player.X = targetX;
    }

    private double LaneCenterX(int lane) => State.RoadLeft + (State.LaneWidth * (lane + 0.5));

    private static double SpawnAtTopY(double entityHeight)
        => -entityHeight - 10;

    private int ActiveCount(EntityKind kind)
    {
        int count = 0;
        for (int i = 0; i < State.Entities.Count; i++)
        {
            if (State.Entities[i].Kind == kind) count++;
        }
        return count;
    }

    private int LaneFromEntity(Entity e)
    {
        var cx = e.X + e.Width / 2.0;
        if (State.LaneWidth <= 0) return 1;
        return Math.Clamp((int)(cx / State.LaneWidth), 0, 2);
    }

    private bool EnemyLaneHasSpace(int lane)
    {
        // If there is any enemy in this lane still near the top, block spawns.
        for (int i = 0; i < State.Entities.Count; i++)
        {
            var e = State.Entities[i];
            if (e.Kind != EntityKind.Enemy) continue;
            if (LaneFromEntity(e) != lane) continue;
            if (e.Y < EnemyMinGapFromTop) return false;
        }
        return true;
    }

    private bool IsAreaClear(double x, double y, double w, double h, double padding)
    {
        // Expand the spawn rectangle slightly so items don't appear "on top" of each other.
        var ax = x - padding;
        var ay = y - padding;
        var aw = w + padding * 2;
        var ah = h + padding * 2;

        for (int i = 0; i < State.Entities.Count; i++)
        {
            var e = State.Entities[i];
            if (Intersects(ax, ay, aw, ah, e.X, e.Y, e.Width, e.Height))
                return false;
        }
        return true;
    }

    private static void Shuffle(Span<int> lanes, Random rng)
    {
        for (int i = lanes.Length - 1; i > 0; i--)
        {
            int j = rng.Next(0, i + 1);
            (lanes[i], lanes[j]) = (lanes[j], lanes[i]);
        }
    }

    private bool TrySpawnInAnyLane(EntityKind kind, double padding)
    {
        Span<int> lanes = stackalloc int[3] { 0, 1, 2 };
        Shuffle(lanes, _rng);

        for (int i = 0; i < lanes.Length; i++)
        {
            int lane = lanes[i];
            var e = Entity.Make(kind, LaneCenterX(lane), 0, State);
            e.Y = SpawnAtTopY(e.Height);

            if (!IsAreaClear(e.X, e.Y, e.Width, e.Height, padding))
                continue;

            State.Entities.Add(e);
            return true;
        }

        return false;
    }

    public void Update(double dt)
    {
        if (dt <= 0) return;
        if (State.ViewWidth <= 0 || State.ViewHeight <= 0) return;
        if (State.IsGameOver) return;
        if (State.IsPaused) return;

        // Convert "screen speed" into world speed when the renderer is zoomed out.
        // This keeps the on-screen motion feeling consistent while showing more road.
        var scale = Math.Clamp(State.RenderScale, 0.1, 2.0);
        var worldSpeed = State.ScrollSpeed / scale;

        // Time alive
        State.TimeAlive += dt;

        // Score increases the longer you survive
        State.ScorePrecise += dt * State.PointsPerSecond;

        // Background scroll (lane dashes)
        State.BgScroll += worldSpeed * dt;

        // Difficulty ramp (acceleration). Starts slow so you have more reaction time,
        // but speeds up the longer you survive.
        State.ScrollSpeed += dt * 6.5;

        // Invincibility countdown
        if (State.IsInvincible)
        {
            State.InvincibleRemaining -= dt;
            if (State.InvincibleRemaining <= 0)
            {
                State.IsInvincible = false;
                State.InvincibleRemaining = 0;
                InvincibilityEnded?.Invoke();
            }
        }

        // Hit cooldown
        _hitCooldown = Math.Max(0, _hitCooldown - dt);

        // Smooth lane movement
        var targetX = LaneCenterX(State.Player.TargetLane) - State.Player.Width / 2;
        State.Player.X = Lerp(State.Player.X, targetX, 18 * dt);

        // Spawn timers
        _enemySpawnT -= dt;
        _coinSpawnT -= dt;
        _fuelSpawnT -= dt;
        _starSpawnT -= dt;
        _treeSpawnT -= dt;

        if (_enemySpawnT <= 0)
        {
            // --- Enemies ---
            // Cap total active enemies and enforce per-lane spacing near the top.
            if (ActiveCount(EntityKind.Enemy) < MaxEnemiesActive && SpawnEnemy())
            {
                // With the slower starting speed, slightly reduce spawn frequency
                // so the screen doesn't feel overly busy.
                _enemySpawnT = RandomRange(0.75, 1.35);
            }
            else
            {
                // Couldn't spawn due to caps/spacing — retry soon.
                _enemySpawnT = 0.25;
            }
        }

if (_coinSpawnT <= 0)
{
    // --- Coins ---
    if (ActiveCount(EntityKind.Coin) < MaxCoinsActive && SpawnCoin())
    {
        _coinSpawnT = RandomRange(1.0, 1.8);
    }
    else
    {
        // Lanes blocked / cap reached — retry soon.
        _coinSpawnT = 0.35;
    }
}

if (_fuelSpawnT <= 0)
{
    // --- Fuel ---
    if (ActiveCount(EntityKind.Fuel) < MaxFuelActive && SpawnFuel())
    {
        _fuelSpawnT = RandomRange(12.0, 20.0);
    }
    else
    {
        _fuelSpawnT = 1.0;
    }
}

if (_starSpawnT <= 0)
{
    // --- Star (invincibility) ---
    if (ActiveCount(EntityKind.Star) < MaxStarsActive && SpawnStar())
    {
        _starSpawnT = RandomRange(18.0, 32.0);
    }
    else
    {
        _starSpawnT = 1.0;
    }
}

        if (_treeSpawnT <= 0)
        {
            // --- Trees (background decoration on the grass shoulders) ---
            if (ActiveCount(EntityKind.Tree) < MaxTreesActive && SpawnTree())
                _treeSpawnT = RandomRange(1.4, 2.6);
            else
                _treeSpawnT = 0.6;
        }


        // Move entities downward
        for (int i = State.Entities.Count - 1; i >= 0; i--)
        {
            var e = State.Entities[i];
            e.Y += worldSpeed * dt;

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
                case EntityKind.Tree:
                    // Decoration: no gameplay effect
                    break;
                case EntityKind.Enemy:
                    // While invincible, you can plow through enemies.
                    if (State.IsInvincible)
                    {
                        State.Entities.RemoveAt(i);
                        // Small reward for hitting an enemy while invincible
                        State.ScorePrecise += 5;
                        break;
                    }

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
                    State.Lives = Math.Min(State.MaxLives, State.Lives + 1);
                    FuelCollected?.Invoke();
                    break;

                case EntityKind.Star:
                    State.Entities.RemoveAt(i);
                    // Invincibility duration can be upgraded
                    State.IsInvincible = true;
                    State.InvincibleRemaining = Math.Max(0.1, State.InvincibilityDuration);
                    // Fire event even if refreshed so music restarts cleanly
                    InvincibilityStarted?.Invoke();
                    break;
            }
        }
    }

private bool SpawnEnemy()
{
    // Try each lane (shuffled) and choose the first one that satisfies:
    // 1) lane spacing rule for enemies, and
    // 2) does not overlap any existing entity at the spawn area.
    Span<int> lanes = stackalloc int[3] { 0, 1, 2 };
    Shuffle(lanes, _rng);

    for (int i = 0; i < lanes.Length; i++)
    {
        int lane = lanes[i];
        if (!EnemyLaneHasSpace(lane)) continue;

        var e = Entity.Make(EntityKind.Enemy, LaneCenterX(lane), 0, State);
        e.Y = SpawnAtTopY(e.Height);

        if (!IsAreaClear(e.X, e.Y, e.Width, e.Height, padding: 20))
            continue;

        State.Entities.Add(e);
        return true;
    }

    return false;
}

private bool SpawnCoin()
{
    // Coins are smaller, but still avoid overlaps with other spawns near the top.
    return TrySpawnInAnyLane(EntityKind.Coin, padding: 18);
}

private bool SpawnFuel()
{
    // Fuel is rarer/bigger — use more padding so it doesn't clip into other items.
    return TrySpawnInAnyLane(EntityKind.Fuel, padding: 24);
}

private bool SpawnStar()
{
    // Star is rare and should feel "clean" when it appears.
    return TrySpawnInAnyLane(EntityKind.Star, padding: 26);
}

    private bool SpawnTree()
    {
        // Trees are purely visual, spawned on the grass shoulders (left/right of the road).
        if (State.ShoulderWidth <= 8) return false;

        // Size is based on shoulder width, with a cap so it doesn't get huge on desktop.
        var maxW = Math.Min(State.ShoulderWidth * 0.75, State.ViewHeight * 0.18);
        var minW = Math.Min(State.ShoulderWidth * 0.45, State.ViewHeight * 0.12);
        var w = RandomRange(minW, maxW);
        var h = w; // tree sprite is roughly square

        var y = SpawnAtTopY(h);

        bool leftSide = _rng.NextDouble() < 0.5;
        double minX, maxX;

        if (leftSide)
        {
            minX = 6;
            maxX = Math.Max(minX, State.RoadLeft - w - 6);
        }
        else
        {
            minX = State.RoadRight + 6;
            maxX = Math.Max(minX, State.ViewWidth - w - 6);
        }

        var x = RandomRange(minX, maxX);

        // Avoid spawning on top of other entities (mainly other trees at the top).
        if (!IsAreaClear(x, y, w, h, padding: 12))
            return false;

        State.Entities.Add(new Entity
        {
            Kind = EntityKind.Tree,
            X = x,
            Y = y,
            Width = w,
            Height = h
        });

        return true;
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
