namespace CarGame.Pages;

public partial class HowToPage : ContentPage
{
    public HowToPage()
    {
        InitializeComponent();
    }

    private async void Back_Clicked(object sender, EventArgs e)
        => await Shell.Current.GoToAsync("..");
}
