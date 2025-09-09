using System.Text.Json;
using System.IO;
using System.Linq;
using Microsoft.Maui.Storage;
using CityTour.Models;

namespace CityTour.Services;

public class PlaceService
{
    private List<Place> _places = new();

    public PlaceService()
    {
        using var stream = FileSystem.OpenAppPackageFileAsync("places.json").Result;
        using var reader = new StreamReader(stream);
        var json = reader.ReadToEnd();
        var list = JsonSerializer.Deserialize<List<Place>>(json);
        if (list != null)
            _places = list;
    }

    public IEnumerable<Place> GetAll() => _places;
    public Place? GetById(string id) => _places.FirstOrDefault(p => p.Id == id);
}
