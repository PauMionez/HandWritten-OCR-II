using HandWritten_OCR.Helpers;
using System.Windows;
using Xunit;

namespace HandWritten_OCR.Tests;

/// <summary>
/// Unit tests for RegionCoordinateTransform — the coordinate math that maps
/// between canvas WPF units and image pixel space.
/// No STA thread required; Rect is a plain value type.
/// </summary>
public class CoordinateTransformTests
{
    // ── GetImageLayout ────────────────────────────────────────────────────────

    [Fact]
    public void GetImageLayout_SquareImageFillsSquareCanvas_NoLetterbox()
    {
        var (rW, rH, ox, oy) = RegionCoordinateTransform.GetImageLayout(500, 500, 500, 500);
        Assert.Equal(500, rW, precision: 6);
        Assert.Equal(500, rH, precision: 6);
        Assert.Equal(0,   ox, precision: 6);
        Assert.Equal(0,   oy, precision: 6);
    }

    [Fact]
    public void GetImageLayout_WideImageInSquareCanvas_LetterboxTopAndBottom()
    {
        // 1000×500 image in 500×500 canvas → scale=0.5 → rendered 500×250, y-bars=125
        var (rW, rH, ox, oy) = RegionCoordinateTransform.GetImageLayout(500, 500, 1000, 500);
        Assert.Equal(500, rW, precision: 6);
        Assert.Equal(250, rH, precision: 6);
        Assert.Equal(0,   ox, precision: 6);
        Assert.Equal(125, oy, precision: 6);
    }

    [Fact]
    public void GetImageLayout_TallImageInSquareCanvas_LetterboxLeftAndRight()
    {
        // 500×1000 image in 500×500 canvas → scale=0.5 → rendered 250×500, x-bars=125
        var (rW, rH, ox, oy) = RegionCoordinateTransform.GetImageLayout(500, 500, 500, 1000);
        Assert.Equal(250, rW, precision: 6);
        Assert.Equal(500, rH, precision: 6);
        Assert.Equal(125, ox, precision: 6);
        Assert.Equal(0,   oy, precision: 6);
    }

    [Fact]
    public void GetImageLayout_ZeroCanvas_AllZeroesReturned()
    {
        var (rW, rH, ox, oy) = RegionCoordinateTransform.GetImageLayout(0, 0, 500, 500);
        Assert.Equal(0, rW); Assert.Equal(0, rH);
        Assert.Equal(0, ox); Assert.Equal(0, oy);
    }

    [Fact]
    public void GetImageLayout_ZeroImage_AllZeroesReturned()
    {
        var (rW, rH, ox, oy) = RegionCoordinateTransform.GetImageLayout(500, 500, 0, 0);
        Assert.Equal(0, rW); Assert.Equal(0, rH);
        Assert.Equal(0, ox); Assert.Equal(0, oy);
    }

    [Fact]
    public void GetImageLayout_NonSquareCanvas_ScaleLimitedByNarrowerAxis()
    {
        // 100×100 image in 400×200 canvas → limited by height → scale=2 → 200×200
        var (rW, rH, ox, oy) = RegionCoordinateTransform.GetImageLayout(400, 200, 100, 100);
        Assert.Equal(200, rW, precision: 6);
        Assert.Equal(200, rH, precision: 6);
        Assert.Equal(100, ox, precision: 6); // (400-200)/2
        Assert.Equal(0,   oy, precision: 6);
    }

    // ── CanvasToImage ─────────────────────────────────────────────────────────

    [Fact]
    public void CanvasToImage_CenterBox_ScalesCorrectly()
    {
        // 1000×1000 image (96 DPI: logical=pixel) in 500×500 canvas → scale=0.5, no letterbox
        // Canvas (100,100,200,200) → image (200,200,400,400)
        var r = RegionCoordinateTransform.CanvasToImage(
            new Rect(100, 100, 200, 200), 500, 500, 1000, 1000, 1000, 1000);
        Assert.Equal(200, r.X,      precision: 6);
        Assert.Equal(200, r.Y,      precision: 6);
        Assert.Equal(400, r.Width,  precision: 6);
        Assert.Equal(400, r.Height, precision: 6);
    }

    [Fact]
    public void CanvasToImage_FullRenderedArea_CoversEntireImage()
    {
        // 1000×500 image in 500×500 canvas → rendered at (0,125,500,250)
        // That canvas rect maps to full image (0,0,1000,500)
        var r = RegionCoordinateTransform.CanvasToImage(
            new Rect(0, 125, 500, 250), 500, 500, 1000, 500, 1000, 500);
        Assert.Equal(0,    r.X,      precision: 4);
        Assert.Equal(0,    r.Y,      precision: 4);
        Assert.Equal(1000, r.Width,  precision: 4);
        Assert.Equal(500,  r.Height, precision: 4);
    }

    [Fact]
    public void CanvasToImage_BoxEntirelyInLetterbar_ReturnsEmpty()
    {
        // 1000×500 image in 500×500 canvas → letterbox bars at y=0..125 and y=375..500
        // Canvas rect (0,0,500,50) is entirely inside the top black bar
        var r = RegionCoordinateTransform.CanvasToImage(
            new Rect(0, 0, 500, 50), 500, 500, 1000, 500, 1000, 500);
        Assert.True(r.IsEmpty, $"Expected Empty; got {r}");
    }

