using CarGame.Game;
using CarGame.UI;
using CarGame.ViewModels;
using Microsoft.Maui.Storage;
using Microsoft.Maui.Graphics;
using Plugin.Maui.Audio;
using System.Diagnostics;
using System.IO;

namespace CarGame;

public partial class MainPage : ContentPage
{
    private const string PrefSelectedCar = "selected_car_sprite";
    private const string PrefCustomCarPath = "custom_car_path";
    private const string CustomCarKey = "customcar";
    private const string PrefHighScore = "highscore";

    // economy
    // - CoinsHeld: spendable currency (decreases when buying)
    // - CoinsEarnedTotal: lifetime earned (never decreases)
    private const string PrefCoinsHeld = "coins_held";
    private const string PrefCoinsEarnedTotal = "coins_earned_total";

    // legacy key (older builds used a single total that never decreased)
    private const string PrefTotalCoinsLegacy = "total_coins";

    // owned cars
    private const string PrefOwnedPurple = "owned_purple";
    private const string PrefOwnedBlue = "owned_blue";
    private const string PrefOwnedGreen = "owned_green";

    // upgrades (shared with GameEngine)
    private const string PrefMaxHealth = "max_health"; // int, 3..6
    private const string PrefInvincibilityDurationSeconds = "invincibility_duration_seconds"; // int, 6..12

    // audio settings
    private const string PrefMasterVolume = "master_volume"; // 0..1
    private const string PrefSfxEnabled = "sfx_enabled";

    // base (per-sound) volumes. These are multiplied by the master volume.
    private const double BaseCoinVol = 0.7;
    private const double BaseFuelVol = 0.8;
    private const double BaseDamageVol = 0.9;
    private const double BaseCrashVol = 1.0;
    private const double BaseEngineRevVol = 0.25;
    private const double BaseMusicVol = 0.5;
    private const double BaseStarMusicVol = 0.8;

    private const double DefaultMasterVolume = 0.5;
    private const string DefaultCar = "yellowcar.png";

    // car costs (coins held)
    private const int CostPurple = 25;
    private const int CostBlue = 50;
    private const int CostGreen = 75;

    private const int UpgradeCost = 50;
    private const int MaxHealthCap = 6;
    private const int BaseHealth = 3;
    // base invincibility = 6s. Upgrade adds +2s each time, up to 12s (6 -> 8 -> 10 -> 12)
    private const int BaseInvSeconds = 6;
    private const int MaxInvSeconds = 12;

    private int _coinsHeld;
    private int _coinsEarnedTotal;

    // audio settings state
    private double _masterVolume;
    private bool _sfxEnabled;

    private readonly GameEngine _engine = new();
    private readonly GameHudViewModel _hud = new();
    private readonly GameDrawable _drawable;

    private readonly IDispatcherTimer _timer;
    private readonly Stopwatch _sw = new();

    // for mouse + touch on Windows (PanGesture works reliably)
    private double _panTotalX = 0;

    // hint fade
    private CancellationTokenSource? _hintFadeCts;
    private bool _lastGameOver;
    private bool _lastPaused;
    private bool _gameOverShown;

    // coin SFX
    private IAudioPlayer? _coinPlayer;

    // fuel SFX
    private IAudioPlayer? _fuelPlayer;

    // damage / crash SFX
    private IAudioPlayer? _damagePlayer;
    private IAudioPlayer? _crashPlayer;

    // background music (should RESUME on unpause)
    private IAudioPlayer? _musicPlayer;

    // engine rev / loop (gameplay only)
    private IAudioPlayer? _engineRevPlayer;

    // star (invincibility) music
    private IAudioPlayer? _starMusicPlayer;

    private bool _inMainMenu = true;
    private bool _settingsOpenedFromPause = false;

    private void SetEraseDataControlsVisible(bool isVisible)
    {
        if (EraseDataButton is not null)
            EraseDataButton.IsVisible = isVisible;
        if (EraseDataHintLabel is not null)
            EraseDataHintLabel.IsVisible = isVisible;
    }

