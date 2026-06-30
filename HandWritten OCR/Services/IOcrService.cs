using System.Windows;

namespace HandWritten_OCR.Services;

public interface IOcrService
{
    bool IsModelLoaded { get; }
    Task LoadModelsAsync(string modelFolder);

    /// <summary>Recognize text from the entire image file.</summary>
    Task<string> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default);

    /// <summary>Recognize text from one rectangular region (pixel coordinates).</summary>
    Task<string> RecognizeRegionAsync(string imagePath, Rect pixelBounds, CancellationToken cancellationToken = default);

    /// <summary>
    /// Recognize text from multiple regions in one pass:
    /// the image file is loaded once, all regions are preprocessed in parallel,
    /// and inference is run sequentially per region to avoid ORT thread oversubscription.
    /// <paramref name="progress"/> receives the 0-based index of each completed region.
    /// </summary>
    Task<IReadOnlyList<string>> RecognizeRegionsAsync(
        string imagePath,
        IReadOnlyList<Rect> regions,
        IProgress<int>? progress = null,
        CancellationToken cancellationToken = default);
}