    [Fact]
    public void CanvasToImage_BoxExtendsOutsideAllEdges_ClipsToImageBounds()
    {
        // 500×500 image in 500×500 canvas, no letterbox. Box beyond all four sides.
        var r = RegionCoordinateTransform.CanvasToImage(
            new Rect(-50, -50, 600, 600), 500, 500, 500, 500, 500, 500);
        Assert.Equal(0,   r.X,      precision: 4);
        Assert.Equal(0,   r.Y,      precision: 4);
        Assert.Equal(500, r.Width,  precision: 4);
        Assert.Equal(500, r.Height, precision: 4);
    }

    [Fact]
    public void CanvasToImage_BoxPartiallyInLetterbar_ClipsToImageEdge()
    {
        // 1000×500 image in 500×500 canvas → letterbox y=125 top.
        // Canvas rect starting at y=100 (in bar) but extending into image.
        var r = RegionCoordinateTransform.CanvasToImage(
            new Rect(0, 100, 250, 100), 500, 500, 1000, 500, 1000, 500);
        Assert.False(r.IsEmpty);
        Assert.Equal(0, r.Y, precision: 4); // clipped to top of image
        Assert.True(r.Height > 0);
    }

    [Fact]
    public void CanvasToImage_HighDpiImage_MapsToPixelCoordinates()
    {
        // 192-DPI image: PixelWidth=400, logical Width=200 (at 96 DPI)
        // 200×200 canvas, image fits exactly (no letterbox at logical level)
        // Canvas (50,50,100,100) → pixel (100,100,200,200)
        var r = RegionCoordinateTransform.CanvasToImage(
            new Rect(50, 50, 100, 100), 200, 200, 200, 200, 400, 400);
        Assert.Equal(100, r.X,      precision: 6);
        Assert.Equal(100, r.Y,      precision: 6);
        Assert.Equal(200, r.Width,  precision: 6);
        Assert.Equal(200, r.Height, precision: 6);
    }

    // ── ImageToCanvas ─────────────────────────────────────────────────────────

    [Fact]
    public void ImageToCanvas_FullImageRect_MapsToRenderedArea()
    {
        // 1000×500 image in 500×500 canvas → rendered at (0,125,500,250)
        var r = RegionCoordinateTransform.ImageToCanvas(
            new Rect(0, 0, 1000, 500), 500, 500, 1000, 500, 1000, 500);
        Assert.Equal(0,   r.X,      precision: 6);
        Assert.Equal(125, r.Y,      precision: 6);
        Assert.Equal(500, r.Width,  precision: 6);
        Assert.Equal(250, r.Height, precision: 6);
    }

    [Fact]
    public void ImageToCanvas_HalfwayPoint_ScalesCorrectly()
    {
        // 1000×1000 image in 500×500 canvas, no letterbox, scale=0.5
        // Image (500,500,200,200) → canvas (250,250,100,100)
        var r = RegionCoordinateTransform.ImageToCanvas(
            new Rect(500, 500, 200, 200), 500, 500, 1000, 1000, 1000, 1000);
        Assert.Equal(250, r.X,      precision: 6);
        Assert.Equal(250, r.Y,      precision: 6);
        Assert.Equal(100, r.Width,  precision: 6);
        Assert.Equal(100, r.Height, precision: 6);
    }

    // ── Round-trip invariant ──────────────────────────────────────────────────
    // CanvasToImage followed by ImageToCanvas must recover the original canvas rect
    // to within half a pixel (rounding from floating-point ops).

    [Theory]
    [InlineData(500, 500, 1000, 1000, 1000, 1000,  100, 100, 200, 200)]  // no letterbox
    [InlineData(500, 500, 1000,  500, 1000,  500,    0, 125, 500, 250)]  // wide image
    [InlineData(500, 500,  500, 1000,  500, 1000,  125,   0, 250, 500)]  // tall image
    [InlineData(500, 500, 1000, 1000, 2000, 2000,  100, 100, 200, 200)]  // hi-DPI, no letterbox
    public void CanvasToImage_ThenImageToCanvas_IsIdentity(
        double cw, double ch, double lw, double lh, double pw, double ph,
        double x,  double y,  double w,  double h)
    {
        const double HalfPixel = 0.5;
        var original = new Rect(x, y, w, h);
        var image    = RegionCoordinateTransform.CanvasToImage(original, cw, ch, lw, lh, pw, ph);
        var back     = RegionCoordinateTransform.ImageToCanvas(image,    cw, ch, lw, lh, pw, ph);

        Assert.InRange(back.X,      original.X      - HalfPixel, original.X      + HalfPixel);
        Assert.InRange(back.Y,      original.Y      - HalfPixel, original.Y      + HalfPixel);
        Assert.InRange(back.Width,  original.Width  - HalfPixel, original.Width  + HalfPixel);
        Assert.InRange(back.Height, original.Height - HalfPixel, original.Height + HalfPixel);
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Fact]
    public void CanvasToImage_ZeroSizeCanvas_ReturnsEmpty()
    {
        var r = RegionCoordinateTransform.CanvasToImage(
            new Rect(0, 0, 100, 100), 0, 0, 500, 500, 500, 500);
        Assert.True(r.IsEmpty);
    }

    [Fact]
    public void ImageToCanvas_ZeroSizeCanvas_ReturnsEmpty()
    {
        var r = RegionCoordinateTransform.ImageToCanvas(
            new Rect(0, 0, 100, 100), 0, 0, 500, 500, 500, 500);
        Assert.True(r.IsEmpty);
    }
}
