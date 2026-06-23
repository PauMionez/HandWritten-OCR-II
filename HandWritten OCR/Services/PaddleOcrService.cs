using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;

namespace HandWritten_OCR.Services;

public sealed class PaddleOcrService : IPaddleOcrService
{
    private static readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(60) };
    private const string OcrUrl = "http://127.0.0.1:5002/ocr/base64";

    public async Task<string> RecognizeAsync(string imagePath, string lang,
        CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        return await PostOcrAsync(Convert.ToBase64String(bytes), null, cancellationToken);
    }

    public async Task<string> RecognizeRegionAsync(string imagePath, Rect pixelBounds, string lang,
        CancellationToken cancellationToken = default)
    {
        var bytes = await File.ReadAllBytesAsync(imagePath, cancellationToken);
        var region = new
        {
            x = (int)pixelBounds.X,
            y = (int)pixelBounds.Y,
            w = (int)pixelBounds.Width,
            h = (int)pixelBounds.Height,
        };
        return await PostOcrAsync(Convert.ToBase64String(bytes), region, cancellationToken);
    }

    private static async Task<string> PostOcrAsync(string imageBase64, object? region,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(new { image_base64 = imageBase64, region });
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await _client.PostAsync(OcrUrl, content, cancellationToken);
        resp.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(cancellationToken));
        var root = doc.RootElement;

        if (!root.GetProperty("success").GetBoolean())
        {
            var err = root.TryGetProperty("error", out var e) ? e.GetString() : "Unknown error";
            throw new InvalidOperationException($"OCR server error: {err}");
        }

        return root.TryGetProperty("text", out var text) ? text.GetString() ?? string.Empty : string.Empty;
    }
}
