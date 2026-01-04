using Microsoft.Maui.Storage;

namespace CarGame.Game;

public sealed class GameEngine
{
    public GameState State { get; } = new();

    // raised when the player collects a coin (used for sound effects/ui)
    public event Action? CoinCollected;

    // raised when the player takes damage (loses a life but is still alive)
    public event Action? PlayerDamaged;

    // raised when the player dies (lives reach 0)
    public event Action? PlayerDied;

    // raised when invincibility starts/ends (used for music/ui)
    public event Action? InvincibilityStarted;
    public event Action? InvincibilityEnded;

    // raised when the player collects a fuel can (used for sound effects/ui)
    public event Action? FuelCollected;

    private readonly Random _rng = new();

    private double _enemySpawnT;
    private double _coinSpawnT;
    private double _fuelSpawnT;
    private double _starSpawnT;
    private double _treeSpawnT;

    // --- Spawn rules (no "queue" above the screen) ---
    // caps prevent the game from flooding the screen if timers get small.
    private const int MaxEnemiesActive = 2;
    private const int MaxCoinsActive = 3;
    private const int MaxFuelActive = 1;
    private const int MaxStarsActive = 1;
    private const int MaxTreesActive = 4;

    // minimum spacing for ENEMIES per lane: don't spawn another enemy in a lane
    // if an enemy in that lane is still within this many pixels of the top.
    private const double EnemyMinGapFromTop = 250;

    // prevents losing multiple lives in a single overlap
    private double _hitCooldown;

    // persisted upgrade keys (set by Shop)
    private const string PrefMaxHealth = "max_health"; // int (default 3)
    private const string PrefInvincibilityDurationSeconds = "invincibility_duration_seconds"; // int (default 6)

    public GameEngine() => Reset();

    public void Reset()
    {
        // preserve player selection across resets
        var selectedCar = State.SelectedCarSprite;

        State.Entities.Clear();

        // load upgrades
        State.MaxLives = Math.Clamp(Preferences.Default.Get(PrefMaxHealth, 3), 3, 6);
        // base = 6s, upgradable in +2s steps up to 12s
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

        // start slower to give the player more time to react when cars appear
        // difficulty still ramps up over time in Update()
        State.ScrollSpeed = 100;
        State.PointsPerSecond = 5;

        State.Player.TargetLaneIndex = 1;

        _enemySpawnT = 5.0;
        _coinSpawnT = 6.0;
        _fuelSpawnT = 12.0;
        _starSpawnT = 16.0;
        _treeSpawnT = 2.5;

        _hitCooldown = 0;

        // load persisted high score
        State.HighScore = Preferences.Default.Get("highscore", 0);
        State.IsNewHighScore = false;

        // restore selection (fallback to default)
        State.SelectedCarSprite = string.IsNullOrWhiteSpace(selectedCar) ? "yellowcar.png" : selectedCar;
    }

    public void TryMovePlayerLane(int delta)
    {
        if (State.IsGameOver) return;
        State.Player.TargetLaneIndex = Math.Clamp(State.Player.TargetLaneIndex + delta, 0, 2);
    }

