using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace CityTour.Services;

internal static class EnvironmentSecrets
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> DotEnvValues = new(LoadDotEnvValues);

    public static string? TryGetValue(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return null;
        }

        var value = Environment.GetEnvironmentVariable(key);
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        if (DotEnvValues.Value.TryGetValue(key, out var fromFile) && !string.IsNullOrWhiteSpace(fromFile))
        {
            return fromFile.Trim();
        }

        return null;
    }

    private static IReadOnlyDictionary<string, string> LoadDotEnvValues()
    {
        try
        {
            var path = FindDotEnvFile();
            if (string.IsNullOrEmpty(path))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawLine in File.ReadAllLines(path))
            {
                var line = rawLine.Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.StartsWith("export ", StringComparison.OrdinalIgnoreCase))
                {
                    line = line.Substring("export ".Length).TrimStart();
                }

                var separatorIndex = line.IndexOf('=');
                if (separatorIndex <= 0)
                {
                    continue;
                }

                var name = line.Substring(0, separatorIndex).Trim();
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                var value = line.Substring(separatorIndex + 1).Trim();
                if (value.Length >= 2 &&
                    ((value.StartsWith("\"", StringComparison.Ordinal) && value.EndsWith("\"", StringComparison.Ordinal)) ||
                     (value.StartsWith("'", StringComparison.Ordinal) && value.EndsWith("'", StringComparison.Ordinal))))
                {
                    value = value.Substring(1, value.Length - 2);
                }

                values[name] = value;
            }

            return values;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to load .env file: {ex.Message}");
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static string? FindDotEnvFile()
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var start in GetCandidateDirectories())
        {
            var current = start;
            while (!string.IsNullOrEmpty(current) && visited.Add(current))
            {
                var candidate = Path.Combine(current, ".env");
                if (File.Exists(candidate))
                {
                    return candidate;
                }

                var parent = Path.GetDirectoryName(current);
                if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                current = parent;
            }
        }

        return null;
    }

    private static IEnumerable<string> GetCandidateDirectories()
    {
        if (!string.IsNullOrEmpty(Environment.CurrentDirectory))
        {
            yield return Environment.CurrentDirectory;
        }

        if (!string.IsNullOrEmpty(AppContext.BaseDirectory) &&
            !string.Equals(Environment.CurrentDirectory, AppContext.BaseDirectory, StringComparison.OrdinalIgnoreCase))
        {
            yield return AppContext.BaseDirectory;
        }
    }
}
