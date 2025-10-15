using Microsoft.AspNetCore.Components.WebView.Maui;
using Microsoft.Extensions.DependencyInjection;
using CityTour;

namespace CityTour;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();

        // Add platform-specific services for web
        if (DeviceInfo.Platform == DevicePlatform.WinUI)
        {
            // Web-specific service registrations can go here
        }

        return builder.Build();
    }
}
