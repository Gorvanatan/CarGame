using CarGame.Game;

namespace CarGame.UI;

public sealed class GameDrawable : IDrawable
{
    // zoom out slightly so the player can see more road ahead ("longer lanes")
    // 1.0 = normal, and lower values show more vertical space
    private const float RenderScale = 1.0f;

    private readonly GameEngine _engine;
    public SpriteStore Sprites { get; } = new();

    public GameDrawable(GameEngine engine) => _engine = engine;

    /// <summary>
    /// loads sprites (safe to call multiple times).
    /// </summary>
    public Task LoadImagesAsync() => Sprites.EnsureLoadedAsync();

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        // kick off sprite loading once (non-blocking) in case caller didn't await it
        if (!Sprites.IsLoaded)
            _ = Sprites.EnsureLoadedAsync();

        // set the render scale so the engine can keep on-screen speeds consistent.
        _engine.State.RenderScale = RenderScale;

        // resize the game "world" to be larger than the screen; we then scale
        // the canvas down, which effectively zooms the camera out.
        _engine.Resize(dirtyRect.Width / RenderScale, dirtyRect.Height / RenderScale);

        float worldWidth = (float)_engine.State.ViewWidth;
        float worldHeight = (float)_engine.State.ViewHeight;

        // world rendering (scaled)
        canvas.SaveState();
        canvas.Scale(RenderScale, RenderScale);

        DrawBackground(canvas, worldWidth, worldHeight);
        DrawDecorations(canvas);
        DrawPlayer(canvas);
        DrawEntities(canvas);

