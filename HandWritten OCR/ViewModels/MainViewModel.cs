using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using HandWritten_OCR.Abstract;
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

    // ── Image list ────────────────────────────────────────────────────────────
    public ObservableCollection<ImageItem> ImageList { get; } = new();
    public bool HasImageList => ImageList.Count > 0;

    [ObservableProperty]
    private ImageItem? _selectedImageItem;

    partial void OnSelectedImageItemChanged(ImageItem? value)
    {
        if (value is null) return;
        _ = HandleImageSelectionAsync(value);
    }

    // ── DataGrid state ────────────────────────────────────────────────────────
    private DataTable? _gridData;
    private string? _selectedCellColumn;
    private int _selectedRowIndex = -1;

    public DataView? GridView => _gridData?.DefaultView;
    public bool HasGridData => _gridData?.Columns.Count > 0;

    // Fired after OCR fills a cell so DataGridView can advance selection
    public event Action<int, int>? RequestCellFocus;

    // ── Observable properties ─────────────────────────────────────────────────
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

    // ── OCR commands ──────────────────────────────────────────────────────────

    [RelayCommand]
    private async Task OpenImageAsync()
    {
        if (IsProcessing) return;
        string? path = GetFilePath("Image Files", "*.png;*.jpg;*.jpeg;*.bmp;*.tiff;*.gif", "Open Image");
        if (path is null) return;

        AddToImageList(path);
        await LoadImageAsync(path);
        EnsureImageNameColumn();
        ManageDataGridRowForImage(Path.GetFileName(path));
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
                string result = await _ocrService.RecognizeAsync(_currentImagePath);
                OcrText = result;
                HasResult = !string.IsNullOrWhiteSpace(result);
                StatusMessage = result.Length > 0
                    ? $"Done — {result.Length} characters recognized."
                    : "Done — no text detected.";
                FillSelectedCell(result);
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
                    FillSelectedCell(box.ExtractedText ?? string.Empty);
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
        AddToImageList(path);
        await LoadImageAsync(path);
        EnsureImageNameColumn();
        ManageDataGridRowForImage(Path.GetFileName(path));
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

    // ── Image-list command ────────────────────────────────────────────────────

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

    // ── DataGrid commands ─────────────────────────────────────────────────────

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

        string? currentImage = _selectedImageItem?.FileName;
        int insertAt = currentImage is not null
            ? InsertionIndexAfterLastRowOf(currentImage)
            : _gridData.Rows.Count;

        var newRow = _gridData.NewRow();
        if (currentImage is not null && _gridData.Columns.Contains("ImageName"))
            newRow["ImageName"] = currentImage;

        _gridData.Rows.InsertAt(newRow, insertAt);
        _selectedRowIndex = insertAt;
        _selectedCellColumn = FirstEditableColumn();

        if (_selectedCellColumn is not null)
            RequestCellFocus?.Invoke(_selectedRowIndex, GetColumnIndex(_selectedCellColumn));

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

    // ── Image-list helpers ────────────────────────────────────────────────────

    private void AddToImageList(string fullPath)
    {
        if (!ImageList.Any(i => i.FullPath.Equals(fullPath, StringComparison.OrdinalIgnoreCase)))
            ImageList.Add(new ImageItem { FileName = Path.GetFileName(fullPath), FullPath = fullPath });
    }

    private async Task HandleImageSelectionAsync(ImageItem item)
    {
        await LoadImageAsync(item.FullPath);
        if (_gridData is null) return; // No template yet — skip row management
        EnsureImageNameColumn();
        ManageDataGridRowForImage(item.FileName);
    }

    /// <summary>
    /// Ensures "ImageName" column exists in the DataTable as the first column.
    /// </summary>
    private void EnsureImageNameColumn()
    {
        if (_gridData is null || _gridData.Columns.Contains("ImageName")) return;
        _gridData.Columns.Add("ImageName");
        _gridData.Columns["ImageName"]!.SetOrdinal(0);
        OnPropertyChanged(nameof(GridView));
    }

    /// <summary>
    /// Finds or creates a DataGrid row for the given image name, respecting
    /// insertion order so that each image's rows stay grouped together in
    /// the same order they appear in the ImageList.
    /// </summary>
    private void ManageDataGridRowForImage(string imageName)
    {
        if (_gridData is null) return;

        // Find the first existing row for this image
        int existingRow = -1;
        if (_gridData.Columns.Contains("ImageName"))
        {
            for (int i = 0; i < _gridData.Rows.Count; i++)
            {
                if (_gridData.Rows[i]["ImageName"]?.ToString() == imageName)
                {
                    existingRow = i;
                    break;
                }
            }
        }

        if (existingRow >= 0)
        {
            _selectedRowIndex = existingRow;
        }
        else
        {
            // No row yet — insert at the position that keeps image groups in order
            int insertAt = FirstInsertionIndexFor(imageName);
            var newRow = _gridData.NewRow();
            if (_gridData.Columns.Contains("ImageName"))
                newRow["ImageName"] = imageName;
            _gridData.Rows.InsertAt(newRow, insertAt);
            _selectedRowIndex = insertAt;
        }

        _selectedCellColumn = FirstEditableColumn();
        if (_selectedCellColumn is not null)
            RequestCellFocus?.Invoke(_selectedRowIndex, GetColumnIndex(_selectedCellColumn));
    }

    /// <summary>
    /// Returns the index where the first row for <paramref name="imageName"/>
    /// should be inserted so that image groups follow ImageList order.
    /// E.g. if Image1 rows already exist, a new Image2 row goes after them.
    /// If later you add another Image1 row (back-navigation), it still goes
    /// before any Image2 rows.
    /// </summary>
    private int FirstInsertionIndexFor(string imageName)
    {
        int imageListIdx = IndexInImageList(imageName);
        if (imageListIdx < 0 || _gridData is null)
            return _gridData?.Rows.Count ?? 0;

        // Scan rows in order; stop at the first row whose image comes AFTER
        // imageName in the ImageList — insert before that row.
        for (int r = 0; r < _gridData.Rows.Count; r++)
        {
            string rowImage = _gridData.Rows[r]["ImageName"]?.ToString() ?? string.Empty;
            int rowIdx = IndexInImageList(rowImage);
            if (rowIdx > imageListIdx)
                return r;
        }

        return _gridData.Rows.Count; // Append at end
    }

    /// <summary>
    /// Returns the index after the last existing row for <paramref name="imageName"/>.
    /// Used by the Add Row button so manually added rows stay grouped with
    /// their image.
    /// </summary>
    private int InsertionIndexAfterLastRowOf(string imageName)
    {
        if (_gridData is null) return 0;

        int lastRow = -1;
        if (_gridData.Columns.Contains("ImageName"))
        {
            for (int i = 0; i < _gridData.Rows.Count; i++)
            {
                if (_gridData.Rows[i]["ImageName"]?.ToString() == imageName)
                    lastRow = i;
            }
        }

        return lastRow >= 0 ? lastRow + 1 : FirstInsertionIndexFor(imageName);
    }

    private int IndexInImageList(string fileName)
    {
        for (int i = 0; i < ImageList.Count; i++)
            if (ImageList[i].FileName == fileName) return i;
        return -1;
    }

    // ── DataGrid cell helpers ─────────────────────────────────────────────────

    /// <summary>
    /// Writes OCR text into the currently selected cell and advances to the
    /// next editable column (skipping ImageName which is auto-managed).
    /// </summary>
    private void FillSelectedCell(string text)
    {
        if (_gridData is null || _selectedRowIndex < 0) return;
        if (_selectedRowIndex >= _gridData.Rows.Count) return;

        // Skip ImageName column — never overwrite it via OCR
        if (_selectedCellColumn == "ImageName" || _selectedCellColumn is null)
            _selectedCellColumn = FirstEditableColumn();

        if (_selectedCellColumn is null || !_gridData.Columns.Contains(_selectedCellColumn)) return;

        _gridData.Rows[_selectedRowIndex][_selectedCellColumn] = text;

        string? next = NextEditableColumn(_selectedCellColumn);
        if (next is not null) _selectedCellColumn = next;
        RequestCellFocus?.Invoke(_selectedRowIndex, GetColumnIndex(_selectedCellColumn));
    }

    /// <summary>Returns the first column that is not "ImageName".</summary>
    private string? FirstEditableColumn()
    {
        if (_gridData is null) return null;
        foreach (DataColumn col in _gridData.Columns)
            if (col.ColumnName != "ImageName") return col.ColumnName;
        return null;
    }

    /// <summary>Returns the next non-ImageName column after <paramref name="current"/>, or null if none.</summary>
    private string? NextEditableColumn(string current)
    {
        if (_gridData is null) return null;
        int idx = GetColumnIndex(current);
        for (int i = idx + 1; i < _gridData.Columns.Count; i++)
            if (_gridData.Columns[i].ColumnName != "ImageName")
                return _gridData.Columns[i].ColumnName;
        return null;
    }

    private int GetColumnIndex(string colName)
    {
        if (_gridData is null || !_gridData.Columns.Contains(colName)) return 0;
        return _gridData.Columns[colName]!.Ordinal;
    }

    // ── Image loader ──────────────────────────────────────────────────────────

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
