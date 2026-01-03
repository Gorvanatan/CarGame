using Microsoft.Maui.Graphics.Platform;
using GraphicsImage = Microsoft.Maui.Graphics.IImage;

namespace CarGame.UI;

/// <summary>
/// Loads and caches sprites packaged as MauiAsset (recommended: put PNG/JPG in Resources/Raw).
/// </summary>
public sealed class SpriteStore
{
    private readonly Dictionary<string, GraphicsImage?> _cars = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Default player sprite (yellow car).
    /// </summary>
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
        // Cars (player can choose from multiple)
        _cars["yellowcar.png"] = await LoadFromAppPackageAsync("yellowcar.png");
        // Player unlockable: Purple (replaces Red)
        _cars["purplecar.png"] = await LoadFromAppPackageAsync("purplecar.png");

        // Enemy sprite (kept as red)
        _cars["redcar.png"]    = await LoadFromAppPackageAsync("redcar.png");
        _cars["bluecar.png"]   = await LoadFromAppPackageAsync("bluecar.png");
        _cars["greencar.png"]  = await LoadFromAppPackageAsync("greencar.png");

        Player = _cars["yellowcar.png"]; // default
        Enemy  = _cars["redcar.png"];    // enemy uses red car
        Coin   = await LoadFromAppPackageAsync("coin.jpg");
        Fuel   = await LoadFromAppPackageAsync("fuelcan.jpg");

        IsLoaded = true;
    }

    public GraphicsImage? GetCar(string? spriteFileName)
    {
        if (string.IsNullOrWhiteSpace(spriteFileName)) return Player;
        if (_cars.TryGetValue(spriteFileName, out var img)) return img;
        return Player;
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
