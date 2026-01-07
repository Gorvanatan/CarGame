namespace CarGame.Game;

// entity types that can appear in the game 
public enum EntityKind
{
    Enemy,
    Coin,
    Fuel,
    Star,

    // background decoration (non-collidable)
    Tree
}

// player car state used by the engine and renderer
public sealed class PlayerCar
{
    // current top-left position of the player car
    public double PositionX;
    public double PositionY;

    // player car size in pixels
    public double Width = 60;
    public double Height = 100;

    // target lane index (0..2) that the player car moves toward smoothly
    public int TargetLaneIndex = 1;
}

// a single enemy, pickup, or decoration in the world
public sealed class Entity
{
    // the type of entity (enemy, coin, fuel, etc.)
    public EntityKind Kind;

    // current top-left position of the entity
    public double PositionX;
    public double PositionY;

    // size of the entity in pixels
    public double Width;
    public double Height;

    public static Entity Create(EntityKind kind, double laneCenterX, double spawnY, GameState gameState)
    {
        // scale car width based on lane width, but cap it so it does not get huge on desktop
        double desiredCarWidth = gameState.LaneWidth * 0.52;
        double maxCarWidth = gameState.ViewHeight * 0.20;
        double entityWidth = Math.Min(desiredCarWidth, maxCarWidth);

        // keep a consistent car-like aspect ratio for enemies by default
        double entityHeight = entityWidth * 1.6;

        // make pickups smaller than cars and mostly square
        if (kind == EntityKind.Coin)
        {
            entityWidth *= 0.45;
            entityHeight = entityWidth;
        }
        else if (kind == EntityKind.Fuel)
        {
            entityWidth *= 0.55;
            entityHeight = entityWidth;
        }
        else if (kind == EntityKind.Star)
        {
            entityWidth *= 0.65;
            entityHeight = entityWidth;
        }

        // convert lane center into a top-left x so the entity is centered in the lane
        double topLeftX = laneCenterX - (entityWidth / 2.0);

        return new Entity
        {
            Kind = kind,
            PositionX = topLeftX,
            PositionY = spawnY,
            Width = entityWidth,
            Height = entityHeight
        };
    }
}
