using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Input;
using CarGame.Services;

namespace CarGame.ViewModels;

public sealed class ShopViewModel : BaseViewModel
{
    private readonly IProfileService _profile;
    private readonly Func<string, string, Task>? _alert;

    // this key is used across the app to represent the custom uploaded car
    private const string CustomCarKey = "customcar";

    // this list drives the shop car ui (items are shown with buy/select state)
    public ObservableCollection<CarOptionViewModel> Cars { get; } = new();

    private int _coinsHeld;
    public int CoinsHeld
    {
        get => _coinsHeld;
        set
        {
            if (SetProperty(ref _coinsHeld, value))
            {
                OnPropertyChanged(nameof(CoinsHeldText));
                RefreshUpgradeButtonStates();
                RefreshCarButtonStates();
            }
        }
    }

    public string CoinsHeldText => $"Coins Held: {CoinsHeld}";

    private string _selectedCarName = "Yellow";
    public string SelectedCarName
    {
        get => _selectedCarName;
        set => SetProperty(ref _selectedCarName, value);
    }

    public string SelectedCarText => $"Selected: {SelectedCarName}";

    private int _maxHealth;
    public int MaxHealth
    {
        get => _maxHealth;
        set
        {
            if (SetProperty(ref _maxHealth, value))
            {
                OnPropertyChanged(nameof(MaxHealthText));
                OnPropertyChanged(nameof(HealthUpgradeButtonText));
                RefreshUpgradeButtonStates();
            }
        }
    }

    private int _invSeconds;
    public int InvSeconds
    {
        get => _invSeconds;
        set
        {
            if (SetProperty(ref _invSeconds, value))
            {
                OnPropertyChanged(nameof(InvincibilityText));
                OnPropertyChanged(nameof(InvUpgradeButtonText));
                RefreshUpgradeButtonStates();
            }
        }
    }

    private bool _canBuyHealthUpgrade;
    public bool CanBuyHealthUpgrade
    {
        get => _canBuyHealthUpgrade;
        set => SetProperty(ref _canBuyHealthUpgrade, value);
    }

    private bool _canBuyInvUpgrade;
    public bool CanBuyInvUpgrade
    {
        get => _canBuyInvUpgrade;
        set => SetProperty(ref _canBuyInvUpgrade, value);
    }

    public string MaxHealthText => $"Max Health: {MaxHealth}/6";
    public string InvincibilityText => $"Invincibility: {InvSeconds}s";

    public string HealthUpgradeButtonText => MaxHealth >= 6 ? "Maxed" : "Buy (50)";
    public string InvUpgradeButtonText => InvSeconds >= 12 ? "Maxed" : "Buy (50)";

    public ICommand BackCommand { get; }
    public ICommand SelectOrBuyCarCommand { get; }
    public ICommand BuyHealthUpgradeCommand { get; }
    public ICommand BuyInvUpgradeCommand { get; }

    // called by the page after a custom image is picked using FilePicker
    public void SetCustomCar(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return;

        _profile.CustomCarPath = filePath;
        _profile.SelectedCarSprite = CustomCarKey;

        // select the custom car without adding it as a visible option in the car list
        LoadFromProfile();
    }

    public ShopViewModel(IProfileService profile, Func<string, string, Task>? alert = null)
    {
        _profile = profile;
        _alert = alert;

        // commands keep button logic out of the page code-behind
        BackCommand = new Command(async () => await Shell.Current.GoToAsync(".."));
        SelectOrBuyCarCommand = new Command<CarOptionViewModel>(async (c) => await SelectOrBuyAsync(c));
        BuyHealthUpgradeCommand = new Command(async () => await BuyHealthUpgradeAsync());
        BuyInvUpgradeCommand = new Command(async () => await BuyInvUpgradeAsync());

        // build the shop list once, then load current ownership/coins/selection from prefs
        BuildCarList();
        LoadFromProfile();
    }

    public void OnAppearing() => LoadFromProfile();

    private void BuildCarList()
    {
        Cars.Clear();

        Cars.Add(new CarOptionViewModel("yellowcar.png", "Yellow", "car_yellow.png", 0));

        Cars.Add(new CarOptionViewModel("purplecar.png", "Purple", "car_purple.png", 25));

        Cars.Add(new CarOptionViewModel("bluecar.png", "Blue", "car_blue.png", 50));

        Cars.Add(new CarOptionViewModel("greencar.png", "Green", "car_green.png", 75));
    }

