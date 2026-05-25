using System.Windows;

namespace HandWritten_OCR.Helpers;

/// <summary>
/// Pure-math coordinate transforms between canvas WPF units and image pixel space.
/// Handles Stretch=Uniform letterboxing and DPI-aware logical size.
/// All parameters are plain doubles — no WPF dispatcher required.
/// </summary>
public static class RegionCoordinateTransform
{
    /// <summary>
    /// Computes the rendered layout of an image displayed with Stretch=Uniform inside a canvas.
    /// </summary>
    /// <param name="canvasW">Canvas actual width in WPF units.</param>
    /// <param name="canvasH">Canvas actual height in WPF units.</param>
    /// <param name="imgLogicalW">Image logical width (BitmapSource.Width — DPI-aware).</param>
    /// <param name="imgLogicalH">Image logical height (BitmapSource.Height — DPI-aware).</param>
    /// <returns>
    /// renderedW/H — size of the rendered image in WPF units.<br/>
    /// offsetX/Y  — letterbox padding on each axis in WPF units.
    /// </returns>
    public static (double renderedW, double renderedH, double offsetX, double offsetY)
        GetImageLayout(double canvasW, double canvasH, double imgLogicalW, double imgLogicalH)
    {
        if (canvasW <= 0 || canvasH <= 0 || imgLogicalW <= 0 || imgLogicalH <= 0)
            return (0, 0, 0, 0);

        double scale = Math.Min(canvasW / imgLogicalW, canvasH / imgLogicalH);
        double rW = imgLogicalW * scale;
        double rH = imgLogicalH * scale;
        return (rW, rH, (canvasW - rW) / 2.0, (canvasH - rH) / 2.0);
    }

    /// <summary>
    /// Converts a rectangle in canvas WPF units to image pixel coordinates.
    /// The result is clipped to the valid image pixel area; returns <see cref="Rect.Empty"/>
    /// if the canvas rect does not overlap the rendered image.
    /// </summary>
    /// <param name="canvas">Rectangle in canvas WPF units.</param>
    /// <param name="canvasW">Canvas actual width.</param>
    /// <param name="canvasH">Canvas actual height.</param>
    /// <param name="imgLogicalW">Image logical width (BitmapSource.Width).</param>
    /// <param name="imgLogicalH">Image logical height (BitmapSource.Height).</param>
    /// <param name="pixelW">Image pixel width (BitmapSource.PixelWidth).</param>
    /// <param name="pixelH">Image pixel height (BitmapSource.PixelHeight).</param>
    public static Rect CanvasToImage(Rect canvas,
        double canvasW, double canvasH,
        double imgLogicalW, double imgLogicalH,
        double pixelW, double pixelH)
    {
        var (rW, rH, ox, oy) = GetImageLayout(canvasW, canvasH, imgLogicalW, imgLogicalH);
        if (rW <= 0 || rH <= 0) return Rect.Empty;

        var raw = new Rect(
            (canvas.X - ox) * pixelW / rW,
            (canvas.Y - oy) * pixelH / rH,
            canvas.Width  * pixelW / rW,
            canvas.Height * pixelH / rH);

        // Clip to valid image pixel area — boxes drawn in letterbox bands become Empty.
        raw.Intersect(new Rect(0, 0, pixelW, pixelH));
        return raw; // Rect.Intersect sets to Empty when there is no overlap
    }

    /// <summary>
    /// Converts a rectangle in image pixel coordinates back to canvas WPF units.
    /// </summary>
    public static Rect ImageToCanvas(Rect image,
        double canvasW, double canvasH,
        double imgLogicalW, double imgLogicalH,
        double pixelW, double pixelH)
    {
        var (rW, rH, ox, oy) = GetImageLayout(canvasW, canvasH, imgLogicalW, imgLogicalH);
        if (rW <= 0 || rH <= 0) return Rect.Empty;

        return new Rect(
            image.X * rW / pixelW + ox,
            image.Y * rH / pixelH + oy,
            image.Width  * rW / pixelW,
            image.Height * rH / pixelH);
    }
}
