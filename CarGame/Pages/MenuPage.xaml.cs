using CarGame.Services;
using CarGame.ViewModels;

namespace CarGame.Pages;

public partial class MenuPage : ContentPage
{
    private readonly MenuViewModel _vm;

    public MenuPage()
    {
        InitializeComponent();

        var profile = TryGetService<IProfileService>() ?? new ProfileService();
        _vm = new MenuViewModel(profile);
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
