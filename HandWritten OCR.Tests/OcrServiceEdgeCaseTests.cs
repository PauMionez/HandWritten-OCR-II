using HandWritten_OCR.Services;
using Xunit;

namespace HandWritten_OCR.Tests;

/// <summary>
/// Regression tests for PaddleServerManager initial state and lifecycle.
/// These tests do NOT require a running Python server.
/// </summary>
public class OcrServiceEdgeCaseTests
{
    // ── Initial state ─────────────────────────────────────────────────────────

    [Fact]
    public void PaddleServerManager_InitialIsReady_IsFalse()
    {
        using var mgr = new PaddleServerManager();
        Assert.False(mgr.IsReady);
    }

    [Fact]
    public void PaddleServerManager_InitialIsStarting_IsFalse()
    {
        using var mgr = new PaddleServerManager();
        Assert.False(mgr.IsStarting);
    }

    [Fact]
    public void PaddleServerManager_IsVenvInstalled_FalseWhenPathMissing()
    {
        // In the test environment PaddleVenv is not next to the test runner binary
        using var mgr = new PaddleServerManager();
        Assert.False(mgr.IsVenvInstalled);
    }

    [Fact]
    public void PaddleServerManager_IsScriptDeployed_FalseWhenPathMissing()
    {
        using var mgr = new PaddleServerManager();
        Assert.False(mgr.IsScriptDeployed);
    }

    // ── StartAsync guard: no-ops when venv/script missing ─────────────────────

    [Fact]
    public async Task StartAsync_WhenVenvMissing_DoesNotSetIsStarting()
    {
        using var mgr = new PaddleServerManager();
        await mgr.StartAsync();
        Assert.False(mgr.IsStarting);
        Assert.False(mgr.IsReady);
    }

    // ── Stop safety ───────────────────────────────────────────────────────────

    [Fact]
    public void Stop_WhenNeverStarted_DoesNotThrow()
    {
        using var mgr = new PaddleServerManager();
        var ex = Record.Exception(() => mgr.Stop());
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_CanBeCalledMultipleTimes_NoException()
    {
        var mgr = new PaddleServerManager();
        mgr.Dispose();
        var ex = Record.Exception(() => mgr.Dispose());
        Assert.Null(ex);
    }
}
