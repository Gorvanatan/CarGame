using System;
using System.IO;
using CarGame.Services;
using CarGame.ViewModels;
using Microsoft.Maui.Storage;

namespace CarGame.Pages;

public partial class ShopPage : ContentPage
{
    private readonly ShopViewModel _vm;

    public ShopPage()
    {
        InitializeComponent();

        var profile = TryGetService<IProfileService>() ?? new ProfileService();
        _vm = new ShopViewModel(profile, async (title, msg) => await DisplayAlert(title, msg, "OK"));
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

    private async void UploadCustomCar_Clicked(object sender, EventArgs e)
    {
        try
        {
            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Pick an image for your car",
                FileTypes = FilePickerFileType.Images
            });

            if (result is null)
                return;

            var ext = Path.GetExtension(result.FileName);
            if (string.IsNullOrWhiteSpace(ext))
                ext = ".png";

            // Copy into app data so we can read it reliably later.
            var destPath = Path.Combine(FileSystem.AppDataDirectory, $"customcar{ext.ToLowerInvariant()}");

            await using (var src = await result.OpenReadAsync())
            await using (var dst = File.Open(destPath, FileMode.Create, FileAccess.Write))
            {
                await src.CopyToAsync(dst);
            }

            _vm.SetCustomCar(destPath);
        }
        catch (Exception ex)
        {
            await DisplayAlert("Couldn't use that image", ex.Message, "OK");
        }
    }
}