    public MainPage()
    {
        InitializeComponent();

        // part 4A: HUD-only MVVM binding
        BindingContext = _hud;
        _hud.UpdateFromState(_engine.State);

        // part 4B: route UI commands (pause/gameover buttons) through the ViewModel
        _hud.PauseRequested += () =>
        {
            if (_inMainMenu) return;
            if (_engine.State.IsGameOver) return;
            if (_engine.State.IsPaused) return;
            PauseGameLoop();
        };

        _hud.ResumeRequested += () =>
        {
            if (_inMainMenu) return;
            if (_engine.State.IsGameOver) return;
            if (!_engine.State.IsPaused) return;
            ResumeGameLoop();
        };

        _hud.RestartRequested += () =>
        {
            HideGameOverOverlay();
            StartGameFromMenu();
        };

        _hud.SettingsRequested += async () =>
        {
            await Shell.Current.GoToAsync("settings");
        };

        _hud.MenuRequested += () =>
        {
            HideGameOverOverlay();
            ShowMainMenu();
        };


        // load audio settings
        _masterVolume = Preferences.Default.Get(PrefMasterVolume, DefaultMasterVolume);
        _sfxEnabled = Preferences.Default.Get(PrefSfxEnabled, true);

        // sync settings UI (safe even if overlays are currently hidden)
        if (VolumeSlider is not null)
            VolumeSlider.Value = Math.Clamp(_masterVolume, 0, 1);
        if (SfxSwitch is not null)
            SfxSwitch.IsToggled = _sfxEnabled;
        UpdateVolumeLabel();

        // load economy (with migration from older builds)
        LoadEconomyAndMigrateIfNeeded();

        // load selected car (persisted) but fall back if it's not owned
        var savedCar = Preferences.Default.Get(PrefSelectedCar, DefaultCar);

        // if the saved selection is a custom car but the file is missing, fall back safely.
        if (savedCar.Equals(CustomCarKey, StringComparison.OrdinalIgnoreCase))
        {
            var customPath = Preferences.Default.Get(PrefCustomCarPath, string.Empty);
            if (string.IsNullOrWhiteSpace(customPath) || !File.Exists(customPath))
            {
                savedCar = DefaultCar;
                Preferences.Default.Set(PrefSelectedCar, savedCar);
            }
        }


        // migration: older versions used redcar.png as the 25-coin unlock.
        if (savedCar.Equals("redcar.png", StringComparison.OrdinalIgnoreCase))
            savedCar = "purplecar.png";
        if (!IsCarOwned(savedCar))
        {
            savedCar = DefaultCar;
            Preferences.Default.Set(PrefSelectedCar, savedCar);
        }
        _engine.State.SelectedCarSprite = savedCar;
        UpdateSelectedCarLabel();
        UpdateCoinsAndUnlockUI();

        _drawable = new GameDrawable(_engine);
        GameView.Drawable = _drawable;

        // if your engine raises this event, we can play coin sound perfectly.
        // (If it doesn't exist, remove these two lines and call OnCoinCollected() from your engine instead.)
        _engine.CoinCollected += OnCoinCollected;
        _engine.FuelCollected += OnFuelCollected;
        _engine.PlayerDamaged += OnPlayerDamaged;
        _engine.PlayerDied += OnPlayerDied;
        _engine.InvincibilityStarted += OnInvincibilityStarted;
        _engine.InvincibilityEnded += OnInvincibilityEnded;

        _ = InitAudioAsync();

        // pAN (mouse drag on Windows + touch on mobile)
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, e) =>
        {
            if (_inMainMenu) return;
            if (_engine.State.IsGameOver || _engine.State.IsPaused) return;

            switch (e.StatusType)
            {
                case GestureStatus.Started:
                    _panTotalX = 0;
                    break;

                case GestureStatus.Running:
                    _panTotalX = e.TotalX;
                    break;

                case GestureStatus.Completed:
                case GestureStatus.Canceled:
                    const double threshold = 50; // pixels
                    if (_panTotalX <= -threshold) _engine.TryMovePlayerLane(-1);
                    else if (_panTotalX >= threshold) _engine.TryMovePlayerLane(+1);
                    break;
            }
        };

