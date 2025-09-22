using System;
using System.IO;
using System.Text.Json;
using Microsoft.Maui.Storage;

namespace CityTour.Services;

public interface IApiKeyProvider
{
    string? GoogleMapsApiKey { get; }
    string? GooglePlacesApiKey { get; }
    string? OpenAiApiKey { get; }
}

public class ApiKeyProvider : IApiKeyProvider
{
    private const string SecretsFileName = "api_keys.json";
    private readonly Lazy<ApiKeyPayload> _secrets = new(LoadSecrets);

    public string? GoogleMapsApiKey => Normalize(_secrets.Value.GoogleMapsApiKey);
    public string? GooglePlacesApiKey => Normalize(_secrets.Value.GooglePlacesApiKey);
    public string? OpenAiApiKey => Normalize(_secrets.Value.OpenAiApiKey);

    private static ApiKeyPayload LoadSecrets()
    {
        try
        {
            using var stream = FileSystem.OpenAppPackageFileAsync(SecretsFileName).GetAwaiter().GetResult();
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();
            var payload = JsonSerializer.Deserialize<ApiKeyPayload>(json);
            return payload ?? new ApiKeyPayload();
        }
        catch (FileNotFoundException)
        {
            return new ApiKeyPayload();
        }
        catch (DirectoryNotFoundException)
        {
            return new ApiKeyPayload();
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to parse {SecretsFileName}: {ex.Message}");
            return new ApiKeyPayload();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load {SecretsFileName}: {ex.Message}");
            return new ApiKeyPayload();
        }
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class ApiKeyPayload
    {
        public string? GoogleMapsApiKey { get; set; }
        public string? GooglePlacesApiKey { get; set; }
        public string? OpenAiApiKey { get; set; }
    }
}
