using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HandWritten_OCR.Abstract;
using HandWritten_OCR.Helpers;
using HandWritten_OCR.Models;
using HandWritten_OCR.Services;
using Microsoft.WindowsAPICodePack.Dialogs;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;

namespace HandWritten_OCR.ViewModels;

public partial class MainViewModel : ViewBaseModel
{
    private readonly IOcrService _ocrService;
    private readonly ExcelService _excelService = new();
    private readonly string _modelFolder;
    private string? _currentImagePath;

    // Remembers which image region produced each filled cell, so a future verify
    // view can show the source crop next to the value. Keyed by (row, column).
    private readonly Dictionary<(int Row, string Column), CellProvenance> _cellProvenance = new();

    // Serializes per-region "instant OCR" so rapid draws queue instead of colliding
    // (without showing the blocking processing overlay).
    private readonly SemaphoreSlim _instantOcrGate = new(1, 1);

    public ObservableCollection<ImageItem> ImageList { get; } = new();
    public bool HasImageList => ImageList.Count > 0;

    [ObservableProperty]
    private ImageItem? _selectedImageItem;

    partial void OnSelectedImageItemChanged(ImageItem? value)
    {
        if (value is null) return;

        LoadImageAsync(value.FullPath);

        ImageListHelper help = new ImageListHelper();
        _ = help.HandleImageSelectionAsync(value, _gridData, _selectedRowIndex, _selectedCellColumn, ImageList, RequestCellFocus);
    }

    private DataTable _gridData;
    private string _selectedCellColumn;
    private int _selectedRowIndex = -1;

    public DataView? GridView => _gridData?.DefaultView;
    public bool HasGridData => _gridData?.Columns.Count > 0;

    // Fired after OCR fills a cell so DataGridView can advance selection
    public event Action<int, int> RequestCellFocus;

    #region properties
    [ObservableProperty] private BitmapSource? _previewImage;
    [ObservableProperty] private string _ocrText = string.Empty;
    [ObservableProperty] private bool _hasResult;
    [ObservableProperty] private bool _isProcessing;
    [ObservableProperty] private string _statusMessage = "Ready — open or drop an image to begin.";
    [ObservableProperty] private bool _isRegionMode;
    [ObservableProperty] private double _imageRotation = 0;

    public ObservableCollection<RegionBox> RegionBoxes { get; } = new();
    public bool HasRegionBoxes => RegionBoxes.Count > 0;
    public string RegionModeLabel => IsRegionMode ? "Stop Drawing" : "Draw Regions";

    #endregion

    public MainViewModel() : this(new TrOcrService()) { }

