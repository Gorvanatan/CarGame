using CarGame.Game;

namespace CarGame.UI;

public sealed class GameDrawable : IDrawable
{
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

        _engine.Resize(dirtyRect.Width, dirtyRect.Height);

        float w = dirtyRect.Width;
        float h = dirtyRect.Height;

        DrawBackground(canvas, w, h);
        DrawPlayer(canvas);
        DrawEntities(canvas);

        if (_engine.State.IsGameOver)
            DrawGameOver(canvas, w, h);
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

        if (Sprites.Player is not null)
        {
            canvas.DrawImage(Sprites.Player, (float)p.X, (float)p.Y, (float)p.Width, (float)p.Height);
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
            }
        }
    }

    private static void DrawGameOver(ICanvas canvas, float w, float h)
    {
        canvas.FontColor = Colors.White;
        canvas.FontSize = 44;
        canvas.DrawString("GAME OVER", 0, h * 0.38f, w, 70,
            HorizontalAlignment.Center, VerticalAlignment.Center);

        canvas.FontSize = 20;
        canvas.DrawString("Tap to restart", 0, h * 0.46f, w, 40,
            HorizontalAlignment.Center, VerticalAlignment.Center);
    }
}
