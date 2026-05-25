using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows;

namespace HandWritten_OCR.Models;

public partial class RegionBox : ObservableObject
{
    [ObservableProperty] private int _id;
    [ObservableProperty] private Rect _imageBounds;   // pixel coords in the source image
    [ObservableProperty] private Rect _canvasBounds;  // WPF units on the drawing canvas
    [ObservableProperty] private string _extractedText = string.Empty;
}
