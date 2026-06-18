using System.Windows;

namespace HandWritten_OCR.Services;

public interface IKrakenService
{
    Task<string> RecognizeAsync(string imagePath, string modelName, CancellationToken cancellationToken = default);
    Task<string> RecognizeRegionAsync(string imagePath, Rect pixelBounds, string modelName, CancellationToken cancellationToken = default);
}
