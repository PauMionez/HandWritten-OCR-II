using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HandWritten_OCR.Abstract;
using HandWritten_OCR.Models;
using HandWritten_OCR.Services;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace HandWritten_OCR.ViewModels;

public partial class MainViewModel : ViewBaseModel
{
    private readonly IOcrService _ocrService;
    private readonly string _modelFolder;
    private string? _currentImagePath;

    [ObservableProperty]
    private BitmapSource? _previewImage;

    [ObservableProperty]
    private string _ocrText = string.Empty;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private bool _isProcessing;

    [ObservableProperty]
    private string _statusMessage = "Ready — open or drop an image to begin.";

    [ObservableProperty]
    private bool _isRegionMode;

    public ObservableCollection<RegionBox> RegionBoxes { get; } = new();

    public bool HasRegionBoxes => RegionBoxes.Count > 0;

    public string RegionModeLabel => IsRegionMode ? "Stop Drawing" : "Draw Regions";

    public MainViewModel() : this(new TrOcrService()) { }

    // Injection constructor used by unit tests (pass a mock/fake IOcrService).
    public MainViewModel(IOcrService ocrService)
    {
        _ocrService = ocrService;
        _modelFolder = Path.Combine(AppContext.BaseDirectory, "models");
        RegionBoxes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRegionBoxes));
    }

    partial void OnIsRegionModeChanged(bool value)
    {
        OnPropertyChanged(nameof(RegionModeLabel));
        StatusMessage = value
            ? "Region mode — click and drag to mark handwritten areas, then click Run OCR."
            : RegionBoxes.Count > 0
                ? $"{RegionBoxes.Count} region(s) marked. Click Run OCR to process."
                : "Ready — open or drop an image to begin.";
    }

    // ── Commands ──────────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task OpenImageAsync()
    {
        if (IsProcessing) return;
        string path = GetFilePath("Image Files", "*.png;*.jpg;*.jpeg;*.bmp;*.tiff;*.gif", "Open Image");
        if (path is not null)
            await LoadImageAsync(path);
    }

    [RelayCommand]
    private async Task RunOcrAsync()
    {
        if (_currentImagePath is null || IsProcessing) return;

        IsProcessing = true;
        OcrText = string.Empty;
        HasResult = false;
        IsRegionMode = false;
        StatusMessage = "Running OCR...";

        try
        {
            if (!_ocrService.IsModelLoaded)
            {
                StatusMessage = "Loading TrOCR models (first run may take a moment)...";
                await _ocrService.LoadModelsAsync(_modelFolder);
            }

            if (RegionBoxes.Count == 0)
            {
                // Full-image OCR — existing behaviour preserved
                string result = await _ocrService.RecognizeAsync(_currentImagePath);
                OcrText = result;
                HasResult = !string.IsNullOrWhiteSpace(result);
                StatusMessage = result.Length > 0
                    ? $"Done — {result.Length} characters recognized."
                    : "Done — no text detected.";
            }
            else
            {
                // Region-based OCR — sequential to avoid shared-Bitmap races
                var parts = new List<string>(RegionBoxes.Count);
                for (int i = 0; i < RegionBoxes.Count; i++)
                {
                    RegionBox box = RegionBoxes[i];
                    StatusMessage = $"Processing region {i + 1} of {RegionBoxes.Count}...";
                    box.ExtractedText = await _ocrService.RecognizeRegionAsync(
                        _currentImagePath, box.ImageBounds);
                    parts.Add($"[Region {box.Id}]\n{box.ExtractedText}");
                }
                OcrText = string.Join("\n\n", parts);
                HasResult = !string.IsNullOrWhiteSpace(OcrText);
                StatusMessage = $"Done — {RegionBoxes.Count} region(s) processed.";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error: {ex.Message}";
            OcrText = string.Empty;
            HasResult = false;
        }
        finally
        {
            IsProcessing = false;
        }
    }

    [RelayCommand]
    private async Task DropImageAsync(string path) => await LoadImageAsync(path);

    [RelayCommand]
    private void CopyText()
    {
        if (RegionBoxes.Count > 0)
        {
            var textOnly = string.Join("\n", RegionBoxes
                .Where(b => !string.IsNullOrWhiteSpace(b.ExtractedText))
                .Select(b => b.ExtractedText!.Trim()));
            if (!string.IsNullOrWhiteSpace(textOnly))
                Clipboard.SetText(textOnly);
        }
        else if (!string.IsNullOrWhiteSpace(OcrText))
        {
            Clipboard.SetText(OcrText);
        }
    }

    [RelayCommand]
    private void ClearAll()
    {
        PreviewImage = null;
        _currentImagePath = null;
        OcrText = string.Empty;
        HasResult = false;
        IsRegionMode = false;
        RegionBoxes.Clear();
        StatusMessage = "Ready — open or drop an image to begin.";
    }

    [RelayCommand]
    private void ToggleRegionMode() => IsRegionMode = !IsRegionMode;

    [RelayCommand]
    private void AddRegion(RegionBox box)
    {
        box.Id = RegionBoxes.Count + 1;
        RegionBoxes.Add(box);
        StatusMessage = $"Region {box.Id} added. Draw more or click Run OCR to process.";
    }

    [RelayCommand]
    private void ClearBoxes()
    {
        RegionBoxes.Clear();
        StatusMessage = "Regions cleared.";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private async Task LoadImageAsync(string path)
    {
        try
        {
            BitmapSource image = await Task.Run(() =>
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
            HasResult = false;
            IsRegionMode = false;
            RegionBoxes.Clear();
            StatusMessage = $"Loaded: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load image: {ex.Message}";
        }
    }
}
