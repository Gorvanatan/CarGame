using System;
using System.Windows.Input;
using CarGame.Game;
using CarGame.Services;
using Microsoft.Maui.Controls;

namespace CarGame.ViewModels;

/// <summary>
/// game page viewmodel that exposes bindable hud/overlay state and command actions.
/// </summary>
public sealed class GamePageViewModel : BaseViewModel
{
    private readonly GameEngine _engine;
    private readonly IProfileService _profile;

    // hud state
    private int _score;
    private int _highScore;
    private int _coinsThisRun;
    private string _livesText = string.Empty;
    private bool _isInvincible;
    private string _invincibleText = "INV";
    private string _hintText = "Swipe left/right to change lanes";

    // overlay state
    private bool _isPaused;
    private bool _isGameOver;

    // game over summary
    private string _finalScoreText = "Score: 0";
    private string _finalTimeAliveText = "Time Alive: 0:00";
    private string _finalHighScoreText = "High Score: 0";
    private string _finalCoinsRunText = "Coins this run: 0";
    private string _finalCoinsHeldText = "Coins held: 0";
    private bool _isNewHighScore;

    private bool _lastGameOver;

    public GamePageViewModel(GameEngine engine, IProfileService profile)
    {
        _engine = engine;
        _profile = profile;

        PauseCommand = new Command(() => SetPaused(true), () => CanPause);
        ResumeCommand = new Command(() => SetPaused(false));
        RestartCommand = new Command(() => RestartRequested?.Invoke());
        SettingsCommand = new Command(() => NavigateRequested?.Invoke("settings"));
        MenuCommand = new Command(() => NavigateRequested?.Invoke("//menu"));

        // initial sync
        SyncFromEngine();
    }

    // events handled by the page (timer/audio/navigation)
    public event Action<bool>? PauseStateChanged;
    public event Action? RestartRequested;
    public event Action<string>? NavigateRequested;

    // commands
    public ICommand PauseCommand { get; }
    public ICommand ResumeCommand { get; }
    public ICommand RestartCommand { get; }
    public ICommand SettingsCommand { get; }
    public ICommand MenuCommand { get; }

    // hud properties
    public int Score
    {
        get => _score;
        private set
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
        private set
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
        private set
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
        private set => SetProperty(ref _livesText, value);
    }

    public bool IsInvincible
    {
        get => _isInvincible;
        private set => SetProperty(ref _isInvincible, value);
    }

    public string InvincibleText
    {
        get => _invincibleText;
        private set => SetProperty(ref _invincibleText, value);
    }

    public string HintText
    {
        get => _hintText;
        set => SetProperty(ref _hintText, value);
    }

    // overlay properties
    public bool IsPaused
    {
        get => _isPaused;
        private set
        {
            if (SetProperty(ref _isPaused, value))
            {
                OnPropertyChanged(nameof(CanPause));
                (PauseCommand as Command)?.ChangeCanExecute();
            }
        }
    }

    public bool IsGameOver
    {
        get => _isGameOver;
        private set
        {
            if (SetProperty(ref _isGameOver, value))
            {
                OnPropertyChanged(nameof(CanPause));
                (PauseCommand as Command)?.ChangeCanExecute();
            }
        }
    }

    public bool CanPause => !IsPaused && !IsGameOver;

    // game over summary properties
    public string FinalScoreText
    {
        get => _finalScoreText;
        private set => SetProperty(ref _finalScoreText, value);
    }

    public string FinalTimeAliveText
    {
        get => _finalTimeAliveText;
        private set => SetProperty(ref _finalTimeAliveText, value);
    }

    public string FinalHighScoreText
    {
        get => _finalHighScoreText;
        private set => SetProperty(ref _finalHighScoreText, value);
    }

    public string FinalCoinsRunText
    {
        get => _finalCoinsRunText;
        private set => SetProperty(ref _finalCoinsRunText, value);
    }

    public string FinalCoinsHeldText
    {
        get => _finalCoinsHeldText;
        private set => SetProperty(ref _finalCoinsHeldText, value);
    }

    public bool IsNewHighScore
    {
        get => _isNewHighScore;
        private set => SetProperty(ref _isNewHighScore, value);
    }

    public void OnNewRunStarted()
    {
        _lastGameOver = false;
        IsGameOver = false;
        IsPaused = false;
        IsNewHighScore = false;
        SyncFromEngine();
    }

    public void NotifyCoinsHeldChanged()
    {
        if (IsGameOver)
            FinalCoinsHeldText = $"Coins held: {_profile.CoinsHeld}";
    }

    public void SyncFromEngine()
    {
        // reads the current engine state and mirrors it into bindable properties
        GameState gameState = _engine.State;

        Score = gameState.Score;
        HighScore = Math.Max(_profile.HighScore, gameState.HighScore);
        CoinsThisRun = gameState.CoinsThisRun;
        LivesText = gameState.LivesText;
        IsInvincible = gameState.IsInvincible;
        InvincibleText = gameState.IsInvincible ? $"INV: {gameState.InvincibleSecondsLeft}s" : "INV";

        // detect transition into game over exactly once
        if (gameState.IsGameOver && !_lastGameOver)
        {
            _lastGameOver = true;
            IsPaused = false;
            IsGameOver = true;
            BuildGameOverSummary(gameState);

            // ensure profile is in sync with engine’s persisted high score
            if (gameState.HighScore > _profile.HighScore)
                _profile.HighScore = gameState.HighScore;
        }
    }

    // builds the strings shown on the game over overlay
    private void BuildGameOverSummary(GameState gameState)
    {
        FinalScoreText = $"Score: {gameState.Score}";

        TimeSpan timeAlive = TimeSpan.FromSeconds(Math.Max(0, gameState.TimeAlive));
        string timeText = timeAlive.TotalHours >= 1
            ? timeAlive.ToString(@"h\:mm\:ss")
            : timeAlive.ToString(@"m\:ss");
        FinalTimeAliveText = $"Time Alive: {timeText}";

        FinalHighScoreText = $"High Score: {Math.Max(_profile.HighScore, gameState.HighScore)}";
        FinalCoinsRunText = $"Coins this run: {gameState.CoinsThisRun}";
        FinalCoinsHeldText = $"Coins held: {_profile.CoinsHeld}";
        IsNewHighScore = gameState.IsNewHighScore;
    }

    private void SetPaused(bool paused)
    {
        if (_engine.State.IsGameOver) return;

        _engine.State.IsPaused = paused;
        IsPaused = paused;
        PauseStateChanged?.Invoke(paused);
    }
}
