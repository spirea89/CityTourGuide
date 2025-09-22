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
            using var stream = TryOpenSecretsStream(fileName);
            if (stream is null)
            {
                return null;
            }

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

    private static Stream? TryOpenSecretsStream(string fileName)
    {
        try
        {
            return FileSystem.OpenAppPackageFileAsync(fileName).GetAwaiter().GetResult();
        }
        catch (FileNotFoundException)
        {
            // Continue to probe other locations.
        }
        catch (DirectoryNotFoundException)
        {
            // Continue to probe other locations.
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to open '{fileName}' from packaged assets: {ex.Message}");
        }

        foreach (var path in EnumerateFallbackFilePaths(fileName))
        {
            try
            {
                System.Diagnostics.Debug.WriteLine($"Loading API key bundle from '{path}'.");
                return File.OpenRead(path);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open fallback key file '{path}': {ex.Message}");
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateFallbackFilePaths(string fileName)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in EnumerateCandidateDirectories())
        {
            if (string.IsNullOrWhiteSpace(root))
            {
                continue;
            }

            foreach (var relative in EnumerateRelativeSearchRoots())
            {
                var path = relative.Length == 0
                    ? Path.Combine(root, fileName)
                    : Path.Combine(root, relative, fileName);

                if (!visited.Add(path) || !File.Exists(path))
                {
                    continue;
                }

                yield return path;
            }
        }
    }

    private static IEnumerable<string> EnumerateCandidateDirectories()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in new[] { NormalizeDirectory(AppContext.BaseDirectory), NormalizeDirectory(Environment.CurrentDirectory) })
        {
            foreach (var directory in EnumerateSelfAndParents(root))
            {
                if (seen.Add(directory))
                {
                    yield return directory;
                }
            }
        }
    }

    private static IEnumerable<string> EnumerateSelfAndParents(string? start)
    {
        var current = start;
        while (!string.IsNullOrEmpty(current))
        {
            yield return current;

            string? parent = null;
            try
            {
                parent = Directory.GetParent(current)?.FullName;
            }
            catch
            {
                yield break;
            }

            if (string.IsNullOrEmpty(parent))
            {
                yield break;
            }

            current = parent;
        }
    }

    private static string? NormalizeDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(directory);
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<string> EnumerateRelativeSearchRoots()
    {
        yield return Path.Combine("Resources", "Raw");
        yield return "Resources";
        yield return "Raw";
        yield return string.Empty;
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