    private void LoadFromProfile()
    {
        // load player wallet + upgrades
        CoinsHeld = _profile.CoinsHeld;

        MaxHealth = _profile.MaxHealth;
        InvSeconds = _profile.InvincibilitySeconds;

        // apply owned flags to each car option
        foreach (var car in Cars)
        {
            car.IsOwned = car.Key switch
            {
                "yellowcar.png" => true,
                "purplecar.png" => _profile.OwnedPurple,
                "bluecar.png" => _profile.OwnedBlue,
                "greencar.png" => _profile.OwnedGreen,
                _ => false
            };

            car.IsSelected = false;
        }

        var selectedKey = _profile.SelectedCarSprite;

        // if a custom car is selected and the file exists, show it in the selected text
        var hasCustomCar = !string.IsNullOrWhiteSpace(_profile.CustomCarPath) && File.Exists(_profile.CustomCarPath);
        if (hasCustomCar && string.Equals(selectedKey, CustomCarKey, StringComparison.OrdinalIgnoreCase))
        {
            // leave all car tiles unselected so the list stays as the built-in cars only
            SelectedCarName = "Custom";
            OnPropertyChanged(nameof(SelectedCarText));

            RefreshUpgradeButtonStates();
            RefreshCarButtonStates();
            OnPropertyChanged(nameof(Cars));
            return;
        }
        if (selectedKey.Equals("redcar.png", StringComparison.OrdinalIgnoreCase))
            selectedKey = "purplecar.png";

        var selectedCar = Cars.FirstOrDefault(c => string.Equals(c.Key, selectedKey, StringComparison.OrdinalIgnoreCase));
        if (selectedCar is null || !selectedCar.IsOwned)
            selectedCar = Cars.First(); // Yellow

        selectedCar.IsSelected = true;
        SelectedCarName = selectedCar.DisplayName;
        OnPropertyChanged(nameof(SelectedCarText));

        RefreshUpgradeButtonStates();
        RefreshCarButtonStates();

        // force ui refresh of triggers
        OnPropertyChanged(nameof(Cars));
    }

    private void RefreshUpgradeButtonStates()
    {
        // keep buttons clickable when not maxed so we can show a popup if coins are low
        CanBuyHealthUpgrade = MaxHealth < 6;
        CanBuyInvUpgrade = InvSeconds < 12;

        (BuyHealthUpgradeCommand as Command)?.ChangeCanExecute();
        (BuyInvUpgradeCommand as Command)?.ChangeCanExecute();
    }

    private void RefreshCarButtonStates() => OnPropertyChanged(nameof(Cars));

    private async Task SelectOrBuyAsync(CarOptionViewModel? car)
    {
        if (car is null) return;

        if (car.IsOwned)
        {
            SelectCar(car);
            return;
        }

        // buy flow
        if (CoinsHeld < car.Cost)
        {
            if (_alert is not null)
                await _alert(
                    "Not enough coins",
                    $"you need {car.Cost} coins to unlock {car.DisplayName}.\nyou currently have {CoinsHeld} coins."
                );
            return;
        }

        CoinsHeld -= car.Cost;
        _profile.CoinsHeld = CoinsHeld;

        SetOwned(car.Key, true);
        car.IsOwned = true;

        SelectCar(car);

        // notify UI
        OnPropertyChanged(nameof(CoinsHeldText));
        OnPropertyChanged(nameof(Cars));
    }

    private void SelectCar(CarOptionViewModel selectedCar)
    {
        // clear previous selection
        foreach (var carOption in Cars) carOption.IsSelected = false;

        // mark the chosen car as selected
        selectedCar.IsSelected = true;

        // save the selected car key so the game can use it
        _profile.SelectedCarSprite = selectedCar.Key;

        // update the ui label
        SelectedCarName = selectedCar.DisplayName;
        OnPropertyChanged(nameof(SelectedCarText));
        OnPropertyChanged(nameof(Cars));
    }

    private void SetOwned(string key, bool value)
    {
        switch (key)
        {
            case "purplecar.png": _profile.OwnedPurple = value; break;
            case "bluecar.png": _profile.OwnedBlue = value; break;
            case "greencar.png": _profile.OwnedGreen = value; break;
        }
    }

    private async Task BuyHealthUpgradeAsync()
    {
        const int upgradeCost = 50;
        if (MaxHealth >= 6) return;
        if (CoinsHeld < upgradeCost)
        {
            if (_alert is not null)
                await _alert(
                    "Not enough coins",
                    $"you need {upgradeCost} coins for this upgrade.\nyou currently have {CoinsHeld} coins."
                );
            return;
        }

        CoinsHeld -= upgradeCost;
        _profile.CoinsHeld = CoinsHeld;

        MaxHealth = Math.Min(6, MaxHealth + 1);
        _profile.MaxHealth = MaxHealth;

        OnPropertyChanged(nameof(CoinsHeldText));
    }

    private async Task BuyInvUpgradeAsync()
    {
        const int upgradeCost = 50;
        if (InvSeconds >= 12) return;
        if (CoinsHeld < upgradeCost)
        {
            if (_alert is not null)
                await _alert(
                    "Not enough coins",
                    $"you need {upgradeCost} coins for this upgrade.\nyou currently have {CoinsHeld} coins."
                );
            return;
        }

        CoinsHeld -= upgradeCost;
        _profile.CoinsHeld = CoinsHeld;

        // upgrade in steps of +2 seconds
        InvSeconds = Math.Min(12, InvSeconds + 2);
        _profile.InvincibilitySeconds = InvSeconds;

        OnPropertyChanged(nameof(CoinsHeldText));
    }
}
