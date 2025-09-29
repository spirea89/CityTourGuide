using System.Net.Http;
using CityTour.Services;
using Microsoft.Maui.Controls.Maps;

namespace CityTour
{
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
            builder.Services.AddSingleton<IApiKeyProvider, ApiKeyProvider>();
            builder.Services.AddSingleton<Services.PlaceService>();
            builder.Services.AddSingleton<IWikipediaService, WikipediaService>();
            builder.Services.AddSingleton<IAiStoryService, AiStoryService>();
            builder.Services.AddSingleton<MainPage>();
            builder.Services.AddTransient<Views.DetailPage>();

            return builder.Build();
        }
    }
}
