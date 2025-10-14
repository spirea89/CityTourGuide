#!/usr/bin/env dotnet-script
#r "nuget: System.Text.Json, 8.0.0"

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.IO;

// Load API key from api_keys.json
var apiKeysPath = Path.Combine("CityTour", "Resources", "Raw", "api_keys.json");
if (!File.Exists(apiKeysPath))
{
    Console.WriteLine($"ERROR: api_keys.json not found at {apiKeysPath}");
    return;
}

var apiKeysJson = File.ReadAllText(apiKeysPath);
var apiKeysDoc = JsonDocument.Parse(apiKeysJson);
var openAiKey = apiKeysDoc.RootElement.GetProperty("openAiApiKey").GetString();

if (string.IsNullOrWhiteSpace(openAiKey))
{
    Console.WriteLine("ERROR: OpenAI API key is not configured in api_keys.json");
    return;
}

Console.WriteLine("OpenAI API Key loaded: " + openAiKey.Substring(0, Math.Min(10, openAiKey.Length)) + "...");

// Get building name and address from user
Console.WriteLine("\nEnter building name (or press Enter for default 'St. Stephen's Cathedral'):");
var buildingName = Console.ReadLine();
if (string.IsNullOrWhiteSpace(buildingName))
{
    buildingName = "St. Stephen's Cathedral";
}

Console.WriteLine("Enter address (or press Enter for default 'Stephansplatz 3, 1010 Wien'):");
var address = Console.ReadLine();
if (string.IsNullOrWhiteSpace(address))
{
    address = "Stephansplatz 3, 1010 Wien";
}

Console.WriteLine($"\nTesting story generation for: {buildingName} at {address}");
Console.WriteLine("Using model: gpt-4o");

// Build the prompt (simplified version of the app's prompt)
var prompt = $@"You are a meticulous local historian. Based on the available information about {buildingName} at {address}: Limited information is available about this location.

Write a vivid, engaging ~120–150 word historical narrative. If you have specific historical details, create a chronological story highlighting founding dates, significant events, and cultural importance. If information is limited, focus on the broader historical context of the location and what makes this place potentially significant to visitors. Use welcoming, informative language that brings the place to life.";

Console.WriteLine("\n=== PROMPT ===");
Console.WriteLine(prompt);
Console.WriteLine("\n=== MAKING API REQUEST ===");

// Create the request payload
var payload = new
{
    model = "gpt-4o",
    messages = new[]
    {
        new { role = "system", content = "You are a creative, historically knowledgeable city tour guide. Craft short stories and responses about buildings that feel authentic, welcoming, and vivid." },
        new { role = "user", content = prompt }
    },
    temperature = 0.8,
    max_tokens = 600
};

var jsonPayload = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine("Request payload:");
Console.WriteLine(jsonPayload);

using var httpClient = new HttpClient();
httpClient.Timeout = TimeSpan.FromSeconds(60);

using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", openAiKey);
request.Content = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

Console.WriteLine("\nSending request to OpenAI...");

var response = await httpClient.SendAsync(request);
var responseBody = await response.Content.ReadAsStringAsync();

Console.WriteLine($"\n=== RESPONSE STATUS: {response.StatusCode} ===");
Console.WriteLine("\n=== RAW RESPONSE BODY ===");
Console.WriteLine(responseBody);

if (response.IsSuccessStatusCode)
{
    Console.WriteLine("\n=== PRETTY-PRINTED RESPONSE ===");
    try
    {
        var doc = JsonDocument.Parse(responseBody);
        var prettyJson = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        Console.WriteLine(prettyJson);
        
        Console.WriteLine("\n=== ATTEMPTING TO EXTRACT CONTENT ===");
        
        // Try standard chat completion format
        if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.ValueKind == JsonValueKind.Array)
        {
            Console.WriteLine("Found 'choices' array");
            foreach (var choice in choices.EnumerateArray())
            {
                Console.WriteLine($"Processing choice...");
                if (choice.TryGetProperty("message", out var message))
                {
                    Console.WriteLine("Found 'message' object");
                    if (message.TryGetProperty("content", out var content))
                    {
                        Console.WriteLine("\n=== EXTRACTED STORY CONTENT ===");
                        Console.WriteLine(content.GetString());
                    }
                    else
                    {
                        Console.WriteLine("ERROR: 'message' object exists but has no 'content' property");
                        Console.WriteLine("Message properties: " + string.Join(", ", message.EnumerateObject().Select(p => p.Name)));
                    }
                }
                else
                {
                    Console.WriteLine("ERROR: Choice has no 'message' property");
                    Console.WriteLine("Choice properties: " + string.Join(", ", choice.EnumerateObject().Select(p => p.Name)));
                }
            }
        }
        else
        {
            Console.WriteLine("ERROR: Response has no 'choices' array");
            Console.WriteLine("Root properties: " + string.Join(", ", doc.RootElement.EnumerateObject().Select(p => p.Name)));
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"ERROR parsing response: {ex.Message}");
    }
}
else
{
    Console.WriteLine("\n=== ERROR RESPONSE ===");
    Console.WriteLine($"Status: {response.StatusCode}");
    Console.WriteLine($"Body: {responseBody}");
}

