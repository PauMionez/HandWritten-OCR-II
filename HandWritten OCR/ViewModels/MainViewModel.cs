using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HandWritten_OCR.Services;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace HandWritten_OCR.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IOcrService _ocrService;
    private readonly IFileDialogService _fileDialogService;
    private readonly string _modelFolder;
    private string? _currentImagePath;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunOcrCommand))]
    private BitmapSource? _previewImage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResult))]
    private string _ocrText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RunOcrCommand))]
    [NotifyCanExecuteChangedFor(nameof(OpenImageCommand))]
    private bool _isProcessing;

    [ObservableProperty]
    private string _statusMessage = "Ready — open or drop an image to begin.";

    public bool HasResult => !string.IsNullOrWhiteSpace(OcrText);

    public MainViewModel(IOcrService ocrService, IFileDialogService fileDialogService, string modelFolder)
    {
        _ocrService = ocrService;
        _fileDialogService = fileDialogService;
        _modelFolder = modelFolder;
    }

    [RelayCommand(CanExecute = nameof(CanOpenImage))]
    private async Task OpenImageAsync()
    {
        var path = _fileDialogService.OpenImageFile();
        if (path is not null)
            await LoadImageAsync(path);
    }

    private bool CanOpenImage() => !IsProcessing;

    [RelayCommand(CanExecute = nameof(CanRunOcr))]
    private async Task RunOcrAsync()
    {
        if (_currentImagePath is null) return;

        IsProcessing = true;
        OcrText = string.Empty;
        StatusMessage = "Running OCR...";

        try
        {
            if (!_ocrService.IsModelLoaded)
            {
                StatusMessage = "Loading TrOCR models (first run may take a moment)...";
                await _ocrService.LoadModelsAsync(_modelFolder);
            }

            var result = await _ocrService.RecognizeAsync(_currentImagePath);
            OcrText = result;
            StatusMessage = result.Length > 0
                ? $"Done — {result.Length} characters recognized."
                : "Done — no text detected.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            OcrText = string.Empty;
        }
        finally
        {
            IsProcessing = false;
        }
    }

    private bool CanRunOcr() => PreviewImage is not null && !IsProcessing;

    [RelayCommand]
    private async Task DropImageAsync(string path) => await LoadImageAsync(path);

    [RelayCommand]
    private void CopyText()
    {
        if (!string.IsNullOrWhiteSpace(OcrText))
            Clipboard.SetText(OcrText);
    }

    [RelayCommand]
    private void ClearAll()
    {
        PreviewImage = null;
        _currentImagePath = null;
        OcrText = string.Empty;
        StatusMessage = "Ready — open or drop an image to begin.";
    }

    private async Task LoadImageAsync(string path)
    {
        try
        {
            var image = await Task.Run(() =>
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            });

            PreviewImage = image;
            _currentImagePath = path;
            OcrText = string.Empty;
            StatusMessage = $"Loaded: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load image: {ex.Message}";
        }
    }
}
