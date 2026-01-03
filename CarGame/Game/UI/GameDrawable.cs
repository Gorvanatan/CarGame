using CarGame.Game;
using Microsoft.Maui.Graphics.Platform;
using GraphicsImage = Microsoft.Maui.Graphics.IImage;

namespace CarGame.UI;

public sealed class GameDrawable : IDrawable
{
    private readonly GameEngine _engine;

    // Sprites (loaded from app package)
    private GraphicsImage? _playerImg;
    private GraphicsImage? _enemyImg;
    private GraphicsImage? _coinImg;
    private GraphicsImage? _fuelImg;

    private bool _loadingStarted;

    public GameDrawable(GameEngine engine) => _engine = engine;

    /// <summary>
    /// Loads sprites from the app package. Safe to call multiple times.
    /// </summary>
    public async Task LoadImagesAsync()
    {
        if (_loadingStarted) return;
        _loadingStarted = true;

        // Android requires resource file names to be lowercase
        _playerImg = await LoadImageFromAppPackageAsync("yellowcar.png");
        _enemyImg = await LoadImageFromAppPackageAsync("redcar.png");
        _coinImg = await LoadImageFromAppPackageAsync("coin.jpg");
        _fuelImg = await LoadImageFromAppPackageAsync("fuelcan.jpg");
    }

    private static async Task<GraphicsImage?> LoadImageFromAppPackageAsync(string filename)
    {
        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync(filename);

            // Copy into MemoryStream so the image can be created safely
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;

            // IMPORTANT: FromStream expects a Stream (NOT a lambda)
            return PlatformImage.FromStream(ms);
        }
        catch
        {
            // If a file isn't found or can't load, return null (we'll draw shapes instead)
            return null;
        }
    }

    public void Draw(ICanvas canvas, RectF dirtyRect)
    {
        // Start sprite loading once (non-blocking)
        if (!_loadingStarted)
            _ = LoadImagesAsync();

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

        if (_playerImg is not null)
        {
            canvas.DrawImage(_playerImg, (float)p.X, (float)p.Y, (float)p.Width, (float)p.Height);
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
                    if (_enemyImg is not null)
                    {
                        canvas.DrawImage(_enemyImg, (float)e.X, (float)e.Y, (float)e.Width, (float)e.Height);
                    }
                    else
                    {
                        canvas.FillColor = Colors.Red;
                        canvas.FillRoundedRectangle((float)e.X, (float)e.Y, (float)e.Width, (float)e.Height, 12);
                    }
                    break;

                case EntityKind.Coin:
                    if (_coinImg is not null)
                    {
                        canvas.DrawImage(_coinImg, (float)e.X, (float)e.Y, (float)e.Width, (float)e.Height);
                    }
                    else
                    {
                        canvas.FillColor = Colors.Gold;
                        canvas.FillEllipse((float)e.X, (float)e.Y, (float)e.Width, (float)e.Height);
                    }
                    break;

                case EntityKind.Fuel:
                    if (_fuelImg is not null)
                    {
                        canvas.DrawImage(_fuelImg, (float)e.X, (float)e.Y, (float)e.Width, (float)e.Height);
                    }
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