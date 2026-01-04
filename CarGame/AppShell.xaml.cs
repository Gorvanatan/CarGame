using CarGame.Pages;

namespace CarGame
{
    public partial class AppShell : Shell
    {
        public AppShell()
        {
            InitializeComponent();

            // Register routes for multi-page navigation
            Routing.RegisterRoute("game", typeof(GamePage));
            Routing.RegisterRoute("shop", typeof(ShopPage));
            Routing.RegisterRoute("settings", typeof(SettingsPage));
            Routing.RegisterRoute("howto", typeof(HowToPage));
        }
    }
}
