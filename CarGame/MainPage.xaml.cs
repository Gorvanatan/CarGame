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

    // For mouse + touch on Windows (PanGesture works reliably)
    private double _panTotalX = 0;

    public MainPage()
    {
        InitializeComponent();

        _drawable = new GameDrawable(_engine);
        GameView.Drawable = _drawable;

        // PAN (works with mouse drag on Windows + touch on mobile)
        var pan = new PanGestureRecognizer();
        pan.PanUpdated += (_, e) =>
        {
            if (_engine.State.IsGameOver) return;

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
                    const double threshold = 50; // pixels (tweak if you want)
                    if (_panTotalX <= -threshold) _engine.TryMovePlayerLane(-1);
                    else if (_panTotalX >= threshold) _engine.TryMovePlayerLane(+1);
                    break;
            }
        };

        // TAP to restart after game over
        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, __) =>
        {
            if (_engine.State.IsGameOver)
                _engine.Reset();
        };

        // Attach gestures
        GameView.GestureRecognizers.Clear();
        GameView.GestureRecognizers.Add(pan);
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
                : "Drag left/right to change lanes";

            GameView.Invalidate();
        };
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _sw.Restart();
        _timer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _timer.Stop();
    }
}