        canvas.RestoreState();
    }

    private void DrawBackground(ICanvas canvas, float worldWidth, float worldHeight)
    {
        var gameState = _engine.State;

        float roadLeft = (float)gameState.RoadLeft;
        float roadW = (float)gameState.RoadWidth;
        float roadRight = roadLeft + roadW;

        // grass shoulders
        canvas.FillColor = Colors.DarkGreen;
        canvas.FillRectangle(0, 0, worldWidth, worldHeight);

        // road (center)
        canvas.FillColor = Colors.DimGray;
        canvas.FillRectangle(roadLeft, 0, roadW, worldHeight);

        // subtle moving road texture (very cheap)
        canvas.FillColor = new Color(0, 0, 0, 0.06f);
        float spacing = 140f;
        float bandH = 70f;
        float startBandY = (float)(-(gameState.BgScroll * 0.15) % spacing);
        for (float bandY = startBandY; bandY < worldHeight; bandY += spacing)
            canvas.FillRectangle(roadLeft, bandY, roadW, bandH);

        // road edge shading + white shoulder line
        canvas.FillColor = new Color(0, 0, 0, 0.18f);
        canvas.FillRectangle(roadLeft, 0, 10, worldHeight);
        canvas.FillRectangle(roadRight - 10, 0, 10, worldHeight);

        canvas.StrokeColor = Colors.White;
        canvas.StrokeSize = 3;
        canvas.DrawLine(roadLeft + 2, 0, roadLeft + 2, worldHeight);
        canvas.DrawLine(roadRight - 2, 0, roadRight - 2, worldHeight);

        // lane dividers (3 lanes -> 2 divider lines) inside the road
        float laneW = (float)gameState.LaneWidth;

        canvas.StrokeColor = Colors.White;
        canvas.StrokeSize = 6;
        canvas.StrokeDashPattern = new float[] { 35, 35 };

        float dashCycle = 70f; // dash+gap = 35+35
        canvas.StrokeDashOffset = (float)(-gameState.BgScroll % dashCycle);

        canvas.DrawLine(roadLeft + laneW, 0, roadLeft + laneW, worldHeight);
        canvas.DrawLine(roadLeft + laneW * 2, 0, roadLeft + laneW * 2, worldHeight);

        // reset dash so other strokes aren't dashed
        canvas.StrokeDashPattern = null;
        canvas.StrokeDashOffset = 0;
    }


    private void DrawDecorations(ICanvas canvas)
    {
        // trees (background decoration) live in the grass shoulders
        foreach (var entity in _engine.State.Entities)
        {
            if (entity.Kind != EntityKind.Tree) continue;

            if (Sprites.Tree is not null)
                canvas.DrawImage(Sprites.Tree, (float)entity.PositionX, (float)entity.PositionY, (float)entity.Width, (float)entity.Height);
            else
            {
                canvas.FillColor = Colors.ForestGreen;
                canvas.FillRoundedRectangle((float)entity.PositionX, (float)entity.PositionY, (float)entity.Width, (float)entity.Height, 14);
            }
        }
    }

    private void DrawPlayer(ICanvas canvas)
    {
        var player = _engine.State.Player;

        var playerSprite = Sprites.GetCar(_engine.State.SelectedCarSprite) ?? Sprites.Player;

        if (playerSprite is not null)
        {
            canvas.DrawImage(playerSprite, (float)player.PositionX, (float)player.PositionY, (float)player.Width, (float)player.Height);

            // simple visual indicator when invincible: draw a star above the car
            if (_engine.State.IsInvincible && Sprites.Star is not null)
            {
                var starSize = (float)(player.Width * 0.55);
                var starX = (float)(player.PositionX + (player.Width - starSize) / 2);
                var starY = (float)(player.PositionY - starSize * 0.75);
                canvas.DrawImage(Sprites.Star, starX, starY, starSize, starSize);
            }
            return;
        }

        // fallback
        canvas.FillColor = Colors.DodgerBlue;
        canvas.FillRoundedRectangle((float)player.PositionX, (float)player.PositionY, (float)player.Width, (float)player.Height, 14);
    }

    private void DrawEntities(ICanvas canvas)
    {
        foreach (var entity in _engine.State.Entities)
        {
            if (entity.Kind == EntityKind.Tree) continue;

            switch (entity.Kind)
            {
                case EntityKind.Enemy:
                    if (Sprites.Enemy is not null)
                        canvas.DrawImage(Sprites.Enemy, (float)entity.PositionX, (float)entity.PositionY, (float)entity.Width, (float)entity.Height);
                    else
                    {
                        canvas.FillColor = Colors.Red;
                        canvas.FillRoundedRectangle((float)entity.PositionX, (float)entity.PositionY, (float)entity.Width, (float)entity.Height, 12);
                    }
                    break;

                case EntityKind.Coin:
                    if (Sprites.Coin is not null)
                        canvas.DrawImage(Sprites.Coin, (float)entity.PositionX, (float)entity.PositionY, (float)entity.Width, (float)entity.Height);
                    else
                    {
                        canvas.FillColor = Colors.Gold;
                        canvas.FillEllipse((float)entity.PositionX, (float)entity.PositionY, (float)entity.Width, (float)entity.Height);
                    }
                    break;

                case EntityKind.Fuel:
                    if (Sprites.Fuel is not null)
                        canvas.DrawImage(Sprites.Fuel, (float)entity.PositionX, (float)entity.PositionY, (float)entity.Width, (float)entity.Height);
                    else
                    {
                        canvas.FillColor = Colors.LimeGreen;
                        canvas.FillRoundedRectangle((float)entity.PositionX, (float)entity.PositionY, (float)entity.Width, (float)entity.Height, 10);
                    }
                    break;

                case EntityKind.Star:
                    if (Sprites.Star is not null)
                        canvas.DrawImage(Sprites.Star, (float)entity.PositionX, (float)entity.PositionY, (float)entity.Width, (float)entity.Height);
                    else
                    {
                        canvas.FillColor = Colors.Yellow;
                        canvas.FillEllipse((float)entity.PositionX, (float)entity.PositionY, (float)entity.Width, (float)entity.Height);
                    }
                    break;
            }
        }
    }
}
