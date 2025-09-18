using System.Net.Http;
using CityTour.Services;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Storage;

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

        builder.Services.AddSingleton<HttpClient>(_ => new HttpClient());
        builder.Services.AddSingleton<Services.PlaceService>();
        builder.Services.AddSingleton<IAiStoryService, AiStoryService>();
        builder.Services.AddSingleton<MainPage>();
        builder.Services.AddTransient<Views.DetailPage>();
        // …
        Preferences.Set("ai.story.apikey", "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");
        return builder.Build();
    }
}
