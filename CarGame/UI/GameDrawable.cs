using CarGame.Game;

namespace CarGame.UI;

public sealed class GameDrawable : IDrawable
{
    // Zoom out slightly so the player can see more road ahead ("longer lanes").
    // 1.0 = normal. 0.8 = show ~25% more vertical space.
    private const float RenderScale = 1.0f;

    private readonly GameEngine _engine;
    public SpriteStore Sprites { get; } = new();

    public GameDrawable(GameEngine engine) => _engine = engine;

    /// <summary>
    /// Loads sprites (safe to call multiple times).
    /// </summary>
    public Task LoadImagesAsync() => Sprites.EnsureLoadedAsync();

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        // Kick off sprite loading once (non-blocking) in case caller didn't await it.
        if (!Sprites.IsLoaded)
            _ = Sprites.EnsureLoadedAsync();

        // Set the render scale so the engine can keep on-screen speeds consistent.
        _engine.State.RenderScale = RenderScale;

        // Resize the game "world" to be larger than the screen; we then scale
        // the canvas down, which effectively zooms the camera out.
        _engine.Resize(dirtyRect.Width / RenderScale, dirtyRect.Height / RenderScale);

        float worldW = (float)_engine.State.ViewWidth;
        float worldH = (float)_engine.State.ViewHeight;

        // World rendering (scaled)
        canvas.SaveState();
        canvas.Scale(RenderScale, RenderScale);

        DrawBackground(canvas, worldW, worldH);
        DrawPlayer(canvas);
        DrawEntities(canvas);

        canvas.RestoreState();

        // UI overlays (unscaled)
        float w = dirtyRect.Width;
        float h = dirtyRect.Height;

        if (_engine.State.IsPaused && !_engine.State.IsGameOver)
            DrawPaused(canvas, w, h);

        if (_engine.State.IsGameOver)
            DrawGameOver(canvas, w, h, _engine.State.IsNewHighScore);
    }

    private static void DrawPaused(ICanvas canvas, float w, float h)
    {
        // Dim the screen
        canvas.FillColor = new Color(0, 0, 0, 0.45f);
        canvas.FillRectangle(0, 0, w, h);

        canvas.FontColor = Colors.White;
        canvas.FontSize = 44;
        canvas.DrawString("PAUSED", 0, h * 0.42f, w, 70,
            HorizontalAlignment.Center, VerticalAlignment.Center);
    }

    private void DrawBackground(ICanvas canvas, float w, float h)
    {
        // Grass (kept for later shoulders)
        canvas.FillColor = Colors.DarkGreen;
        canvas.FillRectangle(0, 0, w, h);

        // Road
        canvas.FillColor = Colors.DimGray;
        canvas.FillRectangle(0, 0, w, h);

        // Edge shading
        canvas.FillColor = Colors.Gray;
        canvas.FillRectangle(0, 0, 10, h);
        canvas.FillRectangle(w - 10, 0, 10, h);

        // Lane dividers (3 lanes -> 2 divider lines)
        float laneW = w / 3f;

        canvas.StrokeColor = Colors.White;
        canvas.StrokeSize = 6;
        canvas.StrokeDashPattern = new float[] { 35, 35 };

        float dashCycle = 70f; // dash+gap = 35+35
        canvas.StrokeDashOffset = (float)(-_engine.State.BgScroll % dashCycle);

        canvas.DrawLine(laneW, 0, laneW, h);
        canvas.DrawLine(laneW * 2, 0, laneW * 2, h);
    }

    private void DrawPlayer(ICanvas canvas)
    {
        var p = _engine.State.Player;

        var playerSprite = Sprites.GetCar(_engine.State.SelectedCarSprite) ?? Sprites.Player;

        if (playerSprite is not null)
        {
            canvas.DrawImage(playerSprite, (float)p.X, (float)p.Y, (float)p.Width, (float)p.Height);

            // Simple visual indicator when invincible: draw a star above the car.
            if (_engine.State.IsInvincible && Sprites.Star is not null)
            {
                var s = (float)(p.Width * 0.55);
                var sx = (float)(p.X + (p.Width - s) / 2);
                var sy = (float)(p.Y - s * 0.75);
                canvas.DrawImage(Sprites.Star, sx, sy, s, s);
            }
            return;
        }

        // Fallback
        canvas.FillColor = Colors.DodgerBlue;
        canvas.FillRoundedRectangle((float)p.X, (float)p.Y, (float)p.Width, (float)p.Height, 14);
    }

    private void DrawEntities(ICanvas canvas)
    {
        foreach (var e in _engine.State.Entities)
        {
            switch (e.Kind)
            {
                case EntityKind.Enemy:
                    if (Sprites.Enemy is not null)
                        canvas.DrawImage(Sprites.Enemy, (float)e.X, (float)e.Y, (float)e.Width, (float)e.Height);
                    else
                    {
                        canvas.FillColor = Colors.Red;
                        canvas.FillRoundedRectangle((float)e.X, (float)e.Y, (float)e.Width, (float)e.Height, 12);
                    }
                    break;

                case EntityKind.Coin:
                    if (Sprites.Coin is not null)
                        canvas.DrawImage(Sprites.Coin, (float)e.X, (float)e.Y, (float)e.Width, (float)e.Height);
                    else
                    {
                        canvas.FillColor = Colors.Gold;
                        canvas.FillEllipse((float)e.X, (float)e.Y, (float)e.Width, (float)e.Height);
                    }
                    break;

                case EntityKind.Fuel:
                    if (Sprites.Fuel is not null)
                        canvas.DrawImage(Sprites.Fuel, (float)e.X, (float)e.Y, (float)e.Width, (float)e.Height);
                    else
                    {
                        canvas.FillColor = Colors.LimeGreen;
                        canvas.FillRoundedRectangle((float)e.X, (float)e.Y, (float)e.Width, (float)e.Height, 10);
                    }
                    break;

                case EntityKind.Star:
                    if (Sprites.Star is not null)
                        canvas.DrawImage(Sprites.Star, (float)e.X, (float)e.Y, (float)e.Width, (float)e.Height);
                    else
                    {
                        canvas.FillColor = Colors.Yellow;
                        canvas.FillEllipse((float)e.X, (float)e.Y, (float)e.Width, (float)e.Height);
                    }
                    break;
            }
        }
    }

    private static void DrawGameOver(ICanvas canvas, float w, float h, bool newHighScore)
    {
        canvas.FontColor = Colors.White;
        canvas.FontSize = 44;
        canvas.DrawString("GAME OVER", 0, h * 0.38f, w, 70,
            HorizontalAlignment.Center, VerticalAlignment.Center);

        canvas.FontSize = 20;
        canvas.DrawString("Returning to menu...", 0, h * 0.46f, w, 40,
            HorizontalAlignment.Center, VerticalAlignment.Center);


        if (newHighScore)
        {
            canvas.FontSize = 22;
            canvas.DrawString("NEW HIGH SCORE!", 0, h * 0.52f, w, 40,
                HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }
}
