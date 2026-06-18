using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;

namespace HandWritten_OCR.Services;

public sealed class KrakenService : IKrakenService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private const string BaseUrl = "http://localhost:5001";

    public async Task<string> RecognizeAsync(string imagePath, string modelName, CancellationToken cancellationToken = default)
    {
        string base64 = await Task.Run(() => FileToBase64(imagePath), cancellationToken);
        return await PostBase64Async(base64, modelName, null, cancellationToken);
    }

    public async Task<string> RecognizeRegionAsync(string imagePath, Rect pixelBounds, string modelName, CancellationToken cancellationToken = default)
    {
        // Send the full page so the server can use full-page segmentation, then filter to the region
        string base64 = await Task.Run(() => FileToBase64(imagePath), cancellationToken);
        var region = new RegionBox((int)pixelBounds.X, (int)pixelBounds.Y,
                                   (int)pixelBounds.Width, (int)pixelBounds.Height);
        return await PostBase64Async(base64, modelName, region, cancellationToken);
    }

    private static string FileToBase64(string imagePath)
        => Convert.ToBase64String(File.ReadAllBytes(imagePath));

    private static async Task<string> PostBase64Async(
        string base64, string modelName, RegionBox? region, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(base64)) return string.Empty;

        object payload = region is null
            ? (object)new { image_base64 = base64, model = modelName }
            : (object)new { image_base64 = base64, model = modelName, region };

        using var response = await _http.PostAsJsonAsync($"{BaseUrl}/ocr/base64", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), default, cancellationToken);
        var root = doc.RootElement;

        if (root.TryGetProperty("success", out var success) && success.GetBoolean() &&
            root.TryGetProperty("text", out var text))
            return text.GetString() ?? string.Empty;

        if (root.TryGetProperty("error", out var err))
            throw new InvalidOperationException($"Kraken error: {err.GetString()}");

        return string.Empty;
    }

    private sealed record RegionBox(int x, int y, int w, int h);
}
