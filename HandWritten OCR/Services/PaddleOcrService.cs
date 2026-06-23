using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows;

namespace HandWritten_OCR.Services;

public sealed class PaddleOcrService : IPaddleOcrService
{
    private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(5) };
    private const string BaseUrl = "http://localhost:5002";

    public async Task<string> RecognizeAsync(string imagePath, string lang, CancellationToken cancellationToken = default)
    {
        string base64 = await Task.Run(() => FileToBase64(imagePath), cancellationToken);
        return await PostBase64Async(base64, lang, null, cancellationToken);
    }

    public async Task<string> RecognizeRegionAsync(string imagePath, Rect pixelBounds, string lang, CancellationToken cancellationToken = default)
    {
        string base64 = await Task.Run(() => FileToBase64(imagePath), cancellationToken);
        var region = new RegionBox((int)pixelBounds.X, (int)pixelBounds.Y,
                                   (int)pixelBounds.Width, (int)pixelBounds.Height);
        return await PostBase64Async(base64, lang, region, cancellationToken);
    }

    private static string FileToBase64(string imagePath)
        => Convert.ToBase64String(File.ReadAllBytes(imagePath));

    private static async Task<string> PostBase64Async(
        string base64, string lang, RegionBox? region, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(base64)) return string.Empty;

        object payload = region is null
            ? (object)new { image_base64 = base64, lang }
            : (object)new { image_base64 = base64, lang, region };

        using var response = await _http.PostAsJsonAsync($"{BaseUrl}/ocr/base64", payload, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var doc = await JsonDocument.ParseAsync(
            await response.Content.ReadAsStreamAsync(cancellationToken), default, cancellationToken);
        var root = doc.RootElement;

        if (root.TryGetProperty("success", out var success) && success.GetBoolean() &&
            root.TryGetProperty("text", out var text))
            return text.GetString() ?? string.Empty;

        if (root.TryGetProperty("error", out var err))
            throw new InvalidOperationException($"PaddleOCR error: {err.GetString()}");

        return string.Empty;
    }

    private sealed record RegionBox(int x, int y, int w, int h);
}
