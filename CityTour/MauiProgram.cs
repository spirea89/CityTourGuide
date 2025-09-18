using System;
using System.Net.Http;
using CityTour.Services;
using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Handlers;
#if WINDOWS
using Microsoft.UI.Xaml.Controls.Maps;
using Microsoft.UI.Xaml.Input;
#endif

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

#if WINDOWS
        MapHandler.Mapper.AppendToMapping("ScrollWheelZoom", (handler, view) =>
        {
            if (handler.PlatformView is MapControl map)
            {
                map.PointerWheelChanged -= OnMapPointerWheelChanged;
                map.PointerWheelChanged += OnMapPointerWheelChanged;
            }
        });
#endif

        return builder.Build();
    }

#if WINDOWS
    private static void OnMapPointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (sender is not MapControl map)
            return;

        var delta = e.GetCurrentPoint(map).Properties.MouseWheelDelta;
        if (delta == 0)
            return;

        var zoomChange = delta > 0 ? 0.5 : -0.5;
        var targetZoom = Math.Clamp(map.ZoomLevel + zoomChange, map.MinZoomLevel, map.MaxZoomLevel);

        if (Math.Abs(targetZoom - map.ZoomLevel) > double.Epsilon)
        {
            map.ZoomLevel = targetZoom;
        }

        e.Handled = true;
    }
#endif
}
