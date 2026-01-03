using CarGame.Game;
using CarGame.UI;
using Microsoft.Maui.Storage;
using Plugin.Maui.Audio;
using System.Diagnostics;

namespace CarGame;

public partial class MainPage : ContentPage
{
    private const string PrefSelectedCar = "selected_car_sprite";
    private const string PrefTotalCoins = "total_coins";
    private const string DefaultCar = "yellowcar.png";

    // Unlock thresholds
    // 25 coins unlocks Purple (replaced Red)
    private const int UnlockPurpleCoins = 25;
    private const int UnlockBlueCoins = 50;
    private const int UnlockGreenCoins = 75;

    private int _totalCoins;

    private readonly GameEngine _engine = new();
    private readonly GameDrawable _drawable;

    private readonly IDispatcherTimer _timer;
    private readonly Stopwatch _sw = new();

    // For mouse + touch on Windows (PanGesture works reliably)
    private double _panTotalX = 0;

    // Hint fade
    private CancellationTokenSource? _hintFadeCts;
    private bool _lastGameOver;
    private bool _lastPaused;

    // Coin SFX
    private IAudioPlayer? _coinPlayer;

    // Damage / crash SFX
    private IAudioPlayer? _damagePlayer;
    private IAudioPlayer? _crashPlayer;

    // Background music (should RESUME on unpause)
    private IAudioPlayer? _musicPlayer;

    private bool _inMainMenu = true;

    public MainPage()
    {
        InitializeComponent();

        // Load persisted total coins (currency)
        _totalCoins = Preferences.Default.Get(PrefTotalCoins, 0);

        // Load selected car (persisted) but fall back if it's still locked
        var savedCar = Preferences.Default.Get(PrefSelectedCar, DefaultCar);

        // Migration: older versions used redcar.png as the 25-coin unlock.
        if (savedCar.Equals("redcar.png", StringComparison.OrdinalIgnoreCase))
            savedCar = "purplecar.png";
        if (!IsCarUnlocked(savedCar))
        {
            savedCar = DefaultCar;
            Preferences.Default.Set(PrefSelectedCar, savedCar);
        }
        _engine.State.SelectedCarSprite = savedCar;
        UpdateSelectedCarLabel();
        UpdateCoinsAndUnlockUI();

        _drawable = new GameDrawable(_engine);
        GameView.Drawable = _drawable;

        // If your engine raises this event, we can play coin sound perfectly.
        // (If it doesn't exist, remove these two lines and call OnCoinCollected() from your engine instead.)
        _engine.CoinCollected += OnCoinCollected;
        _engine.PlayerDamaged += OnPlayerDamaged;
        _engine.PlayerDied += OnPlayerDied;

        _ = InitAudioAsync();

        // PAN (mouse drag on Windows + touch on mobile)
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

        // TAP to restart after game over (ignored while paused)
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, __) =>
        {
            if (_inMainMenu) return;
            if (_engine.State.IsPaused) return;

            if (_engine.State.IsGameOver)
            {
                // Game over now returns to the main menu.
                // (Crash SFX can keep playing; menu is started from the Play button.)
                ShowMainMenu();
            }
        };

        // Attach gestures to GameView (works fine in your setup)
        GameView.GestureRecognizers.Clear();
        GameView.GestureRecognizers.Add(pan);
        GameView.GestureRecognizers.Add(tap);

        // Initial hint (menu will hide HUD anyway)
        UpdateHintForState(force: true);

        // Game loop (~60fps)
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += (_, __) =>
        {
            var dt = _sw.Elapsed.TotalSeconds;
            _sw.Restart();

            _engine.Update(dt);

            ScoreLabel.Text = $"Score: {_engine.State.Score}";
            CoinsLabel.Text = $"Coins: {_engine.State.CoinsThisRun}";
            HighScoreLabel.Text = $"High: {_engine.State.HighScore}";
            LivesLabel.Text = _engine.State.LivesText;

            // If the game ends, return to main menu.
            // (Crash SFX can keep playing; we only stop gameplay + music.)
            if (_engine.State.IsGameOver)
            {
                _engine.State.IsPaused = false;
                PauseButton.Text = "Pause";
                StopMusicAndReset();

                if (!_inMainMenu)
                    ShowMainMenu();
            }

            // Disable pause on game over
            PauseButton.IsEnabled = !_engine.State.IsGameOver;

            // Only update hint text when state changes (so fade can work)
            UpdateHintForState(force: false);

            GameView.Invalidate();
        };

        // Start on the main menu (do NOT start the game loop yet)
        ShowMainMenu();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (!_inMainMenu)
        {
            ResumeGameLoop();
            EnsureMusicPlaying(); // play/resume when page appears
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

        try { (_damagePlayer as IDisposable)?.Dispose(); } catch { }
        _damagePlayer = null;

        try { (_crashPlayer as IDisposable)?.Dispose(); } catch { }
        _crashPlayer = null;

        try { (_musicPlayer as IDisposable)?.Dispose(); } catch { }
        _musicPlayer = null;
    }