        // tAP to restart after game over (ignored while paused)
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, __) =>
        {
            if (_inMainMenu) return;
            if (_engine.State.IsPaused) return;

            if (_engine.State.IsGameOver)
            {
                if (!_gameOverShown)
                    ShowGameOverOverlay();
            }
        };

        // attach gestures to GameView (works fine in your setup)
        GameView.GestureRecognizers.Clear();
        GameView.GestureRecognizers.Add(pan);
        GameView.GestureRecognizers.Add(tap);

        // initial hint (menu will hide HUD anyway)
        UpdateHintForState(force: true);

        // game loop (~60fps)
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += (_, __) =>
        {
            var dt = _sw.Elapsed.TotalSeconds;
            _sw.Restart();

            _engine.Update(dt);
            _hud.UpdateFromState(_engine.State);

            // if the game ends, show the Game Over overlay.
            if (_engine.State.IsGameOver)
            {
                if (!_gameOverShown)
                    ShowGameOverOverlay();
            }
            // disable pause on game over
            PauseButton.IsEnabled = !_engine.State.IsGameOver;

            // only update hint text when state changes (so fade can work)
            UpdateHintForState(force: false);

            GameView.Invalidate();
        };

        // start on the main menu (do NOT start the game loop yet)
        StartGameFromMenu();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // settings are edited on a separate SettingsPage now, so re-load prefs here
        // whenever the game page becomes visible again.
        var newMaster = Preferences.Default.Get(PrefMasterVolume, DefaultMasterVolume);
        var newSfx = Preferences.Default.Get(PrefSfxEnabled, true);

        var settingsChanged = false;
        if (Math.Abs(newMaster - _masterVolume) > 0.0001)
        {
            _masterVolume = Math.Clamp(newMaster, 0, 1);
            settingsChanged = true;
        }

        if (newSfx != _sfxEnabled)
        {
            _sfxEnabled = newSfx;
            settingsChanged = true;

            if (!_sfxEnabled)
            {
                // if the invincibility track was playing, stop it immediately.
                StopStarMusicAndReset();
            }
        }

        if (settingsChanged)
            ApplyAudioSettings();

        if (!_inMainMenu)
        {
            ResumeGameLoop();
            EnsureCorrectAudioForState(); // play/resume correct audio when page appears
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        _timer.Stop();
        CancelHintFade();

        StopMusicAndReset();

        try { (_coinPlayer as IDisposable)?.Dispose(); } catch { }
        _coinPlayer = null;

        try { (_fuelPlayer as IDisposable)?.Dispose(); } catch { }
        _fuelPlayer = null;

        try { (_damagePlayer as IDisposable)?.Dispose(); } catch { }
        _damagePlayer = null;

        try { (_crashPlayer as IDisposable)?.Dispose(); } catch { }
        _crashPlayer = null;

        try { (_musicPlayer as IDisposable)?.Dispose(); } catch { }
        _musicPlayer = null;

        try { (_engineRevPlayer as IDisposable)?.Dispose(); } catch { }
        _engineRevPlayer = null;

        try { (_starMusicPlayer as IDisposable)?.Dispose(); } catch { }
        _starMusicPlayer = null;
    }

    // -----------------------
    // audio init
    // -----------------------
    private async Task InitAudioAsync()
    {
        try
        {
            // put coin_sfx.wav in Resources/Raw with Build Action = MauiAsset
            var stream = await FileSystem.OpenAppPackageFileAsync("Coinget.mp3");
            _coinPlayer = AudioManager.Current.CreatePlayer(stream);
            _coinPlayer.Volume = BaseCoinVol;

            var fuelStream = await FileSystem.OpenAppPackageFileAsync("drinksound.mp3");
            _fuelPlayer = AudioManager.Current.CreatePlayer(fuelStream);
            _fuelPlayer.Volume = BaseFuelVol;

            var dmgStream = await FileSystem.OpenAppPackageFileAsync("Damage.mp3");
            _damagePlayer = AudioManager.Current.CreatePlayer(dmgStream);
            _damagePlayer.Volume = BaseDamageVol;

            var crashStream = await FileSystem.OpenAppPackageFileAsync("carcrash.mp3");
            _crashPlayer = AudioManager.Current.CreatePlayer(crashStream);
            _crashPlayer.Volume = BaseCrashVol;

            // engine rev / loop (gameplay)
            var engineStream = await FileSystem.OpenAppPackageFileAsync("engine_rev.mp3");
            _engineRevPlayer = AudioManager.Current.CreatePlayer(engineStream);
            _engineRevPlayer.Loop = true;
            _engineRevPlayer.Volume = BaseEngineRevVol;

            // star / invincibility music
            var starStream = await FileSystem.OpenAppPackageFileAsync("starman.mp3");
            _starMusicPlayer = AudioManager.Current.CreatePlayer(starStream);
            _starMusicPlayer.Loop = true;
            _starMusicPlayer.Volume = BaseStarMusicVol;
        }
        catch
        {
            _coinPlayer = null; // SFX optional
            _fuelPlayer = null;
            _damagePlayer = null;
            _crashPlayer = null;
            _engineRevPlayer = null;
            _starMusicPlayer = null;
        }

        await InitMusicAsync();

        // apply persisted audio settings (volume + SFX toggle)
        ApplyAudioSettings();

        // if the game is already running when audio finishes loading, start the engine loop.
        EnsureEngineRevPlaying();
    }

    private async Task InitMusicAsync()
    {
        try
        {
            // put Airwaves.mp3 (or Airwaves.wav) in Resources/Raw with Build Action = MauiAsset
            Stream stream;
            var candidates = new[] { "Airwaves.mp3", "airwaves.mp3", "Airwaves.wav", "airwaves.wav" };
            stream = null!;
            Exception? last = null;
            foreach (var name in candidates)
            {
                try
                {
                    stream = await FileSystem.OpenAppPackageFileAsync(name);
                    last = null;
                    break;
                }
                catch (Exception ex)
                {
                    last = ex;
                }
            }
            if (last is not null)
                throw last;

            _musicPlayer = AudioManager.Current.CreatePlayer(stream);
            _musicPlayer.Loop = true;
            _musicPlayer.Volume = BaseMusicVol;

            // if we are already playing (not paused/gameover), start music now
            EnsureMusicPlaying();
        }
        catch
        {
            _musicPlayer = null;
        }
    }

    // -----------------------
    // settings / audio prefs
    // -----------------------
    private void SyncSettingsUi()
    {
        if (VolumeSlider is not null)
            VolumeSlider.Value = Math.Clamp(_masterVolume, 0, 1);
        if (SfxSwitch is not null)
            SfxSwitch.IsToggled = _sfxEnabled;

        UpdateVolumeLabel();
    }

    private void UpdateVolumeLabel()
    {
        if (VolumeValueLabel is null) return;
        var pct = (int)Math.Round(Math.Clamp(_masterVolume, 0, 1) * 100);
        VolumeValueLabel.Text = $"{pct}%";
    }

    private void ApplyAudioSettings()
    {
        var master = Math.Clamp(_masterVolume, 0, 1);
        var sfxMult = _sfxEnabled ? 1.0 : 0.0;

        try { if (_coinPlayer is not null) _coinPlayer.Volume = BaseCoinVol * master * sfxMult; } catch { }
        try { if (_fuelPlayer is not null) _fuelPlayer.Volume = BaseFuelVol * master * sfxMult; } catch { }
        try { if (_damagePlayer is not null) _damagePlayer.Volume = BaseDamageVol * master * sfxMult; } catch { }
        try { if (_crashPlayer is not null) _crashPlayer.Volume = BaseCrashVol * master * sfxMult; } catch { }
        try { if (_engineRevPlayer is not null) _engineRevPlayer.Volume = BaseEngineRevVol * master * sfxMult; } catch { }

        // music is independent of the SFX toggle
        try { if (_musicPlayer is not null) _musicPlayer.Volume = BaseMusicVol * master; } catch { }

        // star (invincibility) music is treated like SFX: it should not play when SFX is off.
        try { if (_starMusicPlayer is not null) _starMusicPlayer.Volume = BaseStarMusicVol * master * sfxMult; } catch { }
    }

    private void VolumeSlider_ValueChanged(object sender, ValueChangedEventArgs e)
    {
        _masterVolume = Math.Clamp(e.NewValue, 0, 1);
        Preferences.Default.Set(PrefMasterVolume, _masterVolume);
        UpdateVolumeLabel();
        ApplyAudioSettings();
    }

    private void SfxSwitch_Toggled(object sender, ToggledEventArgs e)
    {
        _sfxEnabled = e.Value;
        Preferences.Default.Set(PrefSfxEnabled, _sfxEnabled);
        ApplyAudioSettings();

        if (!_sfxEnabled)
        {
            // if the invincibility track was playing, stop it immediately.
            StopStarMusicAndReset();
        }

        // if gameplay is active, make sure the correct loops are playing/paused.
        if (!_inMainMenu && !_engine.State.IsPaused)
            EnsureCorrectAudioForState();
    }

    private async void EraseData_Clicked(object sender, EventArgs e)
    {
        var ok = await DisplayAlert(
            "Erase user data?",
            "This will reset your High Score, all coin totals, owned cars, selected car, and upgrades back to default.",
            "Erase",
            "Cancel");

        if (!ok) return;

        _coinsHeld = 0;
        _coinsEarnedTotal = 0;
        Preferences.Default.Set(PrefCoinsHeld, 0);
        Preferences.Default.Set(PrefCoinsEarnedTotal, 0);
        Preferences.Default.Set(PrefHighScore, 0);
        Preferences.Default.Set(PrefSelectedCar, DefaultCar);
        Preferences.Default.Set(PrefCustomCarPath, string.Empty);
        await _drawable.Sprites.SetCustomCarAsync(null);

        // owned cars
        Preferences.Default.Set(PrefOwnedPurple, false);
        Preferences.Default.Set(PrefOwnedBlue, false);
        Preferences.Default.Set(PrefOwnedGreen, false);

        // upgrades
        Preferences.Default.Set(PrefMaxHealth, BaseHealth);
        Preferences.Default.Set(PrefInvincibilityDurationSeconds, BaseInvSeconds);

        // reflect changes immediately in the current session
        _engine.State.HighScore = 0;
        _engine.State.IsNewHighScore = false;
        _engine.State.SelectedCarSprite = DefaultCar;

        UpdateSelectedCarLabel();
        UpdateCoinsAndUnlockUI();

        // update HUD via MVVM binding
        _hud.UpdateFromState(_engine.State);

        await DisplayAlert("Done", "Your data has been reset.", "OK");
    }

    // -----------------------
    // economy + upgrades
    // -----------------------
    private void LoadEconomyAndMigrateIfNeeded()
    {
        // migrate from older builds where "total_coins" existed and never decreased.
        var legacy = Preferences.Default.Get(PrefTotalCoinsLegacy, 0);

        _coinsHeld = Preferences.Default.Get(PrefCoinsHeld, legacy);
        _coinsEarnedTotal = Preferences.Default.Get(PrefCoinsEarnedTotal, _coinsHeld);
        PersistEconomy();

        // migrate unlocks: if the user previously had enough coins (old threshold system), mark cars as owned.
        var earned = _coinsEarnedTotal;
        if (earned >= CostPurple && !Preferences.Default.Get(PrefOwnedPurple, false)) Preferences.Default.Set(PrefOwnedPurple, true);
        if (earned >= CostBlue && !Preferences.Default.Get(PrefOwnedBlue, false)) Preferences.Default.Set(PrefOwnedBlue, true);
        if (earned >= CostGreen && !Preferences.Default.Get(PrefOwnedGreen, false)) Preferences.Default.Set(PrefOwnedGreen, true);

        // ensure upgrades have valid defaults
        var mh = Math.Clamp(Preferences.Default.Get(PrefMaxHealth, BaseHealth), BaseHealth, MaxHealthCap);
        Preferences.Default.Set(PrefMaxHealth, mh);

        var inv = Math.Clamp(Preferences.Default.Get(PrefInvincibilityDurationSeconds, BaseInvSeconds), BaseInvSeconds, MaxInvSeconds);
        Preferences.Default.Set(PrefInvincibilityDurationSeconds, inv);
    }

    private void PersistEconomy()
    {
        Preferences.Default.Set(PrefCoinsHeld, _coinsHeld);
        Preferences.Default.Set(PrefCoinsEarnedTotal, _coinsEarnedTotal);
    }

    private void AddCoins(int amount)
    {
        if (amount <= 0) return;
        _coinsHeld += amount;
        _coinsEarnedTotal += amount;
        PersistEconomy();
        UpdateEconomyLabels();
    }

    private void SpendCoins(int amount)
    {
        if (amount <= 0) return;
        _coinsHeld = Math.Max(0, _coinsHeld - amount);
        PersistEconomy();
        UpdateEconomyLabels();
    }

    private void UpdateEconomyLabels()
    {
        if (TotalCoinsMenuLabel is not null)
            TotalCoinsMenuLabel.Text = $"Coins Held: {_coinsHeld}";
        if (TotalCoinsShopLabel is not null)
            TotalCoinsShopLabel.Text = $"Coins Held: {_coinsHeld}";

        if (CoinsHeldSettingsLabel is not null)
            CoinsHeldSettingsLabel.Text = $"Coins Held: {_coinsHeld}";
        if (CoinsEarnedSettingsLabel is not null)
            CoinsEarnedSettingsLabel.Text = $"Total Coins Earned: {_coinsEarnedTotal}";
    }

    private void UpdateUpgradeUI()
    {
        var maxHealth = Math.Clamp(Preferences.Default.Get(PrefMaxHealth, BaseHealth), BaseHealth, MaxHealthCap);
        var inv = Math.Clamp(Preferences.Default.Get(PrefInvincibilityDurationSeconds, BaseInvSeconds), BaseInvSeconds, MaxInvSeconds);

        if (HealthUpgradeLabel is not null)
            HealthUpgradeLabel.Text = $"Max Health: {maxHealth}/{MaxHealthCap}";

        if (HealthUpgradeButton is not null)
        {
            var maxed = maxHealth >= MaxHealthCap;
            HealthUpgradeButton.IsEnabled = !maxed;
            HealthUpgradeButton.Text = maxed ? "MAX" : $"Buy ({UpgradeCost})";
        }

        if (InvUpgradeLabel is not null)
            InvUpgradeLabel.Text = $"Invincibility: {inv}s";

        if (InvUpgradeButton is not null)
        {
            var maxed = inv >= MaxInvSeconds;
            InvUpgradeButton.IsEnabled = !maxed;
            InvUpgradeButton.Text = maxed ? "MAX" : $"Buy ({UpgradeCost})";
        }
    }

    private static string CarDisplayName(string spriteFile)
    {
        return spriteFile.Trim().ToLowerInvariant() switch
        {
            "yellowcar.png" => "Yellow",
            "purplecar.png" => "Purple",
            "bluecar.png" => "Blue",
            "greencar.png" => "Green",
            _ => "Car"
        };
    }

    private async Task HandleCarClicked(string spriteFile)
    {
        // default car is free
        if (spriteFile.Equals(DefaultCar, StringComparison.OrdinalIgnoreCase))
        {
            SelectCar(DefaultCar);
            UpdateCoinsAndUnlockUI();
            return;
        }

        if (IsCarOwned(spriteFile))
        {
            SelectCar(spriteFile);
            UpdateCoinsAndUnlockUI();
            return;
        }

        var cost = UnlockCost(spriteFile);
        var name = CarDisplayName(spriteFile);

        if (_coinsHeld < cost)
        {
            await DisplayAlert("Not enough coins", $"You need {cost} coins to buy the {name} car.\n\nCoins Held: {_coinsHeld}", "OK");
            return;
        }

        var ok = await DisplayAlert("Confirm purchase", $"Buy the {name} car for {cost} coins?\n\nCoins Held: {_coinsHeld}", "Buy", "Cancel");
        if (!ok) return;

        SpendCoins(cost);
        SetCarOwned(spriteFile, true);
        SelectCar(spriteFile);
        UpdateCoinsAndUnlockUI();

        await DisplayAlert("Purchased!", $"You bought the {name} car.\n\nCoins Held: {_coinsHeld}", "OK");
    }

    private async void HealthUpgrade_Clicked(object sender, EventArgs e)
    {
        var current = Math.Clamp(Preferences.Default.Get(PrefMaxHealth, BaseHealth), BaseHealth, MaxHealthCap);
        if (current >= MaxHealthCap) return;

        if (_coinsHeld < UpgradeCost)
        {
            await DisplayAlert("Not enough coins", $"You need {UpgradeCost} coins to upgrade health.\n\nCoins Held: {_coinsHeld}", "OK");
            return;
        }

        var ok = await DisplayAlert("Confirm upgrade", $"Upgrade max health from {current} to {current + 1} for {UpgradeCost} coins?", "Buy", "Cancel");
        if (!ok) return;

        SpendCoins(UpgradeCost);
        Preferences.Default.Set(PrefMaxHealth, current + 1);
        UpdateUpgradeUI();

        await DisplayAlert("Upgrade purchased!", $"Max health is now {current + 1}/{MaxHealthCap}.\n\nCoins Held: {_coinsHeld}", "OK");
    }

    private async void InvUpgrade_Clicked(object sender, EventArgs e)
    {
        var current = Math.Clamp(Preferences.Default.Get(PrefInvincibilityDurationSeconds, BaseInvSeconds), BaseInvSeconds, MaxInvSeconds);
        if (current >= MaxInvSeconds) return;

        if (_coinsHeld < UpgradeCost)
        {
            await DisplayAlert("Not enough coins", $"You need {UpgradeCost} coins to upgrade invincibility.\n\nCoins Held: {_coinsHeld}", "OK");
            return;
        }

        var next = Math.Min(MaxInvSeconds, current + 2);
        var ok = await DisplayAlert("Confirm upgrade", $"Increase invincibility from {current}s to {next}s for {UpgradeCost} coins?", "Buy", "Cancel");
        if (!ok) return;

        SpendCoins(UpgradeCost);
        Preferences.Default.Set(PrefInvincibilityDurationSeconds, next);
        UpdateUpgradeUI();

        await DisplayAlert("Upgrade purchased!", $"Invincibility is now {next}s.\n\nCoins Held: {_coinsHeld}", "OK");
    }

    // -----------------------
    // coin SFX
    // -----------------------
    private void OnCoinCollected()
    {
        // bank coins immediately so quitting a run doesn't lose currency.
        AddCoins(amount: 1);

        try
        {
            if (!_sfxEnabled) return;
            if (_masterVolume <= 0) return;
            if (_coinPlayer is null) return;

            // stop resets to the beginning so it can be replayed instantly
            _coinPlayer.Stop();
            _coinPlayer.Play();
        }
        catch
        {
            // ignore
        }
    }

    private void OnFuelCollected()
    {
        try
        {
            if (!_sfxEnabled) return;
            if (_masterVolume <= 0) return;
            if (_fuelPlayer is null) return;

            // stop resets to the beginning so it can be replayed instantly
            _fuelPlayer.Stop();
            _fuelPlayer.Play();
        }
        catch
        {
            // ignore
        }
    }

    private void OnPlayerDamaged()
    {
        try
        {
            if (!_sfxEnabled) return;
            if (_masterVolume <= 0) return;
            if (_damagePlayer is null) return;

            _damagePlayer.Stop();
            _damagePlayer.Play();
        }
        catch
        {
            // ignore
        }
    }

    private void OnPlayerDied()
    {
        try
        {
            if (_sfxEnabled && _masterVolume > 0 && _crashPlayer is not null)
            {
                _crashPlayer.Stop();
                _crashPlayer.Play();
            }
        }
        catch { /* ignore */ }

        // coins are banked immediately on pickup.
    }


    // -----------------------
    // music control (pause should RESUME position)
    // -----------------------
    private void EnsureMusicPlaying()
    {
        try
        {
            if (_musicPlayer is null) return;
            if (_engine.State.IsPaused) return;
            if (_engine.State.IsGameOver) return;
            if (_engine.State.IsInvincible) return;

            // play() resumes from paused position
            if (!_musicPlayer.IsPlaying)
                _musicPlayer.Play();
        }
        catch { }
    }

    private void PauseMusic()
    {
        try { _musicPlayer?.Pause(); } catch { }
    }

    private void StopMusicAndReset()
    {
        try
        {
            if (_musicPlayer is null) return;
            _musicPlayer.Stop();  // reset
            _musicPlayer.Seek(0); // ensure start
        }
        catch { }
    }

    private void RestartMusicFromBeginning()
    {
        try
        {
            if (_musicPlayer is null)
            {
                // if audio hasn't loaded yet, try again once it is
                EnsureMusicPlaying();
                return;
            }

            _musicPlayer.Stop();
            _musicPlayer.Seek(0);
            _musicPlayer.Play();
        }
        catch { }
    }

    // -----------------------
    // engine rev control (pause should RESUME position)
    // -----------------------
    private void EnsureEngineRevPlaying()
    {
        try
        {
            if (_engineRevPlayer is null) return;
            if (_engine.State.IsPaused) return;
            if (_engine.State.IsGameOver) return;
            if (_engine.State.IsInvincible) return;

            if (!_engineRevPlayer.IsPlaying)
                _engineRevPlayer.Play();
        }
        catch { }
    }

    private void PauseEngineRev()
    {
        try { _engineRevPlayer?.Pause(); } catch { }
    }

    private void StopEngineRevAndReset()
    {
        try
        {
            if (_engineRevPlayer is null) return;
            _engineRevPlayer.Stop();
            _engineRevPlayer.Seek(0);
        }
        catch { }
    }

    private void RestartEngineRevFromBeginning()
    {
        try
        {
            if (_engineRevPlayer is null)
            {
                EnsureEngineRevPlaying();
                return;
            }

            _engineRevPlayer.Stop();
            _engineRevPlayer.Seek(0);
            _engineRevPlayer.Play();
        }
        catch { }
    }

    // -----------------------
    // main menu / overlays
    // -----------------------
    private void ShowMainMenu()
    {
        _inMainMenu = true;

        // mVVM state (keeps pause button hidden and prevents pause overlay showing)
        _hud.IsInMainMenu = true;
        _hud.IsPaused = false;
        _hud.IsGameOver = false;

        MainMenuOverlay.IsVisible = true;
        ShopOverlay.IsVisible = false;
        SettingsOverlay.IsVisible = false;
        HowToOverlay.IsVisible = false;

        _gameOverShown = false;

        HudGrid.IsVisible = false;

        // stop gameplay + music while in menu
        _engine.State.IsPaused = true;
        _timer.Stop();
        CancelHintFade();
        StopMusicAndReset();
        StopEngineRevAndReset();
        StopStarMusicAndReset();

        UpdateCoinsAndUnlockUI();

        // keep HUD up to date for when you hit Play (MVVM binding)
        _hud.UpdateFromState(_engine.State);
    }


    private void StartGameFromMenu()
    {
        _inMainMenu = false;

        // mVVM state (enables pause button, hides overlays)
        _hud.IsInMainMenu = false;
        _hud.IsPaused = false;
        _hud.IsGameOver = false;

        MainMenuOverlay.IsVisible = false;
        ShopOverlay.IsVisible = false;
        SettingsOverlay.IsVisible = false;
        HowToOverlay.IsVisible = false;

        _gameOverShown = false;

        HudGrid.IsVisible = true;

        // fresh run
        _crashPlayer?.Stop();
        _damagePlayer?.Stop();
        _coinPlayer?.Stop();

        _engine.Reset();

        // apply saved car (only if owned)
        var savedCar = Preferences.Default.Get(PrefSelectedCar, DefaultCar);

        // if the saved selection is a custom car but the file is missing, fall back safely.
        if (savedCar.Equals(CustomCarKey, StringComparison.OrdinalIgnoreCase))
        {
            var customPath = Preferences.Default.Get(PrefCustomCarPath, string.Empty);
            if (string.IsNullOrWhiteSpace(customPath) || !File.Exists(customPath))
            {
                savedCar = DefaultCar;
                Preferences.Default.Set(PrefSelectedCar, savedCar);
            }
        }

        _engine.State.SelectedCarSprite = IsCarOwned(savedCar) ? savedCar : DefaultCar;
        UpdateSelectedCarLabel();

        // fresh run HUD refresh (MVVM binding)
        _hud.UpdateFromState(_engine.State);

        PauseButton.Text = "Pause";
        PauseButton.IsEnabled = true;
        UpdateHintForState(force: true);

        ResumeGameLoop();
        RestartMusicFromBeginning();
        RestartEngineRevFromBeginning();
        StopStarMusicAndReset();
    }


    
    private static string FormatTime(double seconds)
    {
        var timeAlive = TimeSpan.FromSeconds(Math.Max(0, seconds));
        // show h:mm:ss if >= 1 hour, else m:ss
        return timeAlive.TotalHours >= 1
            ? timeAlive.ToString(@"h\:mm\:ss")
            : timeAlive.ToString(@"m\:ss");
    }

