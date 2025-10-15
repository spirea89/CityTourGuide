using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using CityTour.Web;
using CityTour.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// Register HttpClient
builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// Register web-compatible services
builder.Services.AddSingleton<IApiKeyProvider, WebApiKeyProvider>();
builder.Services.AddSingleton<IBuildingContextService, BuildingContextService>();
builder.Services.AddSingleton<IFactCheckService, FactCheckService>();
builder.Services.AddSingleton<IAiStoryService, WebAiStoryService>();

await builder.Build().RunAsync();