    public void Resize(double width, double height)
    {
        // avoid thrashing if size hasn't really changed
        if (Math.Abs(State.ViewWidth - width) < 0.5 && Math.Abs(State.ViewHeight - height) < 0.5)
            return;

        State.ViewWidth = width;
        State.ViewHeight = height;

        // road is centered with grass shoulders on both sides
        // shoulder size is a % of screen width, clamped so it looks good on phone + desktop
        State.ShoulderWidth = Math.Clamp(width * 0.18, 40, width * 0.28);
        State.RoadLeft = State.ShoulderWidth;
        State.RoadWidth = Math.Max(0, width - (State.ShoulderWidth * 2));
        State.LaneWidth = State.RoadWidth / 3.0;

        // size player relative to lane width, but cap it so it does not get huge on wide windows
        var desiredPlayerWidth = State.LaneWidth * 0.24;
        var maxPlayerWidth = State.ViewHeight * 0.11; // looks reasonable on both phone + desktop

        State.Player.Width = Math.Min(desiredPlayerWidth, maxPlayerWidth);
        State.Player.Height = State.Player.Width * 1.6;

        // place player a bit lower to increase the visible reaction distance
        State.Player.PositionY = height - State.Player.Height - 40;

        // snap to lane on resize
        var targetLaneCenterX = LaneCenterX(State.Player.TargetLaneIndex);
        var targetPlayerX = targetLaneCenterX - (State.Player.Width / 2.0);
        State.Player.PositionX = targetPlayerX;
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

    private int LaneFromEntity(Entity entity)
    {
        // use the entity center point to decide which lane it belongs to
        var entityCenterX = entity.PositionX + entity.Width / 2.0;
        if (State.LaneWidth <= 0) return 1;
        return Math.Clamp((int)(entityCenterX / State.LaneWidth), 0, 2);
    }

    private bool EnemyLaneHasSpace(int lane)
    {
        // if there is any enemy in this lane still near the top, block spawns
        for (int i = 0; i < State.Entities.Count; i++)
        {
            var entity = State.Entities[i];
            if (entity.Kind != EntityKind.Enemy) continue;
            if (LaneFromEntity(entity) != lane) continue;
            if (entity.PositionY < EnemyMinGapFromTop) return false;
        }
        return true;
    }

    private bool IsAreaClear(double areaX, double areaY, double areaWidth, double areaHeight, double padding)
    {
        // expand the spawn rectangle slightly so items do not appear "on top" of each other
        var expandedX = areaX - padding;
        var expandedY = areaY - padding;
        var expandedWidth = areaWidth + padding * 2;
        var expandedHeight = areaHeight + padding * 2;

        for (int i = 0; i < State.Entities.Count; i++)
        {
            var entity = State.Entities[i];
            if (Intersects(expandedX, expandedY, expandedWidth, expandedHeight, entity.PositionX, entity.PositionY, entity.Width, entity.Height))
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
            var entity = Entity.Create(kind, LaneCenterX(lane), 0, State);
            entity.PositionY = SpawnAtTopY(entity.Height);

            if (!IsAreaClear(entity.PositionX, entity.PositionY, entity.Width, entity.Height, padding))
                continue;

            State.Entities.Add(entity);
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

        // convert "screen speed" into world speed when the renderer is zoomed out
        // this keeps the on-screen motion feeling consistent while showing more road
        var scale = Math.Clamp(State.RenderScale, 0.1, 2.0);
        var worldSpeed = State.ScrollSpeed / scale;

        // track time alive
        State.TimeAlive += dt;

        // score increases the longer you survive
        State.ScorePrecise += dt * State.PointsPerSecond;

        // update background scroll (lane dashes)
        State.BgScroll += worldSpeed * dt;

        // difficulty ramp (acceleration) starts slow so you have more reaction time
        // but speeds up the longer you survive
        State.ScrollSpeed += dt * 6.5;

        // invincibility countdown
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

        // hit cooldown
        _hitCooldown = Math.Max(0, _hitCooldown - dt);

        // smooth lane movement
        var targetLaneCenterX = LaneCenterX(State.Player.TargetLaneIndex);
        var targetPlayerX = targetLaneCenterX - (State.Player.Width / 2.0);
        State.Player.PositionX = Lerp(State.Player.PositionX, targetPlayerX, 18 * dt);

        // spawn timers
        _enemySpawnT -= dt;
        _coinSpawnT -= dt;
        _fuelSpawnT -= dt;
        _starSpawnT -= dt;
        _treeSpawnT -= dt;

        if (_enemySpawnT <= 0)
        {
            // --- Enemies ---
            // cap total active enemies and enforce per-lane spacing near the top.
            if (ActiveCount(EntityKind.Enemy) < MaxEnemiesActive && SpawnEnemy())
            {
                // with the slower starting speed, slightly reduce spawn frequency
                // so the screen doesn't feel overly busy.
                _enemySpawnT = RandomRange(0.75, 1.35);
            }
            else
            {
                // couldn't spawn due to caps/spacing — retry soon.
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
        // lanes blocked / cap reached — retry soon.
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


        // move entities downward
        for (int i = State.Entities.Count - 1; i >= 0; i--)
        {
            var entity = State.Entities[i];
            entity.PositionY += worldSpeed * dt;

            // remove off-screen
            if (entity.PositionY > State.ViewHeight + 250)
                State.Entities.RemoveAt(i);
        }

        // collisions
        for (int i = State.Entities.Count - 1; i >= 0; i--)
        {
            var entity = State.Entities[i];
            if (!Intersects(State.Player.PositionX, State.Player.PositionY, State.Player.Width, State.Player.Height,
                            entity.PositionX, entity.PositionY, entity.Width, entity.Height))
                continue;

            switch (entity.Kind)
            {
                case EntityKind.Tree:
                    // decoration: no gameplay effect
                    break;
                case EntityKind.Enemy:
                    // while invincible, you can plow through enemies.
                    if (State.IsInvincible)
                    {
                        State.Entities.RemoveAt(i);
                        // small reward for hitting an enemy while invincible
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

                            // high score save
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
                    // invincibility duration can be upgraded
                    State.IsInvincible = true;
                    State.InvincibleRemaining = Math.Max(0.1, State.InvincibilityDuration);
                    // fire event even if refreshed so music restarts cleanly
                    InvincibilityStarted?.Invoke();
                    break;
            }
        }
    }

private bool SpawnEnemy()
{
    // try each lane (shuffled) and choose the first one that satisfies:
    // 1) lane spacing rule for enemies, and
    // 2) does not overlap any existing entity at the spawn area.
    Span<int> lanes = stackalloc int[3] { 0, 1, 2 };
    Shuffle(lanes, _rng);

    for (int i = 0; i < lanes.Length; i++)
    {
        int lane = lanes[i];
        if (!EnemyLaneHasSpace(lane)) continue;

        var enemyEntity = Entity.Create(EntityKind.Enemy, LaneCenterX(lane), 0, State);
        enemyEntity.PositionY = SpawnAtTopY(enemyEntity.Height);

        if (!IsAreaClear(enemyEntity.PositionX, enemyEntity.PositionY, enemyEntity.Width, enemyEntity.Height, padding: 20))
            continue;

        State.Entities.Add(enemyEntity);
        return true;
    }

    return false;
}

private bool SpawnCoin()
{
    // coins are smaller, but still avoid overlaps with other spawns near the top.
    return TrySpawnInAnyLane(EntityKind.Coin, padding: 18);
}

private bool SpawnFuel()
{
    // fuel is rarer/bigger — use more padding so it doesn't clip into other items.
    return TrySpawnInAnyLane(EntityKind.Fuel, padding: 24);
}

private bool SpawnStar()
{
    // star is rare and should feel "clean" when it appears.
    return TrySpawnInAnyLane(EntityKind.Star, padding: 26);
}

    private bool SpawnTree()
    {
        // trees are purely visual, spawned on the grass shoulders (left/right of the road)
        if (State.ShoulderWidth <= 8) return false;

        // size is based on shoulder width, with a cap so it does not get huge on desktop
        var maxTreeWidth = Math.Min(State.ShoulderWidth * 0.75, State.ViewHeight * 0.18);
        var minTreeWidth = Math.Min(State.ShoulderWidth * 0.45, State.ViewHeight * 0.12);
        var treeWidth = RandomRange(minTreeWidth, maxTreeWidth);
        var treeHeight = treeWidth; // tree sprite is roughly square

        var treeSpawnY = SpawnAtTopY(treeHeight);

        bool leftSide = _rng.NextDouble() < 0.5;
        double minX, maxX;

        if (leftSide)
        {
            minX = 6;
            maxX = Math.Max(minX, State.RoadLeft - treeWidth - 6);
        }
        else
        {
            minX = State.RoadRight + 6;
            maxX = Math.Max(minX, State.ViewWidth - treeWidth - 6);
        }

        var treeSpawnX = RandomRange(minX, maxX);

        // avoid spawning on top of other entities (mainly other trees at the top)
        if (!IsAreaClear(treeSpawnX, treeSpawnY, treeWidth, treeHeight, padding: 12))
            return false;

        State.Entities.Add(new Entity
        {
            Kind = EntityKind.Tree,
            PositionX = treeSpawnX,
            PositionY = treeSpawnY,
            Width = treeWidth,
            Height = treeHeight
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

    private static double RandomRange(double minValue, double maxValue)
        => minValue + Random.Shared.NextDouble() * (maxValue - minValue);

    private static double Lerp(double startValue, double endValue, double amount)
        => startValue + (endValue - startValue) * Math.Clamp(amount, 0, 1);
}