// -----------------------
    // game Over screen
// -----------------------
    private void ShowGameOverOverlay()
    {
        _gameOverShown = true;

        // stop gameplay + loops
        _engine.State.IsPaused = true;

        // mVVM overlay state
        _hud.IsPaused = false;
        _hud.IsGameOver = true;

        PauseButton.IsEnabled = false;

        // stop audio (crash SFX can finish playing)
        StopMusicAndReset();
        StopEngineRevAndReset();
        StopStarMusicAndReset();

        // update summary text (bound in XAML)
        _hud.UpdateGameOverSummary(_engine.State, _coinsHeld, FormatTime(_engine.State.TimeAlive));

        // stop updates and redraw once to show the overlay
        _timer.Stop();
        CancelHintFade();
        GameView.Invalidate();
    }


    private void HideGameOverOverlay()
    {
        _hud.IsGameOver = false;
        _gameOverShown = false;
    }


private void GameOverRestart_Clicked(object sender, EventArgs e)
{
    HideGameOverOverlay();
    StartGameFromMenu();
}

private void GameOverMenu_Clicked(object sender, EventArgs e)
{
    HideGameOverOverlay();
    ShowMainMenu();
}


    private void UpdateSelectedCarLabel()
    {
        var file = _engine.State.SelectedCarSprite ?? DefaultCar;
        var name = file.ToLowerInvariant() switch
        {
            "yellowcar.png" => "Yellow",
            "purplecar.png" => "Purple",
            "redcar.png" => "Purple", // legacy / migration safety
            "bluecar.png" => "Blue",
            "greencar.png" => "Green",
            CustomCarKey => "Custom",
            _ => "Yellow"
        };

        if (SelectedCarLabel is not null)
            SelectedCarLabel.Text = $"Selected: {name}";
    }

    private void SelectCar(string file)
    {
        // prevent selecting cars you don't own
        if (!IsCarOwned(file))
            return;

        _engine.State.SelectedCarSprite = file;
        Preferences.Default.Set(PrefSelectedCar, file);
        UpdateSelectedCarLabel();
    }

    private bool IsCarOwned(string? spriteFile)
    {
        var key = (spriteFile ?? string.Empty).Trim().ToLowerInvariant();
        return key switch
        {
            "purplecar.png" => Preferences.Default.Get(PrefOwnedPurple, false),
            "bluecar.png" => Preferences.Default.Get(PrefOwnedBlue, false),
            "greencar.png" => Preferences.Default.Get(PrefOwnedGreen, false),

            // custom is available if the file exists
            CustomCarKey => File.Exists(Preferences.Default.Get(PrefCustomCarPath, string.Empty)),

            _ => true, // yellow (and any unknown) is always available
        };
    }

    private void SetCarOwned(string spriteFile, bool owned)
    {
        var key = spriteFile.Trim().ToLowerInvariant();
        switch (key)
        {
            case "purplecar.png":
                Preferences.Default.Set(PrefOwnedPurple, owned);
                break;
            case "bluecar.png":
                Preferences.Default.Set(PrefOwnedBlue, owned);
                break;
            case "greencar.png":
                Preferences.Default.Set(PrefOwnedGreen, owned);
                break;
        }
    }

    private int UnlockCost(string spriteFile)
    {
        var key = spriteFile.Trim().ToLowerInvariant();
        return key switch
        {
            "purplecar.png" => CostPurple,
            "bluecar.png" => CostBlue,
            "greencar.png" => CostGreen,
            _ => 0
        };
    }

    private void UpdateCoinsAndUnlockUI()
{
    // menu/shop/settings labels
    UpdateEconomyLabels();

    // safety: never keep a locked car selected
    if (!IsCarOwned(_engine.State.SelectedCarSprite))
    {
        _engine.State.SelectedCarSprite = DefaultCar;
        Preferences.Default.Set(PrefSelectedCar, DefaultCar);
        Preferences.Default.Set(PrefCustomCarPath, string.Empty);
        _ = _drawable.Sprites.SetCustomCarAsync(null);
    }

    // car tiles (make selected/owned/locked obvious)
    SetCarUi(CarYellowFrame, CarYellowButton, CarYellowUnlockLabel, DefaultCar);
    SetCarUi(CarPurpleFrame, CarRedButton, CarRedUnlockLabel, "purplecar.png");
    SetCarUi(CarBlueFrame, CarBlueButton, CarBlueUnlockLabel, "bluecar.png");
    SetCarUi(CarGreenFrame, CarGreenButton, CarGreenUnlockLabel, "greencar.png");

    UpdateUpgradeUI();
    UpdateSelectedCarLabel();
}

