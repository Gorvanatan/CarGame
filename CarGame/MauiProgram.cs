using CarGame.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CarGame;

// creates and configures the maui app and dependency injection services
public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        // builds the maui host
        var appBuilder = MauiApp.CreateBuilder();

        appBuilder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                // registers fonts used by the app ui
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        // registers small services used by mvvm pages
        appBuilder.Services.AddSingleton<IProfileService, ProfileService>();

#if DEBUG
        // enables debug logging while developing
        appBuilder.Logging.AddDebug();
#endif

        return appBuilder.Build();
    }
}
