namespace CarGame.Models;

// describes a car option shown in the shop (unlock + select)
public sealed class CarOption
{
    // unique key used for saving/loading (for example a sprite filename)
    public required string Key { get; init; }

    // friendly name shown in the ui
    public required string DisplayName { get; init; }

    // image source used by the ui (sprite filename or file path)
    public required string ImageSource { get; init; }

    // coin cost to buy this car
    public int Cost { get; init; }

    // tracks whether the player already owns this car
    public bool IsOwned { get; set; }

    // tracks whether this car is currently selected
    public bool IsSelected { get; set; }
}
