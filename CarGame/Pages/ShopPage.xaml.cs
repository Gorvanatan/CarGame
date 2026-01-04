using System;
using System.IO;
using CarGame.Services;
using CarGame.ViewModels;
using Microsoft.Maui.Storage;

namespace CarGame.Pages;

// shop page that lets the player buy cars and upgrades
public partial class ShopPage : ContentPage
{
    // keeps the viewmodel alive for the lifetime of the page
    private readonly ShopViewModel _shopViewModel;

    public ShopPage()
    {
        // loads xaml content for the page
        InitializeComponent();

        // tries to get services from maui dependency injection, but falls back safely
        IProfileService profileService = TryGetService<IProfileService>() ?? new ProfileService();

        _shopViewModel = new ShopViewModel(profileService, async (title, message) => await DisplayAlert(title, message, "OK"));
        BindingContext = _shopViewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // refreshes coin totals and ownership flags when returning to the shop
        _shopViewModel.OnAppearing();
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

    private async void UploadCustomCar_Clicked(object sender, EventArgs eventArgs)
    {
        try
        {
            // opens a picker so the user can choose an image for a custom car skin
            FileResult? pickedFile = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Pick an image for your car",
                FileTypes = FilePickerFileType.Images
            });

            if (pickedFile is null)
                return;

            string fileExtension = Path.GetExtension(pickedFile.FileName);
            if (string.IsNullOrWhiteSpace(fileExtension))
                fileExtension = ".png";

            // copies the file into app data so we can load it reliably later
            string destinationPath = Path.Combine(FileSystem.AppDataDirectory, $"customcar{fileExtension.ToLowerInvariant()}");

            await using (Stream sourceStream = await pickedFile.OpenReadAsync())
            await using (FileStream destinationStream = File.Open(destinationPath, FileMode.Create, FileAccess.Write))
            {
                await sourceStream.CopyToAsync(destinationStream);
            }

            // tells the viewmodel to save and select the custom car
            _shopViewModel.SetCustomCar(destinationPath);
        }
        catch (Exception exception)
        {
            // shows a friendly error if the image cannot be used
            await DisplayAlert("Couldn't use that image", exception.Message, "OK");
        }
    }
}
