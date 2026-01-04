using CarGame.Pages;

namespace CarGame;

// shell container that holds the navigation routes for the app
public partial class AppShell : Shell
{
    public AppShell()
    {
        // loads xaml content for the shell
        InitializeComponent();

        // registers routes so viewmodels can navigate by route name
        Routing.RegisterRoute("game", typeof(GamePage));
        Routing.RegisterRoute("shop", typeof(ShopPage));
        Routing.RegisterRoute("settings", typeof(SettingsPage));
        Routing.RegisterRoute("howto", typeof(HowToPage));
    }
}
