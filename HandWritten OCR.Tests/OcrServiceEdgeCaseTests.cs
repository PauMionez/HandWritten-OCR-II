using HandWritten_OCR.Services;
using System.Windows;
using Xunit;

namespace HandWritten_OCR.Tests;

/// <summary>
/// Regression tests for TrOcrService edge-case behaviour.
/// These tests do NOT require model files to be present.
/// </summary>
public class OcrServiceEdgeCaseTests
{
    // ── Guard: models must be loaded before inference ─────────────────────────

    [Fact]
    public async Task RecognizeAsync_WhenModelsNotLoaded_ThrowsInvalidOperationException()
    {
        using var svc = new TrOcrService();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.RecognizeAsync("any_file.png"));
    }

    [Fact]
    public async Task RecognizeRegionAsync_WhenModelsNotLoaded_ThrowsInvalidOperationException()
    {
        using var svc = new TrOcrService();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.RecognizeRegionAsync("any_file.png", new Rect(0, 0, 100, 100)));
    }

    [Fact]
    public async Task RecognizeRegionAsync_WithEmptyRect_WhenModelsNotLoaded_ThrowsInvalidOperationException()
    {
        using var svc = new TrOcrService();

        // Rect.Empty is a degenerate rect — the guard should still fire before we
        // attempt to open the file, so the same exception is expected.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.RecognizeRegionAsync("any_file.png", Rect.Empty));
    }

    // ── IsModelLoaded state ───────────────────────────────────────────────────

    [Fact]
    public void IsModelLoaded_BeforeLoadModelsAsync_IsFalse()
    {
        using var svc = new TrOcrService();
        Assert.False(svc.IsModelLoaded);
    }

    // ── Dispose safety ────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes_NoException()
    {
        var svc = new TrOcrService();
        svc.Dispose();
        // second Dispose must not throw
        var ex = Record.Exception(() => svc.Dispose());
        Assert.Null(ex);
    }
}
