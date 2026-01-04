namespace CarGame.Pages;

// simple help page that shows controls and game rules
public partial class HowToPage : ContentPage
{
    public HowToPage()
    {
        // loads xaml content for the page
        InitializeComponent();
    }

    private async void Back_Clicked(object sender, EventArgs eventArgs)
    {
        // navigates back to the previous page in the shell stack
        await Shell.Current.GoToAsync("..");
    }
}