    // -----------------------
    // Audio init
    // -----------------------
    private async Task InitAudioAsync()
    {
        try
        {
            // Put coin_sfx.wav in Resources/Raw with Build Action = MauiAsset
            var stream = await FileSystem.OpenAppPackageFileAsync("Coinget.mp3");
            _coinPlayer = AudioManager.Current.CreatePlayer(stream);
            _coinPlayer.Volume = 0.7;

            var dmgStream = await FileSystem.OpenAppPackageFileAsync("Damage.mp3");
            _damagePlayer = AudioManager.Current.CreatePlayer(dmgStream);
            _damagePlayer.Volume = 0.9;

            var crashStream = await FileSystem.OpenAppPackageFileAsync("carcrash.mp3");
            _crashPlayer = AudioManager.Current.CreatePlayer(crashStream);
            _crashPlayer.Volume = 1.0;
        }
        catch
        {
            _coinPlayer = null; // SFX optional
            _damagePlayer = null;
            _crashPlayer = null;
        }

        await InitMusicAsync();
    }

    private async Task InitMusicAsync()
    {
        try
        {
            // Put Airwaves.mp3 (or Airwaves.wav) in Resources/Raw with Build Action = MauiAsset
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
            _musicPlayer.Volume = 0.5;

            // If we are already playing (not paused/gameover), start music now
            EnsureMusicPlaying();
        }
        catch
        {
            _musicPlayer = null;
        }
    }

    // -----------------------
    // Coin SFX
    // -----------------------
    private void OnCoinCollected()
    {
        try
        {
            if (_coinPlayer is null) return;

            // Stop resets to the beginning so it can be replayed instantly
            _coinPlayer.Stop();
            _coinPlayer.Play();
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
            if (_crashPlayer is not null)
            {
                _crashPlayer.Stop();
                _crashPlayer.Play();
            }
        }
        catch { /* ignore */ }

        // Bank coins for this run into total coins (persisted)
        try
        {
            if (_engine.State.CoinsThisRun > 0)
            {
                _totalCoins += _engine.State.CoinsThisRun;
                Preferences.Default.Set(PrefTotalCoins, _totalCoins);
                UpdateCoinsAndUnlockUI();
            }
        }
        catch { }
    }


