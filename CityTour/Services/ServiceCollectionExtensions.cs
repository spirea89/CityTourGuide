using System;
using System.Net.Http;
using CityTour;
using CityTour.Views;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Storage;

namespace CityTour.Services;

/// <summary>
/// Extension helpers for registering application services with the
/// dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers the core application services.
    /// </summary>
    public static IServiceCollection AddAstonovationServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddSingleton<HttpClient>(_ => new HttpClient());
        services.AddSingleton<PlaceService>();
        services.AddSingleton<IAiStoryService, AiStoryService>();
        services.AddSingleton<MainPage>();
        services.AddTransient<DetailPage>();

        return services;
    }

    /// <summary>
    /// Registers the <see cref="CitySettings"/> options from the provided
    /// configuration. When the configuration does not contain a matching
    /// section the default values are used instead so the application
    /// continues to behave as before.
    /// </summary>
    public static IServiceCollection AddCitySettings(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var settings = configuration.GetSection("City").Get<CitySettings>()
                       ?? CitySettings.CreateDefault();

        services.AddSingleton(settings);
        return services;
    }

    /// <summary>
    /// Exposes the MAUI preferences implementation as <see cref="IPreferences"/>
    /// so it can be injected into services that require persisted settings.
    /// </summary>
    public static IServiceCollection AddPreferences(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IPreferences>(_ => Preferences.Default);
        return services;
    }
}
