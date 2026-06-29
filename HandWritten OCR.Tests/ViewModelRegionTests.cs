using CommunityToolkit.Mvvm.Input;
using HandWritten_OCR.Models;
using HandWritten_OCR.Services;
using HandWritten_OCR.ViewModels;
using System.IO;
using System.Reflection;
using System.Windows;
using Xunit;

namespace HandWritten_OCR.Tests;

/// <summary>
/// Unit + regression tests for the region-selection logic in MainViewModel.
/// A hand-rolled FakePaddleOcrService avoids any dependency on Moq.
/// </summary>
public class ViewModelRegionTests
{
    // ── Fake service ──────────────────────────────────────────────────────────

    private sealed class FakePaddleOcrService : IPaddleOcrService
    {
        public string FullImageResult  { get; set; } = "full image text";
        public string RegionResult     { get; set; } = "region text";
        public int    RecognizeCallCount       { get; private set; }
        public int    RecognizeRegionCallCount { get; private set; }
        public List<Rect> RegionBoundsReceived { get; } = new();

        public Task<string> RecognizeAsync(string _, string lang, CancellationToken __ = default)
        {
            RecognizeCallCount++;
            return Task.FromResult(FullImageResult);
        }

        public Task<string> RecognizeRegionAsync(string _, Rect bounds, string lang, CancellationToken __ = default)
        {
            RecognizeRegionCallCount++;
            RegionBoundsReceived.Add(bounds);
            return Task.FromResult(RegionResult);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static MainViewModel BuildVm(FakePaddleOcrService? svc = null)
    {
        var vm = new MainViewModel(svc ?? new FakePaddleOcrService(), new PaddleServerManager());
        // Mark server ready so RunOcrAsync is not blocked by the readiness guard
        vm.IsPaddleServerReady = true;
        return vm;
    }

    // Set the private _currentImagePath field so RunOcrAsync can proceed.
    private static void SetImagePath(MainViewModel vm, string? path) =>
        typeof(MainViewModel)
            .GetField("_currentImagePath", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(vm, path);

    private static async Task RunOcrAsync(MainViewModel vm) =>
        await ((IAsyncRelayCommand)vm.RunOcrCommand).ExecuteAsync(null);

    // ── ToggleRegionMode ──────────────────────────────────────────────────────

    [Fact]
    public void ToggleRegionMode_InitiallyFalse_BecomesTrue()
    {
        var vm = BuildVm();
        Assert.False(vm.IsRegionMode);
        vm.ToggleRegionModeCommand.Execute(null);
        Assert.True(vm.IsRegionMode);
    }

    [Fact]
    public void ToggleRegionMode_WhenTrue_BecomesFalse()
    {
        var vm = BuildVm();
        vm.ToggleRegionModeCommand.Execute(null);
        vm.ToggleRegionModeCommand.Execute(null);
        Assert.False(vm.IsRegionMode);
    }

    [Fact]
    public void RegionModeLabel_ReflectsCurrentMode()
    {
        var vm = BuildVm();
        Assert.Equal("Draw Regions", vm.RegionModeLabel);
        vm.ToggleRegionModeCommand.Execute(null);
        Assert.Equal("Stop Drawing", vm.RegionModeLabel);
        vm.ToggleRegionModeCommand.Execute(null);
        Assert.Equal("Draw Regions", vm.RegionModeLabel);
    }

    [Fact]
    public void ToggleRegionMode_RaisesPropertyChangedForLabel()
    {
        var vm = BuildVm();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        vm.ToggleRegionModeCommand.Execute(null);

        Assert.Contains(nameof(MainViewModel.IsRegionMode), changed);
        Assert.Contains(nameof(MainViewModel.RegionModeLabel), changed);
    }

    // ── AddRegion ─────────────────────────────────────────────────────────────

    [Fact]
    public void AddRegion_FirstBox_GetsIdOne()
    {
        var vm = BuildVm();
        var box = new RegionBox();
        vm.AddRegionCommand.Execute(box);
        Assert.Equal(1, box.Id);
    }

    [Fact]
    public void AddRegion_ThreeBoxes_IdsAreSequential()
    {
        var vm = BuildVm();
        var boxes = Enumerable.Range(0, 3).Select(_ => new RegionBox()).ToList();
        foreach (var b in boxes) vm.AddRegionCommand.Execute(b);
        Assert.Equal(new[] { 1, 2, 3 }, boxes.Select(b => b.Id).ToArray());
    }

    [Fact]
    public void AddRegion_AddsToCollection()
    {
        var vm = BuildVm();
        Assert.Empty(vm.RegionBoxes);
        vm.AddRegionCommand.Execute(new RegionBox());
        Assert.Single(vm.RegionBoxes);
    }

    [Fact]
    public void HasRegionBoxes_FalseInitially_TrueAfterAdd()
    {
        var vm = BuildVm();
        Assert.False(vm.HasRegionBoxes);
        vm.AddRegionCommand.Execute(new RegionBox());
        Assert.True(vm.HasRegionBoxes);
    }

    // ── Instant OCR (field-targeted recognition on draw) ──────────────────────

    [Fact]
    public void AddRegion_WithImageLoaded_RecognizesRegionImmediately()
    {
        var fake = new FakePaddleOcrService { RegionResult = "Sept" };
        var vm = BuildVm(fake);
        SetImagePath(vm, "dummy.png");

        var box = new RegionBox { ImageBounds = new Rect(0, 0, 100, 100) };
        vm.AddRegionCommand.Execute(box);

        Assert.Equal(1, fake.RecognizeRegionCallCount);
        Assert.Equal("Sept", box.ExtractedText);
    }

    [Fact]
    public void AddRegion_WithNoImageLoaded_DoesNotRecognize()
    {
        var fake = new FakePaddleOcrService();
        var vm = BuildVm(fake);
        // no image path set — instant OCR must be skipped

        vm.AddRegionCommand.Execute(new RegionBox { ImageBounds = new Rect(0, 0, 100, 100) });

        Assert.Equal(0, fake.RecognizeRegionCallCount);
        Assert.Single(vm.RegionBoxes);   // box is still added
    }

    // ── ClearBoxes ────────────────────────────────────────────────────────────

    [Fact]
    public void ClearBoxes_EmptiesCollection()
    {
        var vm = BuildVm();
        vm.AddRegionCommand.Execute(new RegionBox());
        vm.AddRegionCommand.Execute(new RegionBox());
        vm.ClearBoxesCommand.Execute(null);
        Assert.Empty(vm.RegionBoxes);
    }

    [Fact]
    public void ClearBoxes_SetsHasRegionBoxestoFalse()
    {
        var vm = BuildVm();
        vm.AddRegionCommand.Execute(new RegionBox());
        vm.ClearBoxesCommand.Execute(null);
        Assert.False(vm.HasRegionBoxes);
    }

    // ── ClearAll (regression: must also clear regions) ────────────────────────

    [Fact]
    public void ClearAll_ResetsIsRegionModeAndRegionBoxes()
    {
        var vm = BuildVm();
        vm.ToggleRegionModeCommand.Execute(null);
        vm.AddRegionCommand.Execute(new RegionBox());
        vm.AddRegionCommand.Execute(new RegionBox());

        vm.ClearAllCommand.Execute(null);

        Assert.False(vm.IsRegionMode);
        Assert.Empty(vm.RegionBoxes);
        Assert.False(vm.HasRegionBoxes);
    }

    [Fact]
    public void ClearAll_ResetsOcrTextAndHasResult()
    {
        var vm = BuildVm();
        vm.ClearAllCommand.Execute(null);
        Assert.Equal(string.Empty, vm.OcrText);
        Assert.False(vm.HasResult);
    }

    // ── RunOcrAsync branching ─────────────────────────────────────────────────

    [Fact]
    public async Task RunOcrAsync_WhenCurrentImagePathIsNull_DoesNotCallService()
    {
        var fake = new FakePaddleOcrService();
        var vm = BuildVm(fake);
        // _currentImagePath is null by default — the guard must short-circuit

        await RunOcrAsync(vm);

        Assert.Equal(0, fake.RecognizeCallCount);
        Assert.Equal(0, fake.RecognizeRegionCallCount);
    }

    [Fact]
    public async Task RunOcrAsync_WithNoRegions_CallsRecognizeAsyncOnly()
    {
        var fake = new FakePaddleOcrService { FullImageResult = "full text" };
        var vm = BuildVm(fake);
        SetImagePath(vm, "dummy.png");

        await RunOcrAsync(vm);

        Assert.Equal(1, fake.RecognizeCallCount);
        Assert.Equal(0, fake.RecognizeRegionCallCount);
        Assert.Equal("full text", vm.OcrText);
        Assert.True(vm.HasResult);
    }

    [Fact]
    public async Task RunOcrAsync_WithNoRegions_EmptyResult_HasResultIsFalse()
    {
        var fake = new FakePaddleOcrService { FullImageResult = "" };
        var vm = BuildVm(fake);
        SetImagePath(vm, "dummy.png");

        await RunOcrAsync(vm);

        Assert.False(vm.HasResult);
    }

    [Fact]
    public async Task RunOcrAsync_WithOneRegion_CallsRecognizeRegionAsync()
    {
        var fake = new FakePaddleOcrService { RegionResult = "handwriting" };
        var vm = BuildVm(fake);
        SetImagePath(vm, "dummy.png");
        vm.AddRegionCommand.Execute(new RegionBox { ImageBounds = new Rect(0, 0, 100, 100) });

        int beforeRun = fake.RecognizeRegionCallCount;
        await RunOcrAsync(vm);

        Assert.Equal(0, fake.RecognizeCallCount);
        Assert.Equal(beforeRun + 1, fake.RecognizeRegionCallCount);
    }

    [Fact]
    public async Task RunOcrAsync_WithTwoRegions_CallsRecognizeRegionAsyncTwice()
    {
        var fake = new FakePaddleOcrService();
        var vm = BuildVm(fake);
        SetImagePath(vm, "dummy.png");
        vm.AddRegionCommand.Execute(new RegionBox { ImageBounds = new Rect(0,  0, 100, 100) });
        vm.AddRegionCommand.Execute(new RegionBox { ImageBounds = new Rect(50, 0, 100, 100) });

        int beforeRun = fake.RecognizeRegionCallCount;
        await RunOcrAsync(vm);

        Assert.Equal(beforeRun + 2, fake.RecognizeRegionCallCount);
    }

    [Fact]
    public async Task RunOcrAsync_WithRegions_OcrTextContainsRegionLabels()
    {
        var fake = new FakePaddleOcrService { RegionResult = "the quick fox" };
        var vm = BuildVm(fake);
        SetImagePath(vm, "dummy.png");
        vm.AddRegionCommand.Execute(new RegionBox { ImageBounds = new Rect(0, 0, 100, 100) });

        await RunOcrAsync(vm);

        Assert.Contains("[Region 1]", vm.OcrText);
        Assert.Contains("the quick fox", vm.OcrText);
        Assert.True(vm.HasResult);
    }

    [Fact]
    public async Task RunOcrAsync_WithTwoRegions_BothLabelsInOcrText()
    {
        var fake = new FakePaddleOcrService { RegionResult = "text" };
        var vm = BuildVm(fake);
        SetImagePath(vm, "dummy.png");
        vm.AddRegionCommand.Execute(new RegionBox { ImageBounds = new Rect(0,   0, 100, 100) });
        vm.AddRegionCommand.Execute(new RegionBox { ImageBounds = new Rect(100, 0, 100, 100) });

        await RunOcrAsync(vm);

        Assert.Contains("[Region 1]", vm.OcrText);
        Assert.Contains("[Region 2]", vm.OcrText);
    }

    [Fact]
    public async Task RunOcrAsync_WithRegions_SetsExtractedTextOnEachBox()
    {
        var fake = new FakePaddleOcrService { RegionResult = "written text" };
        var vm = BuildVm(fake);
        SetImagePath(vm, "dummy.png");
        var box = new RegionBox { ImageBounds = new Rect(0, 0, 100, 100) };
        vm.AddRegionCommand.Execute(box);

        await RunOcrAsync(vm);

        Assert.Equal("written text", box.ExtractedText);
    }

    [Fact]
    public async Task RunOcrAsync_WithRegions_PassesCorrectBoundsToService()
    {
        var fake = new FakePaddleOcrService();
        var vm = BuildVm(fake);
        SetImagePath(vm, "dummy.png");
        var expectedBounds = new Rect(10, 20, 300, 150);
        vm.AddRegionCommand.Execute(new RegionBox { ImageBounds = expectedBounds });

        await RunOcrAsync(vm);

        Assert.NotEmpty(fake.RegionBoundsReceived);
        Assert.All(fake.RegionBoundsReceived, b => Assert.Equal(expectedBounds, b));
    }

    [Fact]
    public async Task RunOcrAsync_DisablesRegionModeBeforeProcessing()
    {
        var fake = new FakePaddleOcrService();
        var vm = BuildVm(fake);
        SetImagePath(vm, "dummy.png");
        vm.ToggleRegionModeCommand.Execute(null);
        vm.AddRegionCommand.Execute(new RegionBox { ImageBounds = new Rect(0, 0, 100, 100) });

        await RunOcrAsync(vm);

        Assert.False(vm.IsRegionMode);
    }

    // ── LoadImage (STA required for BitmapImage): clears existing regions ─────

    [Fact]
    public void LoadImage_ClearsRegionBoxesAndResetsRegionMode()
    {
        Exception? error = null;
        bool boxesClearedAfterLoad = false;
        bool regionModeResetAfterLoad = false;
        int countBeforeLoad = 0;

        var thread = new Thread(() =>
        {
            try
            {
                var vm = BuildVm();
                vm.ToggleRegionModeCommand.Execute(null);
                vm.AddRegionCommand.Execute(new RegionBox { ImageBounds = new Rect(0, 0, 50, 50) });
                vm.AddRegionCommand.Execute(new RegionBox { ImageBounds = new Rect(0, 0, 50, 50) });
                countBeforeLoad = vm.RegionBoxes.Count;

                string tmp = Path.ChangeExtension(Path.GetTempFileName(), ".png");
                try
                {
                    using (var bmp = new System.Drawing.Bitmap(8, 8))
                        bmp.Save(tmp, System.Drawing.Imaging.ImageFormat.Png);

                    var dropCmd = (IAsyncRelayCommand)vm.DropImageCommand;
                    dropCmd.ExecuteAsync(tmp).GetAwaiter().GetResult();

                    boxesClearedAfterLoad    = vm.RegionBoxes.Count == 0;
                    regionModeResetAfterLoad = !vm.IsRegionMode;
                }
                finally { if (File.Exists(tmp)) File.Delete(tmp); }
            }
            catch (Exception ex) { error = ex; }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (error != null) throw error;
        Assert.Equal(2, countBeforeLoad);
        Assert.True(boxesClearedAfterLoad,    "RegionBoxes must be cleared when a new image is loaded.");
        Assert.True(regionModeResetAfterLoad, "IsRegionMode must be reset when a new image is loaded.");
    }
}
