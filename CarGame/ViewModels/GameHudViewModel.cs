using System;
using System.Windows.Input;
using CarGame.Game;
using Microsoft.Maui.Controls;

namespace CarGame.ViewModels;

/// <summary>
/// part 4A/4B: HUD + Pause/GameOver MVVM.
/// gameplay logic stays in the engine + code-behind; UI state is driven via bindable properties.
/// button presses are surfaced as commands that raise request events (handled in the page code-behind).
/// </summary>
public sealed class GameHudViewModel : BaseViewModel
{
    // --- HUD state ---
    private int _score;
    private int _highScore;
    private int _coinsThisRun;
    private string _livesText = string.Empty;
    private bool _isInvincible;
    private string _invincibleText = "INV";

    // --- Overlay state ---
    private bool _isPaused;
    private bool _isGameOver;
    private bool _isInMainMenu = true;

    // --- Game over summary ---
    private string _finalScoreText = "Score: 0";
    private string _finalTimeAliveText = "Time Alive: 0:00";
    private string _finalHighScoreText = "High Score: 0";
    private string _finalCoinsRunText = "Coins this run: 0";
    private string _finalCoinsHeldText = "Coins held: 0";
    private bool _isNewHighScore;

    // requests (handled by MainPage.xaml.cs)
    public event Action? PauseRequested;
    public event Action? ResumeRequested;
    public event Action? RestartRequested;
    public event Action? SettingsRequested;
    public event Action? MenuRequested;

    public GameHudViewModel()
    {
        PauseCommand = new Command(() => PauseRequested?.Invoke());
        ResumeCommand = new Command(() => ResumeRequested?.Invoke());
        RestartCommand = new Command(() => RestartRequested?.Invoke());
        SettingsCommand = new Command(() => SettingsRequested?.Invoke());
        MenuCommand = new Command(() => MenuRequested?.Invoke());
    }

    // --- Commands ---
    public ICommand PauseCommand { get; }
    public ICommand ResumeCommand { get; }
    public ICommand RestartCommand { get; }
    public ICommand SettingsCommand { get; }
    public ICommand MenuCommand { get; }

    // --- HUD properties ---
    public int Score
    {
        get => _score;
        set
        {
            if (SetProperty(ref _score, value))
            {
                OnPropertyChanged(nameof(ScoreText));
                OnPropertyChanged(nameof(FinalScoreText));
            }
        }
    }

    public string ScoreText => $"Score: {Score}";

    public int HighScore
    {
        get => _highScore;
        set
        {
            if (SetProperty(ref _highScore, value))
            {
                OnPropertyChanged(nameof(HighScoreText));
                OnPropertyChanged(nameof(FinalHighScoreText));
            }
        }
    }

    public string HighScoreText => $"High: {HighScore}";

    public int CoinsThisRun
    {
        get => _coinsThisRun;
        set
        {
            if (SetProperty(ref _coinsThisRun, value))
            {
                OnPropertyChanged(nameof(CoinsText));
                OnPropertyChanged(nameof(FinalCoinsRunText));
            }
        }
    }

    public string CoinsText => $"Coins: {CoinsThisRun}";

    public string LivesText
    {
        get => _livesText;
        set => SetProperty(ref _livesText, value);
    }

    public bool IsInvincible
    {
        get => _isInvincible;
        set => SetProperty(ref _isInvincible, value);
    }

    public string InvincibleText
    {
        get => _invincibleText;
        set => SetProperty(ref _invincibleText, value);
    }

    // --- Pause/GameOver visibility ---
    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            if (SetProperty(ref _isPaused, value))
                OnPropertyChanged(nameof(CanPause));
        }
    }

    public bool IsGameOver
    {
        get => _isGameOver;
        set
        {
            if (SetProperty(ref _isGameOver, value))
                OnPropertyChanged(nameof(CanPause));
        }
    }

    public bool IsInMainMenu
    {
        get => _isInMainMenu;
        set
        {
            if (SetProperty(ref _isInMainMenu, value))
                OnPropertyChanged(nameof(CanPause));
        }
    }

    /// <summary>
    /// pause button should only show when the run is active (not paused, not game over).
    /// </summary>
    public bool CanPause => !IsInMainMenu && !IsPaused && !IsGameOver;

    // --- Game over summary bound text ---
    public string FinalScoreText
    {
        get => _finalScoreText;
        set => SetProperty(ref _finalScoreText, value);
    }

    public string FinalTimeAliveText
    {
        get => _finalTimeAliveText;
        set => SetProperty(ref _finalTimeAliveText, value);
    }

    public string FinalHighScoreText
    {
        get => _finalHighScoreText;
        set => SetProperty(ref _finalHighScoreText, value);
    }

    public string FinalCoinsRunText
    {
        get => _finalCoinsRunText;
        set => SetProperty(ref _finalCoinsRunText, value);
    }

    public string FinalCoinsHeldText
    {
        get => _finalCoinsHeldText;
        set => SetProperty(ref _finalCoinsHeldText, value);
    }

    public bool IsNewHighScore
    {
        get => _isNewHighScore;
        set => SetProperty(ref _isNewHighScore, value);
    }

    public void UpdateFromState(GameState state)
    {
        // during gameplay, keep HUD in sync.
        Score = state.Score;
        HighScore = state.HighScore;
        CoinsThisRun = state.CoinsThisRun;
        LivesText = state.LivesText;
        IsInvincible = state.IsInvincible;
        InvincibleText = state.IsInvincible ? $"INV: {state.InvincibleSecondsLeft}s" : "INV";
    }

    public void UpdateGameOverSummary(GameState state, int coinsHeld, string timeAliveText)
    {
        FinalScoreText = $"Score: {state.Score}";
        FinalTimeAliveText = $"Time Alive: {timeAliveText}";
        FinalHighScoreText = $"High Score: {state.HighScore}";
        FinalCoinsRunText = $"Coins this run: {state.CoinsThisRun}";
        FinalCoinsHeldText = $"Coins held: {coinsHeld}";
        IsNewHighScore = state.IsNewHighScore;
    }
}
