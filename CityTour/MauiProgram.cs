using System;
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

        builder.Services
            .AddAstonovationServices(builder.Configuration)
            .AddCitySettings(builder.Configuration)
            .AddPreferences();

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
