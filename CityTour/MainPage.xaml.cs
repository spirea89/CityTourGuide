using Microsoft.Maui.Controls.Maps;
using Microsoft.Maui.Maps;
using CityTour.Services;
using CityTour.Models;

namespace CityTour;

public partial class MainPage : ContentPage
{
    private readonly PlaceService _service;
    private List<Place> _allPlaces = new();

    public MainPage(PlaceService service)
    {
        InitializeComponent();
        _service = service;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Load places once
        _allPlaces = _service.GetAll().ToList();

        // Center on Vienna
        var vienna = new Location(48.2082, 16.3738);
        Map.MoveToRegion(MapSpan.FromCenterAndRadius(vienna, Distance.FromKilometers(3)));

        // Draw all pins initially
        RefreshPins(_allPlaces);
    }

    private void RefreshPins(IEnumerable<Place> places)
    {
        Map.Pins.Clear();

        foreach (var p in places)
        {
            var pin = new Pin
            {
                Label = p.Name,
                Address = p.Address,
                Type = PinType.Place,
                Location = new Location(p.Latitude, p.Longitude)
            };

            pin.MarkerClicked += async (s, e) =>
            {
                e.HideInfoWindow = true;
                await Shell.Current.GoToAsync($"DetailPage?id={p.Id}");
            };

            Map.Pins.Add(pin);
        }
    }

    private void ApplyFilter(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            RefreshPins(_allPlaces);
            return;
        }

        var filtered = _allPlaces
            .Where(p => p.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
            .ToList();

        RefreshPins(filtered);
    }

    // SearchBar events
    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
        => ApplyFilter(e.NewTextValue);

    private void OnSearchButtonPressed(object sender, EventArgs e)
        => ApplyFilter(Search.Text);
    // NEW: Recenter button
    private void OnRecenterClicked(object sender, EventArgs e)
    {
        var vienna = new Location(48.2082, 16.3738);
        Map.MoveToRegion(MapSpan.FromCenterAndRadius(vienna, Distance.FromKilometers(3)));
    }

}
