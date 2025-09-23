using CityTour.Models;
using CityTour.Services;
using Microsoft.Maui.ApplicationModel;

namespace CityTour.Views;

[QueryProperty(nameof(PlaceId), "id")]
public partial class DetailPage : ContentPage
{
    private readonly PlaceService _service;
    private Place? _place;

    public string? PlaceId { get; set; }

    public DetailPage(PlaceService service)
    {
        InitializeComponent();
        _service = service;
    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        if (!string.IsNullOrWhiteSpace(PlaceId))
        {
            _place = _service.GetById(PlaceId);
            if (_place != null)
            {
                NameLabel.Text = _place.Name;
                var displayAddress = AddressFormatter.GetDisplayAddress(_place.Address) ?? _place.Address;
                AddressLabel.Text = displayAddress;
                DescLabel.Text = _place.Description;
            }
        }
    }

    private async void OnOpenMapsClicked(object sender, EventArgs e)
    {
        if (_place == null) return;
        var loc = new Microsoft.Maui.Devices.Sensors.Location(_place.Latitude, _place.Longitude);
        var options = new MapLaunchOptions { Name = _place.Name, NavigationMode = NavigationMode.Walking };
        await Map.Default.OpenAsync(loc, options);
    }
}