    // -----------------------
    // Music control (pause should RESUME position)
    // -----------------------
    private void EnsureMusicPlaying()
    {
        try
        {
            if (_musicPlayer is null) return;
            if (_engine.State.IsPaused) return;
            if (_engine.State.IsGameOver) return;

            // Play() resumes from paused position
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
                // If audio hasn't loaded yet, try again once it is
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
    // Main menu / overlays
    // -----------------------
    private void ShowMainMenu()
    {
        _inMainMenu = true;

        MainMenuOverlay.IsVisible = true;
        SettingsOverlay.IsVisible = false;
        HowToOverlay.IsVisible = false;

        HudGrid.IsVisible = false;
        PauseButton.IsVisible = false;

        // Stop gameplay + music while in menu
        _engine.State.IsPaused = true;
        _timer.Stop();
        CancelHintFade();
        StopMusicAndReset();

        UpdateCoinsAndUnlockUI();

        // Keep labels up to date for when you hit Play
        ScoreLabel.Text = $"Score: {_engine.State.Score}";
        HighScoreLabel.Text = $"High: {_engine.State.HighScore}";
        LivesLabel.Text = _engine.State.LivesText;
    }

    private void StartGameFromMenu()
    {
        _inMainMenu = false;

        MainMenuOverlay.IsVisible = false;
        SettingsOverlay.IsVisible = false;
        HowToOverlay.IsVisible = false;

        HudGrid.IsVisible = true;
        PauseButton.IsVisible = true;

        // Fresh run
        _crashPlayer?.Stop();
        _damagePlayer?.Stop();
        _coinPlayer?.Stop();

        _engine.Reset();
        // Apply saved car (only if unlocked)
        var savedCar = Preferences.Default.Get(PrefSelectedCar, DefaultCar);
        _engine.State.SelectedCarSprite = IsCarUnlocked(savedCar) ? savedCar : DefaultCar;
        UpdateSelectedCarLabel();

        // Fresh run coin counter
        CoinsLabel.Text = $"Coins: {_engine.State.CoinsThisRun}";

        PauseButton.Text = "Pause";
        PauseButton.IsEnabled = true;
        UpdateHintForState(force: true);

        ResumeGameLoop();
        RestartMusicFromBeginning();
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
            _ => "Yellow"
        };

        if (SelectedCarLabel is not null)
            SelectedCarLabel.Text = $"Selected: {name}";
    }

    private void SelectCar(string file)
    {
        // Prevent selecting locked cars (also keeps it safe if button is somehow enabled)
        if (!IsCarUnlocked(file))
            return;

        _engine.State.SelectedCarSprite = file;
        Preferences.Default.Set(PrefSelectedCar, file);
        UpdateSelectedCarLabel();
    }

    private bool IsCarUnlocked(string? spriteFile)
    {
        var key = (spriteFile ?? string.Empty).Trim().ToLowerInvariant();
        return key switch
        {
            "purplecar.png" => _totalCoins >= UnlockPurpleCoins,
            "bluecar.png" => _totalCoins >= UnlockBlueCoins,
            "greencar.png" => _totalCoins >= UnlockGreenCoins,
            _ => true, // yellow (and any unknown) is always available
        };
    }

    private int UnlockCost(string spriteFile)
    {
        var key = spriteFile.Trim().ToLowerInvariant();
        return key switch
        {
            "purplecar.png" => UnlockPurpleCoins,
            "bluecar.png" => UnlockBlueCoins,
            "greencar.png" => UnlockGreenCoins,
            _ => 0
        };
    }

    private void UpdateCoinsAndUnlockUI()
    {
        // Menu/settings labels
        if (TotalCoinsMenuLabel is not null)
            TotalCoinsMenuLabel.Text = $"Total Coins: {_totalCoins}";

        if (TotalCoinsSettingsLabel is not null)
            TotalCoinsSettingsLabel.Text = $"Total Coins: {_totalCoins}";

        // Lock/unlock buttons
        // Yellow is the default car and is always available.
        if (CarYellowButton is not null)
        {
            CarYellowButton.IsEnabled = true;
            CarYellowButton.Opacity = 1.0;
        }
        if (CarYellowUnlockLabel is not null)
        {
            CarYellowUnlockLabel.Text = "Default";
            CarYellowUnlockLabel.Opacity = 1.0;
        }

        SetCarLockState(CarRedButton, CarRedUnlockLabel, "purplecar.png");
        SetCarLockState(CarBlueButton, CarBlueUnlockLabel, "bluecar.png");
        SetCarLockState(CarGreenButton, CarGreenUnlockLabel, "greencar.png");

        // If currently-selected car is locked (shouldn't happen, but keeps it safe)
        if (!IsCarUnlocked(_engine.State.SelectedCarSprite))
        {
            _engine.State.SelectedCarSprite = DefaultCar;
            Preferences.Default.Set(PrefSelectedCar, DefaultCar);
            UpdateSelectedCarLabel();
        }
    }

    private void SetCarLockState(ImageButton? btn, Label? label, string spriteFile)
    {
        var cost = UnlockCost(spriteFile);
        var unlocked = IsCarUnlocked(spriteFile);

        if (btn is not null)
        {
            btn.IsEnabled = unlocked;
            btn.Opacity = unlocked ? 1.0 : 0.35;
        }

        if (label is not null)
        {
            label.Text = unlocked ? "Unlocked" : $"{cost} coins";
            label.Opacity = unlocked ? 1.0 : 0.8;
        }
    }

    // XAML button handlers
    private void Play_Clicked(object sender, EventArgs e) => StartGameFromMenu();

    private void Settings_Clicked(object sender, EventArgs e)
    {
        MainMenuOverlay.IsVisible = false;
        SettingsOverlay.IsVisible = true;
        HowToOverlay.IsVisible = false;
        UpdateSelectedCarLabel();
        UpdateCoinsAndUnlockUI();
    }

    private void SettingsBack_Clicked(object sender, EventArgs e)
    {
        SettingsOverlay.IsVisible = false;
        MainMenuOverlay.IsVisible = true;
    }

    private void HowTo_Clicked(object sender, EventArgs e)
    {
        MainMenuOverlay.IsVisible = false;
        SettingsOverlay.IsVisible = false;
        HowToOverlay.IsVisible = true;
    }

    private void HowToClose_Clicked(object sender, EventArgs e)
    {
        HowToOverlay.IsVisible = false;
        MainMenuOverlay.IsVisible = true;
    }

    private void CarYellow_Clicked(object sender, EventArgs e) => SelectCar(DefaultCar);
    private void CarRed_Clicked(object sender, EventArgs e) => SelectCar("purplecar.png");
    private void CarBlue_Clicked(object sender, EventArgs e) => SelectCar("bluecar.png");
    private void CarGreen_Clicked(object sender, EventArgs e) => SelectCar("greencar.png");

    // -----------------------
    // Pause button
    // -----------------------
    private void PauseButton_Clicked(object sender, EventArgs e)
    {
        if (_engine.State.IsGameOver) return;

        if (_engine.State.IsPaused)
            ResumeGameLoop();
        else
            PauseGameLoop();
    }

    private void PauseGameLoop()
    {
        _engine.State.IsPaused = true;
        PauseButton.Text = "Resume";
        UpdateHintForState(force: true);

        // ✅ Pause music so it resumes at same spot
        PauseMusic();

        // Stop updates but redraw once to show the PAUSED overlay
        _timer.Stop();
        GameView.Invalidate();
    }

    private void ResumeGameLoop()
    {
        _engine.State.IsPaused = false;
        PauseButton.Text = "Pause";
        UpdateHintForState(force: true);

        _sw.Restart();
        _timer.Start();
        GameView.Invalidate();

        // ✅ Resume music where it paused
        EnsureMusicPlaying();
    }

    // -----------------------
    // Hint fade logic
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

        // Running: show controls briefly then fade
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
