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
    string? WikipediaApiKey { get; }
}

public class ApiKeyProvider : IApiKeyProvider
{
    private static readonly string[] CandidateSecretsFileNames = new[]
    {
        "api_keys.json",
        "api_keys"
    };

    private readonly Lazy<ApiKeyPayload> _secrets = new(LoadSecrets);

    public string? GoogleMapsApiKey => Normalize(
        _secrets.Value.GoogleMapsApiKey
        ?? EnvironmentSecrets.TryGetValue("GOOGLE_MAPS_API_KEY")
        ?? EnvironmentSecrets.TryGetValue("GOOGLE_API_KEY"));

    public string? GooglePlacesApiKey => Normalize(
        _secrets.Value.GooglePlacesApiKey
        ?? EnvironmentSecrets.TryGetValue("GOOGLE_PLACES_API_KEY")
        ?? EnvironmentSecrets.TryGetValue("GOOGLE_API_KEY"));

    public string? OpenAiApiKey => Normalize(
        _secrets.Value.OpenAiApiKey
        ?? EnvironmentSecrets.TryGetValue("OPENAI_API_KEY"));

    public string? WikipediaApiKey => Normalize(
        _secrets.Value.WikipediaApiKey
        ?? EnvironmentSecrets.TryGetValue("WIKIPEDIA_API_KEY"));

    private static ApiKeyPayload LoadSecrets()
    {
        foreach (var fileName in CandidateSecretsFileNames)
        {
            var payload = TryLoadSecrets(fileName);
            if (payload is not null)
            {
                return payload;
            }
        }

        System.Diagnostics.Debug.WriteLine(
            $"No API key bundle found. Expected one of: {string.Join(", ", CandidateSecretsFileNames)}");
        return new ApiKeyPayload();
    }

    private static ApiKeyPayload? TryLoadSecrets(string fileName)
    {
        try
        {
            using var stream = FileSystem.OpenAppPackageFileAsync(fileName).GetAwaiter().GetResult();
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();

            if (string.IsNullOrWhiteSpace(json))
            {
                System.Diagnostics.Debug.WriteLine($"API key file '{fileName}' is empty.");
                return null;
            }

            var payload = JsonSerializer.Deserialize<ApiKeyPayload>(
                json,
                new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            if (payload is null)
            {
                System.Diagnostics.Debug.WriteLine($"API key file '{fileName}' does not contain a valid payload.");
            }
            return payload;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (JsonException ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to parse {fileName}: {ex.Message}");
            return null;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to load {fileName}: {ex.Message}");
            return null;
        }
    }

    private static string? Normalize(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class ApiKeyPayload
    {
        public string? GoogleMapsApiKey { get; set; }
        public string? GooglePlacesApiKey { get; set; }
        public string? OpenAiApiKey { get; set; }
        public string? WikipediaApiKey { get; set; }
    }
}
