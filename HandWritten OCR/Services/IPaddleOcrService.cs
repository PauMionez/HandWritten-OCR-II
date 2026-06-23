using System.Windows;

namespace HandWritten_OCR.Services;

public interface IPaddleOcrService
{
    Task<string> RecognizeAsync(string imagePath, string lang, CancellationToken cancellationToken = default);
    Task<string> RecognizeRegionAsync(string imagePath, Rect pixelBounds, string lang, CancellationToken cancellationToken = default);
}
