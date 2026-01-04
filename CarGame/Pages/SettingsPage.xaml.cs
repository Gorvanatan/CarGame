using CarGame.Services;
using CarGame.ViewModels;

namespace CarGame.Pages;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _vm;

    public SettingsPage()
    {
        InitializeComponent();

        var profile = TryGetService<IProfileService>() ?? new ProfileService();
        _vm = new SettingsViewModel(
            profile,
            async (title, msg) => await DisplayAlert(title, msg, "OK"),
            async (title, msg, accept, cancel) => await DisplayAlert(title, msg, accept, cancel));

        BindingContext = _vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.OnAppearing();
    }

    private static T? TryGetService<T>() where T : class
    {
        try
        {
            return Application.Current?.Handler?.MauiContext?.Services.GetService(typeof(T)) as T;
        }
        catch
        {
            return null;
        }
    }
}