private void SetCarUi(Frame? frame, ImageButton? btn, Label? label, string spriteFile)
{
    var owned = IsCarOwned(spriteFile);
    var selected = string.Equals((_engine.State.SelectedCarSprite ?? DefaultCar), spriteFile, StringComparison.OrdinalIgnoreCase);
    var cost = UnlockCost(spriteFile);

    if (btn is not null)
    {
        // keep buttons clickable so locked cars can be purchased.
        btn.IsEnabled = true;
        btn.Opacity = owned ? 1.0 : 0.55;
    }

    if (label is not null)
    {
        if (spriteFile.Equals(DefaultCar, StringComparison.OrdinalIgnoreCase))
            label.Text = selected ? "Selected" : "Default";
        else
            label.Text = selected ? "Selected" : (owned ? "Owned" : $"{cost} coins");

        label.Opacity = 0.9;
    }

    if (frame is not null)
    {
        // selected: gold border, Owned: green border, Locked: grey border
        frame.BorderColor = selected ? Colors.Gold : (owned ? Color.FromArgb("#4CAF50") : Color.FromArgb("#4A4A4A"));
        frame.BackgroundColor = selected ? Color.FromArgb("#252525") : Color.FromArgb("#141414");
    }
}

// xAML button handlers
    private void Play_Clicked(object sender, EventArgs e) => StartGameFromMenu();

    private async void Settings_Clicked(object sender, EventArgs e)
    {
        // settings live on a dedicated page now.
        await Shell.Current.GoToAsync("settings");
    }

    private void Shop_Clicked(object sender, EventArgs e)
    {
        MainMenuOverlay.IsVisible = false;
        SettingsOverlay.IsVisible = false;
        HowToOverlay.IsVisible = false;
        ShopOverlay.IsVisible = true;

        UpdateSelectedCarLabel();
        UpdateCoinsAndUnlockUI();
    }

    private void SettingsBack_Clicked(object sender, EventArgs e)
    {
        SettingsOverlay.IsVisible = false;

        if (_settingsOpenedFromPause)
        {
            // return to pause menu
            _hud.IsPaused = true;
        }
        else
        {
            // return to main menu
            MainMenuOverlay.IsVisible = true;
        }
    }

    private void ShopBack_Clicked(object sender, EventArgs e)
    {
        ShopOverlay.IsVisible = false;
        MainMenuOverlay.IsVisible = true;
    }

    private void HowTo_Clicked(object sender, EventArgs e)
    {
        MainMenuOverlay.IsVisible = false;
        ShopOverlay.IsVisible = false;
        SettingsOverlay.IsVisible = false;
        HowToOverlay.IsVisible = true;
    }

    private void HowToClose_Clicked(object sender, EventArgs e)
    {
        HowToOverlay.IsVisible = false;
        MainMenuOverlay.IsVisible = true;
    }

    private async void CarYellow_Clicked(object sender, EventArgs e) => await HandleCarClicked(DefaultCar);
    private async void CarRed_Clicked(object sender, EventArgs e) => await HandleCarClicked("purplecar.png");
    private async void CarBlue_Clicked(object sender, EventArgs e) => await HandleCarClicked("bluecar.png");

    private async void CustomCarUpload_Clicked(object sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Pick an image for your car",
                FileTypes = FilePickerFileType.Images
            });

            if (result is null)
                return;

            var ext = Path.GetExtension(result.FileName);
            if (string.IsNullOrWhiteSpace(ext))
                ext = ".png";

            // copy into app data so we can read it reliably later.
            var destPath = Path.Combine(FileSystem.AppDataDirectory, $"customcar{ext.ToLowerInvariant()}");
            await using (var src = await result.OpenReadAsync())
            await using (var dst = File.Open(destPath, FileMode.Create, FileAccess.Write))
            {
                await src.CopyToAsync(dst);
            }

            Preferences.Default.Set(PrefCustomCarPath, destPath);

            // warm the sprite cache so it shows instantly.
            await _drawable.Sprites.SetCustomCarAsync(destPath);

            // select it
            _engine.State.SelectedCarSprite = CustomCarKey;
            Preferences.Default.Set(PrefSelectedCar, CustomCarKey);

            UpdateSelectedCarLabel();
            GameView.Invalidate();
        }
        catch (Exception ex)
        {
            await DisplayAlert("Couldn't use that image", ex.Message, "OK");
        }
    }

    private async void CarGreen_Clicked(object sender, EventArgs e) => await HandleCarClicked("greencar.png");

    // -----------------------
    // pause button
    // -----------------------
    private void PauseButton_Clicked(object sender, EventArgs e)
    {
        if (_engine.State.IsGameOver) return;

        // use the PauseOverlay for controls; pause button only pauses.
        if (!_engine.State.IsPaused)
            PauseGameLoop();
    }

    private void PauseGameLoop()
    {
        _engine.State.IsPaused = true;
        _hud.IsPaused = true;

        PauseButton.Text = "Pause";
        UpdateHintForState(force: true);

        // ✅ Pause music so it resumes at same spot
        PauseMusic();
        PauseEngineRev();
        PauseStarMusic();

        // stop updates but redraw once to show the PAUSED overlay
        _timer.Stop();
        GameView.Invalidate();
    }


    private void ResumeGameLoop()
    {
        _engine.State.IsPaused = false;
        _hud.IsPaused = false;

        PauseButton.Text = "Pause";
        UpdateHintForState(force: true);

        _sw.Restart();
        _timer.Start();
        GameView.Invalidate();

        // ✅ Resume music where it paused
        EnsureCorrectAudioForState();
    }


    // -----------------------
    // pause overlay buttons
    // -----------------------
    private void PauseResume_Clicked(object sender, EventArgs e)
    {
        if (_engine.State.IsGameOver) return;
        ResumeGameLoop();
    }

    private void PauseRestart_Clicked(object sender, EventArgs e)
    {
        // restart the run from scratch and continue playing
        StartGameFromMenu();
    }

    private async void PauseSettings_Clicked(object sender, EventArgs e)
    {
        // settings live on a dedicated page now.
        await Shell.Current.GoToAsync("settings");
    }

    private void PauseMainMenu_Clicked(object sender, EventArgs e)
    {
        ShowMainMenu();
    }

    // -----------------------
    // invincibility (star) music switching
    // -----------------------
    private void OnInvincibilityStarted()
    {
        // if SFX is off, don't switch to the star track.
        if (!_sfxEnabled) return;

        // pause normal audio, play star music from the beginning
        PauseMusic();
        PauseEngineRev();
        RestartStarMusicFromBeginning();
    }

    private void OnInvincibilityEnded()
    {
        StopStarMusicAndReset();
        // resume normal audio if still playing
        EnsureMusicPlaying();
        EnsureEngineRevPlaying();
    }

    private void EnsureCorrectAudioForState()
    {
        // if SFX is off, we never want the invincibility track to override normal music.
        if (_engine.State.IsInvincible && _sfxEnabled)
        {
            // star music overrides
            PauseMusic();
            PauseEngineRev();
            if (_starMusicPlayer is not null && !_starMusicPlayer.IsPlaying)
                _starMusicPlayer.Play();
        }
        else
        {
            StopStarMusicAndReset();
            EnsureMusicPlaying();
            EnsureEngineRevPlaying();
        }
    }

    private void PauseStarMusic()
    {
        try { _starMusicPlayer?.Pause(); } catch { }
    }

    private void StopStarMusicAndReset()
    {
        try
        {
            if (_starMusicPlayer is null) return;
            _starMusicPlayer.Stop();
            _starMusicPlayer.Seek(0);
        }
        catch { }
    }

    private void RestartStarMusicFromBeginning()
    {
        try
        {
            if (_starMusicPlayer is null) return;
            _starMusicPlayer.Stop();
            _starMusicPlayer.Seek(0);
            _starMusicPlayer.Play();
        }
        catch { }
    }

    // -----------------------
    // hint fade logic
    // -----------------------
    private void UpdateHintForState(bool force)
    {
        var go = _engine.State.IsGameOver;
        var paused = _engine.State.IsPaused;

        if (!force && go == _lastGameOver && paused == _lastPaused)
            return;

        _lastGameOver = go;
        _lastPaused = paused;

        if (go)
        {
            CancelHintFade();
            HintLabel.Opacity = 1;
            HintLabel.Text = "Game Over";
            return;
        }

        if (paused)
        {
            CancelHintFade();
            HintLabel.Opacity = 1;
            HintLabel.Text = "Paused";
            return;
        }

        // running: show controls briefly then fade
        HintLabel.Text = "Drag left/right to change lanes";
        HintLabel.Opacity = 1;
        StartHintFade();
    }

    private void StartHintFade()
    {
        CancelHintFade();
        _hintFadeCts = new CancellationTokenSource();
        var token = _hintFadeCts.Token;

        _ = FadeHintAsync(token);
    }

    private async Task FadeHintAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(5000, token);
            if (token.IsCancellationRequested) return;
            await HintLabel.FadeTo(0, 700, Easing.CubicOut);
        }
        catch
        {
            // ignore cancellations
        }
    }

    private void CancelHintFade()
    {
        try { _hintFadeCts?.Cancel(); } catch { }
        _hintFadeCts = null;
    }
}
