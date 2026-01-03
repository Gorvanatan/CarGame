namespace CarGame.Game;

public enum EntityKind
{
    Enemy,
    Coin,
    Fuel
}

public sealed class PlayerCar
{
    public double X, Y;
    public double Width = 60, Height = 100;

    /// <summary>
    /// Target lane index (0..2). The player smoothly lerps toward this lane.
    /// </summary>
    public int TargetLane = 1;
}

public sealed class Entity
{
    public EntityKind Kind;
    public double X, Y;
    public double Width, Height;

    public static Entity Make(EntityKind kind, double laneCenterX, double y, GameState s)
    {
        // Base size relative to lane width, with a cap so enemies/items don't get massive on desktop.
        var desiredCarW = s.LaneWidth * 0.52;
        var maxCarW = s.ViewHeight * 0.20;
        var w = Math.Min(desiredCarW, maxCarW);
        var h = w * 1.6;

        // Items are smaller than cars
        if (kind == EntityKind.Coin)
        {
            w *= 0.45;
            h = w;
        }

        if (kind == EntityKind.Fuel)
        {
            w *= 0.55;
            h = w;
        }

        return new Entity
        {
            Kind = kind,
            X = laneCenterX - w / 2,
            Y = y,
            Width = w,
            Height = h
        };
    }
}
