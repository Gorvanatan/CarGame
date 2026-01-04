namespace CarGame.ViewModels;

// viewmodel for a single car card in the shop ui
public sealed class CarOptionViewModel : BaseViewModel
{
    public CarOptionViewModel(string key, string displayName, string imageSource, int cost)
    {
        // stores basic car info for binding
        Key = key;
        DisplayName = displayName;
        ImageSource = imageSource;
        Cost = cost;
    }

    // unique key used for saving/loading ownership and selection
    public string Key { get; }

    // friendly name shown in the ui
    public string DisplayName { get; }

    // image filename or file path for the car image
    public string ImageSource { get; }

    // coin cost to buy this car (0 means free/default)
    public int Cost { get; }

    private bool _isOwned;

    // true when the player has purchased/unlocked this car
    public bool IsOwned
    {
        get => _isOwned;
        set
        {
            // updates button text whenever ownership changes
            if (SetProperty(ref _isOwned, value))
                OnPropertyChanged(nameof(ActionText));
        }
    }

    private bool _isSelected;

    // true when this car is the currently selected car
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            // updates button text whenever selection changes
            if (SetProperty(ref _isSelected, value))
                OnPropertyChanged(nameof(ActionText));
        }
    }

    // returns the correct button text based on owned/selected status
    public string ActionText
    {
        get
        {
            if (IsSelected) return "Selected";
            if (IsOwned) return "Select";
            return Cost <= 0 ? "Select" : $"Buy ({Cost})";
        }
    }
}
