using CarGame.Game;
using CarGame.UI;
using System.Diagnostics;

namespace CarGame;

public partial class MainPage : ContentPage
{
    private readonly GameEngine _engine = new();
    private readonly GameDrawable _drawable;

    private readonly IDispatcherTimer _timer;
    private readonly Stopwatch _sw = new();

    public MainPage()
    {
        InitializeComponent();

        _drawable = new GameDrawable(_engine);
        GameView.Drawable = _drawable;

        // Swipe gestures
        var swipeLeft = new SwipeGestureRecognizer { Direction = SwipeDirection.Left };
        swipeLeft.Swiped += (_, __) => _engine.TryMovePlayerLane(-1);

        var swipeRight = new SwipeGestureRecognizer { Direction = SwipeDirection.Right };
        swipeRight.Swiped += (_, __) => _engine.TryMovePlayerLane(+1);

        // Tap to restart if game over
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, __) =>
        {
            if (_engine.State.IsGameOver)
                _engine.Reset();
        };

        GameView.GestureRecognizers.Add(swipeLeft);
        GameView.GestureRecognizers.Add(swipeRight);
        GameView.GestureRecognizers.Add(tap);

        // Game loop (~60fps)
        _timer = Dispatcher.CreateTimer();
        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += (_, __) =>
        {
            var dt = _sw.Elapsed.TotalSeconds;
            _sw.Restart();

            _engine.Update(dt);

            ScoreLabel.Text = $"Score: {_engine.State.Score}";
            LivesLabel.Text = _engine.State.LivesText;
            HintLabel.Text = _engine.State.IsGameOver
                ? "Game Over — tap to restart"
                : "Swipe left/right to change lanes";

            GameView.Invalidate();
        };
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Load sprites once (safe to call repeatedly; it no-ops after first load)
        await _drawable.LoadImagesAsync();

        _sw.Restart();
        _timer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _timer.Stop();
    }
}
