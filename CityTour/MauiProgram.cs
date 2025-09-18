using CityTour.Services;
using Microsoft.Maui.Controls.Maps;

namespace CityTour;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .UseMauiMaps()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

        builder.Services
            .AddAstonovationServices(builder.Configuration)
            .AddCitySettings(builder.Configuration)
            .AddPreferences();

        return builder.Build();
    }
}
