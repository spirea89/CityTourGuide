using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using CityTour.Models;
using Microsoft.Maui.Storage;

namespace CityTour.Services
{
    public class PlaceService
    {
        private List<Place> _places = new List<Place>();

        public PlaceService()
        {
            using var stream = FileSystem.OpenAppPackageFileAsync("places.json").Result;
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var list = JsonSerializer.Deserialize<List<Place>>(json);
            if (list != null)
            {
                _places = list;
            }
        }

        public IEnumerable<Place> GetAll() => _places;
        public Place? GetById(string id) => _places.FirstOrDefault(p => p.Id == id);
    }
}
