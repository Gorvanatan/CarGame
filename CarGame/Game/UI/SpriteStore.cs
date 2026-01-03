using Microsoft.Maui.Graphics.Platform;
using GraphicsImage = Microsoft.Maui.Graphics.IImage;

namespace CarGame.UI;

/// <summary>
/// Loads and caches sprites packaged as MauiAsset (recommended: put PNG/JPG in Resources/Raw).
/// </summary>
public sealed class SpriteStore
{
    public GraphicsImage? Player { get; private set; }
    public GraphicsImage? Enemy  { get; private set; }
    public GraphicsImage? Coin   { get; private set; }
    public GraphicsImage? Fuel   { get; private set; }

    public bool IsLoaded { get; private set; }

    /// <summary>
    /// Load sprites once. Safe to call multiple times.
    /// </summary>
    public async Task EnsureLoadedAsync()
    {
        if (IsLoaded) return;

        // NOTE: These must be lowercase on Android.
        // Put these files in Resources/Raw with Build Action = MauiAsset.
        Player = await LoadFromAppPackageAsync("yellowcar.png");
        Enemy  = await LoadFromAppPackageAsync("redcar.png");
        Coin   = await LoadFromAppPackageAsync("coin.jpg");
        Fuel   = await LoadFromAppPackageAsync("fuelcan.jpg");

        IsLoaded = true;
    }

    private static async Task<GraphicsImage?> LoadFromAppPackageAsync(string filename)
    {
        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync(filename);

            // Copy to memory so PlatformImage can decode safely.
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;

            // IMPORTANT: expects a Stream (NOT a lambda)
            return PlatformImage.FromStream(ms);
        }
        catch
        {
            return null;
        }
    }
}
