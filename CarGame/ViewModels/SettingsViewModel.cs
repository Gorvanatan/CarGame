using System.IO;
using System.Windows.Input;
using CarGame.Services;

namespace CarGame.ViewModels;

public sealed class SettingsViewModel : BaseViewModel
{
    private readonly IProfileService _profile;
    private readonly Func<string, string, Task>? _alert;
    private readonly Func<string, string, string, string, Task<bool>>? _confirm;

    private int _coinsHeld;
    public int CoinsHeld
    {
        get => _coinsHeld;
        set
        {
            if (SetProperty(ref _coinsHeld, value))
                OnPropertyChanged(nameof(CoinsHeldText));
        }
    }

    private int _coinsEarned;
    public int CoinsEarned
    {
        get => _coinsEarned;
        set
        {
            if (SetProperty(ref _coinsEarned, value))
                OnPropertyChanged(nameof(CoinsEarnedText));
        }
    }

    public string CoinsHeldText => $"Coins Held: {CoinsHeld}";
    public string CoinsEarnedText => $"Total Coins Earned: {CoinsEarned}";

    private double _masterVolume;
    public double MasterVolume
    {
        get => _masterVolume;
        set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (SetProperty(ref _masterVolume, clamped))
            {
                _profile.MasterVolume = clamped;
                OnPropertyChanged(nameof(VolumePercentText));
            }
        }
    }

    public string VolumePercentText
    {
        get
        {
            var pct = (int)Math.Round(Math.Clamp(MasterVolume, 0.0, 1.0) * 100);
            return $"{pct}%";
        }
    }

    private bool _sfxEnabled;
    public bool SfxEnabled
    {
        get => _sfxEnabled;
        set
        {
            if (SetProperty(ref _sfxEnabled, value))
                _profile.SfxEnabled = value;
        }
    }

    public ICommand BackCommand { get; }
    public ICommand EraseDataCommand { get; }

    public SettingsViewModel(
        IProfileService profile,
        Func<string, string, Task>? alert = null,
        Func<string, string, string, string, Task<bool>>? confirm = null)
    {
        _profile = profile;
        _alert = alert;
        _confirm = confirm;

        BackCommand = new Command(async () => await Shell.Current.GoToAsync(".."));
        EraseDataCommand = new Command(async () => await EraseAsync());

        LoadFromProfile();
    }

    public void OnAppearing() => LoadFromProfile();

    private void LoadFromProfile()
    {
        CoinsHeld = _profile.CoinsHeld;
        CoinsEarned = _profile.CoinsEarnedTotal;

        MasterVolume = _profile.MasterVolume;
        SfxEnabled = _profile.SfxEnabled;
    }

    private async Task EraseAsync()
    {
        // If no confirm delegate is provided, fail safe (do nothing).
        if (_confirm is null)
            return;

        var ok = await _confirm(
            "Erase user data?",
            "This will reset your High Score, coin totals, owned cars, selected car, upgrades, and audio settings back to default.",
            "Erase",
            "Cancel");

        if (!ok)
            return;

        // Try delete custom image file too (optional cleanup)
        var customPath = _profile.CustomCarPath;

        _profile.EraseAll();

        try
        {
            if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
                File.Delete(customPath);
        }
        catch
        {
            // ignore
        }

        LoadFromProfile();

        if (_alert is not null)
            await _alert("Done", "Your data has been reset.");
    }
}
