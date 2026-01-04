using System.Windows.Input;
using CarGame.Services;
using Microsoft.Maui.Controls;

namespace CarGame.ViewModels;

public sealed class MenuViewModel : BaseViewModel
{
    private readonly IProfileService _profile;

    private string _highScoreText = "High Score: 0";
    public string HighScoreText
    {
        get => _highScoreText;
        private set => SetProperty(ref _highScoreText, value);
    }

    private string _coinsHeldText = "Coins Held: 0";
    public string CoinsHeldText
    {
        get => _coinsHeldText;
        private set => SetProperty(ref _coinsHeldText, value);
    }

    private string _selectedCarText = "Selected Car: Yellow";
    public string SelectedCarText
    {
        get => _selectedCarText;
        private set => SetProperty(ref _selectedCarText, value);
    }

    public ICommand PlayCommand { get; }
    public ICommand ShopCommand { get; }
    public ICommand SettingsCommand { get; }
    public ICommand HowToCommand { get; }

    public MenuViewModel(IProfileService profile)
    {
        _profile = profile;

        PlayCommand = new Command(async () => await Shell.Current.GoToAsync("game"));
        ShopCommand = new Command(async () => await Shell.Current.GoToAsync("shop"));
        SettingsCommand = new Command(async () => await Shell.Current.GoToAsync("settings"));
        HowToCommand = new Command(async () => await Shell.Current.GoToAsync("howto"));
    }

    public void OnAppearing()
    {
        HighScoreText = $"High Score: {_profile.HighScore}";
        CoinsHeldText = $"Coins Held: {_profile.CoinsHeld}";
        SelectedCarText = $"Selected Car: {CarName(_profile.SelectedCarSprite)}";
    }

    private static string CarName(string sprite)
    {
        return (sprite ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "yellowcar.png" => "Yellow",
            "purplecar.png" => "Purple",
            "bluecar.png" => "Blue",
            "greencar.png" => "Green",
            "customcar" => "Custom",
            _ => "Car"
        };
    }
}
