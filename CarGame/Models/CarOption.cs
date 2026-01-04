namespace CarGame.Models;

public sealed class CarOption
{
    public required string Key { get; init; }   // e.g. "yellowcar.png", "purplecar.png", "customcar"
    public required string DisplayName { get; init; }
    public required string ImageSource { get; init; }
    public int Cost { get; init; }

    public bool IsOwned { get; set; }
    public bool IsSelected { get; set; }
}
