using CarGame.Services;
using CarGame.ViewModels;

namespace CarGame.Pages;

// main landing page that shows play/shop/settings and the current saved stats
public partial class MenuPage : ContentPage
{
    // keeps the viewmodel alive for the lifetime of the page
    private readonly MenuViewModel _menuViewModel;

    public MenuPage()
    {
        // loads xaml content for the page
        InitializeComponent();

        // tries to get services from maui dependency injection, but falls back safely
        IProfileService profileService = TryGetService<IProfileService>() ?? new ProfileService();

        _menuViewModel = new MenuViewModel(profileService);
        BindingContext = _menuViewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // refreshes stats in case the user bought upgrades or changed settings
        _menuViewModel.OnAppearing();
    }

    private static T? TryGetService<T>() where T : class
    {
        try
        {
            // reads the maui service provider from the current app handler
            return Application.Current?.Handler?.MauiContext?.Services.GetService(typeof(T)) as T;
        }
        catch
        {
            // returns null if the service provider is not available yet
            return null;
        }
    }
}
