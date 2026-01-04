namespace CarGame;

// app entry point for the maui application
public partial class App : Application
{
    public App()
    {
        // loads app resources and initializes xaml components
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        // creates the first window and sets the root to AppShell for navigation
        return new Window(new AppShell());
    }
}
