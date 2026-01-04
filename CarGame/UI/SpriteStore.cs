using Microsoft.Maui.Graphics.Platform;
using Microsoft.Maui.Storage;
using System.IO;
using GraphicsImage = Microsoft.Maui.Graphics.IImage;

namespace CarGame.UI;

/// <summary>
/// loads and caches sprites packaged as MauiAsset (recommended: put PNG/JPG in Resources/Raw).
/// </summary>
public sealed class SpriteStore
{
    private readonly Dictionary<string, GraphicsImage?> _cars = new(StringComparer.OrdinalIgnoreCase);

    private const string CustomCarKey = "customcar";
    private const string PrefCustomCarPath = "custom_car_path";
    private string? _customCarPath;


    /// <summary>
    /// default player sprite (yellow car).
    /// </summary>
    public GraphicsImage? Player { get; private set; }
    public GraphicsImage? Enemy  { get; private set; }
    public GraphicsImage? Coin   { get; private set; }
    public GraphicsImage? Fuel   { get; private set; }
    public GraphicsImage? Star   { get; private set; }
    public GraphicsImage? Tree   { get; private set; }

    public bool IsLoaded { get; private set; }

    /// <summary>
    /// load sprites once. Safe to call multiple times.
    /// </summary>
    public async Task EnsureLoadedAsync()
    {
        if (IsLoaded) return;

        // nOTE: These must be lowercase on Android.
        // put these files in Resources/Raw with Build Action = MauiAsset.
        // cars (player can choose from multiple)
        _cars["yellowcar.png"] = await LoadFromAppPackageAsync("yellowcar.png");
        // player unlockable: Purple (replaces Red)
        _cars["purplecar.png"] = await LoadFromAppPackageAsync("purplecar.png");

        // enemy sprite (kept as red)
        _cars["redcar.png"]    = await LoadFromAppPackageAsync("redcar.png");
        _cars["bluecar.png"]   = await LoadFromAppPackageAsync("bluecar.png");
        _cars["greencar.png"]  = await LoadFromAppPackageAsync("greencar.png");

        Player = _cars["yellowcar.png"]; // default
        Enemy  = _cars["redcar.png"];    // enemy uses red car
        Coin   = await LoadFromAppPackageAsync("coin.jpg");
        Fuel   = await LoadFromAppPackageAsync("fuelcan.jpg");
        Star   = await LoadFromAppPackageAsync("starpower.png");

        Tree   = await LoadFromAppPackageAsync("tree.png");

        // optional: user-uploaded custom car
        await LoadCustomCarFromPreferencesAsync();

        IsLoaded = true;
    }

    public GraphicsImage? GetCar(string? spriteFileName)
    {
        if (string.IsNullOrWhiteSpace(spriteFileName)) return Player;
        if (_cars.TryGetValue(spriteFileName, out var img)) return img;

        // allow selecting a user-uploaded car (stored as a file path in preferences)
        if (spriteFileName.Equals(CustomCarKey, StringComparison.OrdinalIgnoreCase))
        {
            // lazy load (in case preferences changed after initial load)
            if (img is null)
                _ = LoadCustomCarFromPreferencesAsync();
            if (_cars.TryGetValue(CustomCarKey, out var custom) && custom is not null)
                return custom;
        }

        return Player;
    }


    public async Task SetCustomCarAsync(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            _customCarPath = null;
            _cars.Remove(CustomCarKey);
            return;
        }

        _customCarPath = filePath;

        var img = await LoadFromFileAsync(filePath);
        if (img is null)
        {
            _cars.Remove(CustomCarKey);
            return;
        }

        _cars[CustomCarKey] = img;
    }

    private async Task LoadCustomCarFromPreferencesAsync()
    {
        try
        {
            var path = Preferences.Default.Get(PrefCustomCarPath, string.Empty);
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                _customCarPath = null;
                _cars.Remove(CustomCarKey);
                return;
            }

            // only reload if the path changed
            if (string.Equals(_customCarPath, path, StringComparison.Ordinal))
                return;

            _customCarPath = path;

            var img = await LoadFromFileAsync(path);
            if (img is null)
                _cars.Remove(CustomCarKey);
            else
                _cars[CustomCarKey] = img;
        }
        catch
        {
            // ignore
        }
    }

    private static async Task<GraphicsImage?> LoadFromFileAsync(string filePath)
    {
        try
        {
            await using var stream = File.OpenRead(filePath);

            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;

            return PlatformImage.FromStream(ms);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<GraphicsImage?> LoadFromAppPackageAsync(string filename)
    {
        try
        {
            await using var stream = await FileSystem.OpenAppPackageFileAsync(filename);

            // copy to memory so PlatformImage can decode safely.
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            ms.Position = 0;

            // iMPORTANT: expects a Stream (NOT a lambda)
            return PlatformImage.FromStream(ms);
        }
        catch
        {
            return null;
        }
    }
}
