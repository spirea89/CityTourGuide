using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
    private static readonly string[] CandidateSecretsFileNames = new[]
    {
        "api_keys.json",
        "api_keys",
        "api_key.json",
        "api_key"
    };

    private readonly Lazy<ApiKeyPayload> _secrets = new(LoadSecrets);

    public string? GoogleMapsApiKey => Normalize(_secrets.Value.GoogleMapsApiKey);
    public string? GooglePlacesApiKey => Normalize(_secrets.Value.GooglePlacesApiKey);
    public string? OpenAiApiKey => Normalize(_secrets.Value.OpenAiApiKey);

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

            if (!ApiKeyPayload.TryParse(json, out var payload))
            {
                System.Diagnostics.Debug.WriteLine($"API key file '{fileName}' does not contain a valid payload.");
                return null;
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
        private static readonly JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly string[] GoogleMapsKeyCandidates = new[]
        {
            "googlemapsapikey",
            "googlemapskey",
            "mapsapikey",
            "mapskey",
            "googlemaps"
        };

        private static readonly string[] GooglePlacesKeyCandidates = new[]
        {
            "googleplacesapikey",
            "googleplaceskey",
            "placesapikey",
            "placeskey",
            "googleplaces"
        };

        private static readonly string[] OpenAiKeyCandidates = new[]
        {
            "openaiapikey",
            "openaikey",
            "openai"
        };

        public string? GoogleMapsApiKey { get; init; }
        public string? GooglePlacesApiKey { get; init; }
        public string? OpenAiApiKey { get; init; }

        private bool HasAnyValue
            => !string.IsNullOrWhiteSpace(GoogleMapsApiKey)
               || !string.IsNullOrWhiteSpace(GooglePlacesApiKey)
               || !string.IsNullOrWhiteSpace(OpenAiApiKey);

        public static bool TryParse(string json, out ApiKeyPayload payload)
        {
            var typed = JsonSerializer.Deserialize<ApiKeyPayload>(json, SerializerOptions);
            if (typed is not null && typed.HasAnyValue)
            {
                payload = typed;
                return true;
            }

            var fallback = ParseLoosely(json);
            if (fallback.HasAnyValue)
            {
                payload = fallback;
                return true;
            }

            payload = new ApiKeyPayload();
            return false;
        }

        private static ApiKeyPayload ParseLoosely(string json)
        {
            using var document = JsonDocument.Parse(
                json,
                new JsonDocumentOptions
                {
                    AllowTrailingCommas = true,
                    CommentHandling = JsonCommentHandling.Skip
                });

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return new ApiKeyPayload();
            }

            var values = new Dictionary<string, string>();

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var normalizedName = NormalizePropertyName(property.Name);
                if (normalizedName.Length == 0)
                {
                    continue;
                }

                var stringValue = property.Value.GetString();
                if (string.IsNullOrWhiteSpace(stringValue))
                {
                    continue;
                }

                values[normalizedName] = stringValue;
            }

            return new ApiKeyPayload
            {
                GoogleMapsApiKey = Resolve(values, GoogleMapsKeyCandidates),
                GooglePlacesApiKey = Resolve(values, GooglePlacesKeyCandidates),
                OpenAiApiKey = Resolve(values, OpenAiKeyCandidates)
            };
        }

        private static string? Resolve(Dictionary<string, string> values, string[] candidates)
        {
            foreach (var candidate in candidates)
            {
                if (values.TryGetValue(candidate, out var value))
                {
                    return value;
                }
            }

            return null;
        }

        private static string NormalizePropertyName(string name)
        {
            var buffer = new StringBuilder(name.Length);
            foreach (var ch in name)
            {
                if (char.IsLetterOrDigit(ch))
                {
                    buffer.Append(char.ToLowerInvariant(ch));
                }
            }

            return buffer.ToString();
        }
    }
}
