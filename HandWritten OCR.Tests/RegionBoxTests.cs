using HandWritten_OCR.Models;
using System.Windows;
using Xunit;

namespace HandWritten_OCR.Tests;

/// <summary>
/// Unit tests for the RegionBox model — verifies observable property
/// change notifications and default state.
/// </summary>
public class RegionBoxTests
{
    [Fact]
    public void NewRegionBox_Id_DefaultsToZero()
    {
        Assert.Equal(0, new RegionBox().Id);
    }

    [Fact]
    public void NewRegionBox_ExtractedText_DefaultsToEmpty()
    {
        Assert.Equal(string.Empty, new RegionBox().ExtractedText);
    }

    [Fact]
    public void NewRegionBox_ImageBounds_DefaultsToZeroRect()
    {
        var b = new RegionBox().ImageBounds;
        // default(Rect) = {0,0,0,0}, not Rect.Empty
        Assert.Equal(0, b.X); Assert.Equal(0, b.Y);
        Assert.Equal(0, b.Width); Assert.Equal(0, b.Height);
    }

    [Fact]
    public void Id_WhenSet_RaisesPropertyChanged()
    {
        var box = new RegionBox();
        string? fired = null;
        box.PropertyChanged += (_, e) => fired = e.PropertyName;

        box.Id = 7;

        Assert.Equal(nameof(RegionBox.Id), fired);
        Assert.Equal(7, box.Id);
    }

    [Fact]
    public void ImageBounds_WhenSet_RaisesPropertyChanged()
    {
        var box = new RegionBox();
        bool notified = false;
        box.PropertyChanged += (_, e) =>
        { if (e.PropertyName == nameof(RegionBox.ImageBounds)) notified = true; };

        box.ImageBounds = new Rect(10, 20, 300, 400);

        Assert.True(notified);
        Assert.Equal(new Rect(10, 20, 300, 400), box.ImageBounds);
    }

    [Fact]
    public void CanvasBounds_WhenSet_RaisesPropertyChanged()
    {
        var box = new RegionBox();
        bool notified = false;
        box.PropertyChanged += (_, e) =>
        { if (e.PropertyName == nameof(RegionBox.CanvasBounds)) notified = true; };

        box.CanvasBounds = new Rect(5, 5, 80, 60);

        Assert.True(notified);
        Assert.Equal(new Rect(5, 5, 80, 60), box.CanvasBounds);
    }

    [Fact]
    public void ExtractedText_WhenSet_RaisesPropertyChanged()
    {
        var box = new RegionBox();
        bool notified = false;
        box.PropertyChanged += (_, e) =>
        { if (e.PropertyName == nameof(RegionBox.ExtractedText)) notified = true; };

        box.ExtractedText = "Hello, World!";

        Assert.True(notified);
        Assert.Equal("Hello, World!", box.ExtractedText);
    }

    [Fact]
    public void SameValue_DoesNotRaisePropertyChanged()
    {
        var box = new RegionBox { Id = 3 };
        int count = 0;
        box.PropertyChanged += (_, _) => count++;

        box.Id = 3; // same value

        Assert.Equal(0, count);
    }
}
