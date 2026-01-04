namespace CarGame.ViewModels;

public sealed class CarOptionViewModel : BaseViewModel
{
    public CarOptionViewModel(string key, string name, string imageSource, int cost)
    {
        Key = key;
        DisplayName = name;
        ImageSource = imageSource;
        Cost = cost;
    }

    public string Key { get; }
    public string DisplayName { get; }
    public string ImageSource { get; }
    public int Cost { get; }

    private bool _isOwned;
    public bool IsOwned
    {
        get => _isOwned;
        set
        {
            if (SetProperty(ref _isOwned, value))
                OnPropertyChanged(nameof(ActionText));
        }
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
                OnPropertyChanged(nameof(ActionText));
        }
    }

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
