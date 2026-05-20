namespace HandWritten_OCR.Services;

public interface IOcrService
{
    bool IsModelLoaded { get; }
    Task LoadModelsAsync(string modelFolder);
    Task<string> RecognizeAsync(string imagePath, CancellationToken cancellationToken = default);
}
