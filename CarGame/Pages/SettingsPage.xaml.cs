using CarGame.Services;
using CarGame.ViewModels;

namespace CarGame.Pages;

// settings page for audio and save data actions
public partial class SettingsPage : ContentPage
{
    // keeps the viewmodel alive for the lifetime of the page
    private readonly SettingsViewModel _settingsViewModel;

    public SettingsPage()
    {
        // loads xaml content for the page
        InitializeComponent();

        // tries to get services from maui dependency injection, but falls back safely
        IProfileService profileService = TryGetService<IProfileService>() ?? new ProfileService();

        // passes simple alert helpers into the viewmodel so commands can show messages
        _settingsViewModel = new SettingsViewModel(
            profileService,
            async (title, message) => await DisplayAlert(title, message, "OK"),
            async (title, message, accept, cancel) => await DisplayAlert(title, message, accept, cancel));

        BindingContext = _settingsViewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // reloads current values when returning from another page
        _settingsViewModel.OnAppearing();
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