    public MainViewModel(IOcrService ocrService)
    {
        _ocrService = ocrService;
        _modelFolder = Path.Combine(AppContext.BaseDirectory, "models");
        RegionBoxes.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRegionBoxes));
        ImageList.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasImageList));
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

    #region OCR commands 

    [RelayCommand]
    private async Task OpenImageAsync()
    {
        if (IsProcessing) return;
        string? path = GetFilePath("Image Files", "*.png;*.jpg;*.jpeg;*.bmp;*.tiff;*.gif", "Open Image");
        if (path is null) return;
        ImageListHelper help = new ImageListHelper();


        help.AddToImageList(path, ImageList);
        await LoadImageAsync(path);
        help.EnsureImageNameColumn(_gridData);
        help.ManageDataGridRowForImage(Path.GetFileName(path), _gridData, _selectedRowIndex, _selectedCellColumn, ImageList, RequestCellFocus);
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
            DataGridHelper dataHelp = new DataGridHelper();

            if (!_ocrService.IsModelLoaded)
            {
                StatusMessage = "Loading TrOCR models (first run may take a moment)...";
                await _ocrService.LoadModelsAsync(_modelFolder);
            }

            if (RegionBoxes.Count == 0)
            {
                string result = await _ocrService.RecognizeAsync(_currentImagePath);
                OcrText = result;
                HasResult = !string.IsNullOrWhiteSpace(result);
                StatusMessage = result.Length > 0
                    ? $"Done — {result.Length} characters recognized."
                    : "Done — no text detected.";
                dataHelp.FillSelectedCell(result, _gridData, _selectedRowIndex, _selectedCellColumn, RequestCellFocus);
            }
            else
            {
                var parts = new List<string>(RegionBoxes.Count);
                for (int i = 0; i < RegionBoxes.Count; i++)
                {
                    RegionBox box = RegionBoxes[i];
                    StatusMessage = $"Processing region {i + 1} of {RegionBoxes.Count}...";
                    box.ExtractedText = await _ocrService.RecognizeRegionAsync(
                        _currentImagePath, box.ImageBounds);
                    parts.Add($"[Region {box.Id}]\n{box.ExtractedText}");
                    int rowFilled = _selectedRowIndex;
                    string? writtenColumn = dataHelp.FillSelectedCell(box.ExtractedText ?? string.Empty, _gridData, _selectedRowIndex, _selectedCellColumn, RequestCellFocus);
                    if (writtenColumn is not null && rowFilled >= 0)
                        _cellProvenance[(rowFilled, writtenColumn)] = new CellProvenance(_currentImagePath, box.ImageBounds);
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
    private async Task DropImageAsync(string path)
    {
        ImageListHelper help = new ImageListHelper();

        help.AddToImageList(path, ImageList);
        await LoadImageAsync(path);
        help.EnsureImageNameColumn(_gridData);
        help.ManageDataGridRowForImage(Path.GetFileName(path), _gridData, _selectedRowIndex, _selectedCellColumn, ImageList, RequestCellFocus);
    }

    [RelayCommand]
    private void RotateLeft() => ImageRotation -= 90;

    [RelayCommand]
    private void RotateRight() => ImageRotation += 90;

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
        ImageRotation = 0;
        RegionBoxes.Clear();
        ImageList.Clear();
        _cellProvenance.Clear();
        StatusMessage = "Ready — open or drop an image to begin.";
    }

    [RelayCommand]
    private void ToggleRegionMode() => IsRegionMode = !IsRegionMode;

    [RelayCommand]
    private void AddRegion(RegionBox box)
    {
        box.Id = RegionBoxes.Count + 1;
        RegionBoxes.Add(box);
        StatusMessage = $"Region {box.Id} added.";

        // Field-targeted instant OCR: recognize this region now and drop the text
        // into the currently selected cell. Fire-and-forget keeps the command
        // synchronous (the box is always added even while a prior OCR is running);
        // the gate serializes the OCR work. The batch "Run OCR" button still works.
        if (_currentImagePath is not null)
            _ = RecognizeRegionIntoSelectedCellAsync(box);
    }

    private async Task RecognizeRegionIntoSelectedCellAsync(RegionBox box)
    {
        await _instantOcrGate.WaitAsync();
        try
        {
            string? imagePath = _currentImagePath;
            if (imagePath is null) return;

            if (!_ocrService.IsModelLoaded)
            {
                StatusMessage = "Loading TrOCR models (first run may take a moment)...";
                await _ocrService.LoadModelsAsync(_modelFolder);
            }

            StatusMessage = $"Recognizing region {box.Id}...";
            string text = await _ocrService.RecognizeRegionAsync(imagePath, box.ImageBounds);
            box.ExtractedText = text;

            // Mirror all region results into the Recognized Text panel.
            OcrText = string.Join("\n\n", RegionBoxes
                .Where(b => !string.IsNullOrWhiteSpace(b.ExtractedText))
                .Select(b => $"[Region {b.Id}]\n{b.ExtractedText}"));
            HasResult = !string.IsNullOrWhiteSpace(OcrText);

            // Write into the targeted cell and remember where the value came from.
            var dataHelp = new DataGridHelper();
            int rowFilled = _selectedRowIndex;
            string? writtenColumn = dataHelp.FillSelectedCell(
                text ?? string.Empty, _gridData, _selectedRowIndex, _selectedCellColumn, RequestCellFocus);

            if (writtenColumn is not null && rowFilled >= 0)
                _cellProvenance[(rowFilled, writtenColumn)] = new CellProvenance(imagePath, box.ImageBounds);

            StatusMessage = writtenColumn is not null
                ? $"Region {box.Id} → {writtenColumn}: \"{text}\""
                : $"Region {box.Id}: \"{text}\"";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Region {box.Id} OCR failed: {ex.Message}";
        }
        finally
        {
            _instantOcrGate.Release();
        }
    }

    [RelayCommand]
    private void ClearBoxes()
    {
        RegionBoxes.Clear();
        StatusMessage = "Regions cleared.";
    }

    #endregion

    #region Image-list command

    [RelayCommand]
    private void OpenFolder()
    {
        string? folder = GetFolderPath("Select Image Folder");
        if (folder is null) return;

        var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { ".png", ".jpg", ".jpeg", ".bmp", ".tiff", ".gif" };

        var images = Directory.GetFiles(folder)
            .Where(f => extensions.Contains(Path.GetExtension(f)))
            .OrderBy(f => f)
            .Select(f => new ImageItem { FileName = Path.GetFileName(f), FullPath = f })
            .ToList();

        if (images.Count == 0)
        {
            StatusMessage = "No image files found in the selected folder.";
            return;
        }

        ImageList.Clear();
        foreach (var img in images)
            ImageList.Add(img);

        StatusMessage = $"Loaded {images.Count} image(s) from \"{Path.GetFileName(folder)}\".";
    }

    #endregion

    #region DataGrid commands

    [RelayCommand]
    private void ImportTemplate()
    {
        string? path = GetFilePath("Excel Files", "*.xlsx;*.xls", "Import Template");
        if (path is null) return;

        try
        {
            var headers = _excelService.ReadHeaders(path);
            if (headers.Count == 0)
            {
                StatusMessage = "No headers found in the first row of the Excel file.";
                return;
            }

            var table = new DataTable();

            // Always ensure ImageName is the first column
            bool hasImageName = headers.Any(h =>
                h.Equals("ImageName", StringComparison.OrdinalIgnoreCase));
            if (!hasImageName)
                table.Columns.Add("ImageName");

            foreach (var h in headers)
                table.Columns.Add(h);

            _gridData = table;
            _cellProvenance.Clear();   // columns/rows changed — old provenance is invalid
            OnPropertyChanged(nameof(GridView));
            OnPropertyChanged(nameof(HasGridData));

            int total = table.Columns.Count;
            StatusMessage = $"Template loaded — {total} column(s): {string.Join(", ", table.Columns.Cast<DataColumn>().Select(c => c.ColumnName))}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to load template: {ex.Message}";
        }
    }

    [RelayCommand]
    private void AddRow()
    {
        if (_gridData is null) return;

        ImageListHelper imageHelp = new ImageListHelper();
        DataGridHelper dataHelp = new DataGridHelper();

        string? currentImage = _selectedImageItem?.FileName;
        int insertAt = currentImage is not null
            ? imageHelp.InsertionIndexAfterLastRowOf(currentImage, _gridData, ImageList)
            : _gridData.Rows.Count;

        var newRow = _gridData.NewRow();
        if (currentImage is not null && _gridData.Columns.Contains("ImageName"))
            newRow["ImageName"] = currentImage;

        _gridData.Rows.InsertAt(newRow, insertAt);
        _selectedRowIndex = insertAt;
        _selectedCellColumn = dataHelp.FirstEditableColumn(_gridData);

        if (_selectedCellColumn is not null)
            RequestCellFocus?.Invoke(_selectedRowIndex, dataHelp.GetColumnIndex(_selectedCellColumn, _gridData));

        StatusMessage = $"Row {_gridData.Rows.Count} added.";
    }

    [RelayCommand]
    private void DeleteRow()
    {
        if (_gridData is null || _selectedRowIndex < 0 || _selectedRowIndex >= _gridData.Rows.Count) return;
        _gridData.Rows.RemoveAt(_selectedRowIndex);
        _selectedRowIndex = Math.Min(_selectedRowIndex, _gridData.Rows.Count - 1);
        StatusMessage = "Row deleted.";
    }

    [RelayCommand]
    private void Export()
    {
        if (_gridData is null || _gridData.Rows.Count == 0)
        {
            StatusMessage = "No data to export — add rows first.";
            return;
        }

        var dialog = new CommonSaveFileDialog
        {
            DefaultFileName = "OCR_Data",
            DefaultExtension = ".xlsx",
            AlwaysAppendDefaultExtension = true,
        };
        dialog.Filters.Add(new CommonFileDialogFilter("Excel Workbook", "*.xlsx"));

        if (dialog.ShowDialog() != CommonFileDialogResult.Ok) return;

        try
        {
            _excelService.Export(_gridData, dialog.FileName);
            StatusMessage = $"Exported to {Path.GetFileName(dialog.FileName)}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Export failed: {ex.Message}";
        }
    }

    // Called from DataGridView code-behind when selection changes
    public void SetSelectedCell(string? columnName, int rowIndex)
    {
        _selectedCellColumn = columnName;
        _selectedRowIndex = rowIndex;
    }

    /// <summary>
    /// Looks up the source image + region that produced a given cell's value, if it
    /// was filled by OCR. Used by the verify view to show the crop next to the value.
    /// </summary>
    public bool TryGetCellProvenance(int rowIndex, string columnName, out CellProvenance provenance)
        => _cellProvenance.TryGetValue((rowIndex, columnName), out provenance);
    #endregion

    
    public async Task LoadImageAsync(string path)
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

/// <summary>Source image + region rectangle that produced an OCR-filled cell.</summary>
public readonly record struct CellProvenance(string ImagePath, System.Windows.Rect ImageBounds);
