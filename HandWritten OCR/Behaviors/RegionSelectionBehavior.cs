using HandWritten_OCR.Helpers;
using HandWritten_OCR.Models;
using Microsoft.Xaml.Behaviors;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace HandWritten_OCR.Behaviors;

public sealed class RegionSelectionBehavior : Behavior<Canvas>
{
    private Point _dragStart;
    private Rectangle? _rubberBand;
    private bool _isDragging;

    // ── Dependency Properties ────────────────────────────────────────────────

    public static readonly DependencyProperty AddRegionCommandProperty =
        DependencyProperty.Register(nameof(AddRegionCommand), typeof(ICommand),
            typeof(RegionSelectionBehavior), new PropertyMetadata(null));

    public ICommand? AddRegionCommand
    {
        get => (ICommand?)GetValue(AddRegionCommandProperty);
        set => SetValue(AddRegionCommandProperty, value);
    }

    public static readonly DependencyProperty RegionBoxesProperty =
        DependencyProperty.Register(nameof(RegionBoxes), typeof(ObservableCollection<RegionBox>),
            typeof(RegionSelectionBehavior), new PropertyMetadata(null, OnRegionBoxesChanged));

    public ObservableCollection<RegionBox>? RegionBoxes
    {
        get => (ObservableCollection<RegionBox>?)GetValue(RegionBoxesProperty);
        set => SetValue(RegionBoxesProperty, value);
    }

    public static readonly DependencyProperty IsActiveProperty =
        DependencyProperty.Register(nameof(IsActive), typeof(bool),
            typeof(RegionSelectionBehavior), new PropertyMetadata(false));

    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    public static readonly DependencyProperty ImageSourceProperty =
        DependencyProperty.Register(nameof(ImageSource), typeof(BitmapSource),
            typeof(RegionSelectionBehavior), new PropertyMetadata(null));

    public BitmapSource? ImageSource
    {
        get => (BitmapSource?)GetValue(ImageSourceProperty);
        set => SetValue(ImageSourceProperty, value);
    }

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override void OnAttached()
    {
        base.OnAttached();
        AssociatedObject.MouseLeftButtonDown += OnMouseDown;
        AssociatedObject.MouseMove           += OnMouseMove;
        AssociatedObject.MouseLeftButtonUp   += OnMouseUp;
        AssociatedObject.SizeChanged         += OnCanvasSizeChanged;
    }

    protected override void OnDetaching()
    {
        base.OnDetaching();
        AssociatedObject.MouseLeftButtonDown -= OnMouseDown;
        AssociatedObject.MouseMove           -= OnMouseMove;
        AssociatedObject.MouseLeftButtonUp   -= OnMouseUp;
        AssociatedObject.SizeChanged         -= OnCanvasSizeChanged;
    }

    // ── Collection tracking ──────────────────────────────────────────────────

    // Persistent box visuals, painted directly on the drawing canvas (NOT a
    // separate ItemsControl). The rubber band renders correctly on this canvas,
    // so a committed box drawn here at the same coordinates lines up exactly —
    // there is no second layer to drift out of alignment.
    private readonly Dictionary<RegionBox, List<UIElement>> _boxVisuals = new();

    private static void OnRegionBoxesChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (RegionSelectionBehavior)d;
        if (e.OldValue is ObservableCollection<RegionBox> old)
        {
            old.CollectionChanged -= self.OnCollectionChanged;
            self.ClearAllBoxVisuals();
        }
        if (e.NewValue is ObservableCollection<RegionBox> @new)
        {
            @new.CollectionChanged += self.OnCollectionChanged;
            // Render any boxes that already exist (e.g. a loaded template).
            foreach (var box in @new) self.AddBoxVisual(box);
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        switch (e.Action)
        {
            case NotifyCollectionChangedAction.Add:
                if (e.NewItems is not null)
                    foreach (RegionBox box in e.NewItems) AddBoxVisual(box);
                break;
            case NotifyCollectionChangedAction.Remove:
                if (e.OldItems is not null)
                    foreach (RegionBox box in e.OldItems) RemoveBoxVisual(box);
                break;
            case NotifyCollectionChangedAction.Reset:
                ClearAllBoxVisuals();
                break;
        }
    }

    // Paint a box (outline + id label) on the drawing canvas at its CanvasBounds.
    // Centered stroke mirrors the rubber band so the committed box lands exactly
    // where it was drawn.
    private void AddBoxVisual(RegionBox box)
    {
        if (_boxVisuals.ContainsKey(box)) return;

        var rect = new Rectangle
        {
            Width  = box.CanvasBounds.Width,
            Height = box.CanvasBounds.Height,
            Stroke = new SolidColorBrush(Color.FromRgb(64, 128, 255)),
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(32, 64, 128, 255)),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(rect, box.CanvasBounds.X);
        Canvas.SetTop(rect, box.CanvasBounds.Y);

        var label = new TextBlock
        {
            Text = $"#{box.Id}",
            Foreground = new SolidColorBrush(Colors.White),
            FontWeight = FontWeights.Bold,
            FontSize = 11,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(label, box.CanvasBounds.X + 3);
        Canvas.SetTop(label, box.CanvasBounds.Y + 1);

        AssociatedObject.Children.Add(rect);
        AssociatedObject.Children.Add(label);
        _boxVisuals[box] = new List<UIElement> { rect, label };
    }

    private void RemoveBoxVisual(RegionBox box)
    {
        if (!_boxVisuals.TryGetValue(box, out var visuals)) return;
        foreach (var v in visuals) AssociatedObject.Children.Remove(v);
        _boxVisuals.Remove(box);
    }

    private void ClearAllBoxVisuals()
    {
        foreach (var visuals in _boxVisuals.Values)
            foreach (var v in visuals) AssociatedObject.Children.Remove(v);
        _boxVisuals.Clear();
    }

    // No resize recompute: boxes are positioned in canvas coords at draw time and
    // share the image's transform, so they track the image during pan/zoom. (A
    // window resize won't rescale them — acceptable; OCR uses fixed ImageBounds.)
    private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e)
    {
    }

    // ── Mouse handlers ───────────────────────────────────────────────────────

    private void OnMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!IsActive || ImageSource is null) return;
        _dragStart = e.GetPosition(AssociatedObject);
        _rubberBand = new Rectangle
        {
            Stroke = new SolidColorBrush(Color.FromRgb(64, 128, 255)),
            StrokeDashArray = new DoubleCollection { 4, 2 },
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(30, 64, 128, 255)),
            Width = 0,
            Height = 0
        };
        Canvas.SetLeft(_rubberBand, _dragStart.X);
        Canvas.SetTop(_rubberBand, _dragStart.Y);
        AssociatedObject.Children.Add(_rubberBand);
        AssociatedObject.CaptureMouse();
        _isDragging = true;
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDragging || _rubberBand is null) return;
        Point pos = e.GetPosition(AssociatedObject);
        Canvas.SetLeft(_rubberBand, Math.Min(pos.X, _dragStart.X));
        Canvas.SetTop(_rubberBand, Math.Min(pos.Y, _dragStart.Y));
        _rubberBand.Width  = Math.Abs(pos.X - _dragStart.X);
        _rubberBand.Height = Math.Abs(pos.Y - _dragStart.Y);
        e.Handled = true;
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDragging || _rubberBand is null) return;

        AssociatedObject.Children.Remove(_rubberBand);
        _rubberBand = null;
        _isDragging = false;
        AssociatedObject.ReleaseMouseCapture();

        Point pos = e.GetPosition(AssociatedObject);
        double cx = Math.Min(pos.X, _dragStart.X);
        double cy = Math.Min(pos.Y, _dragStart.Y);
        double cw = Math.Abs(pos.X - _dragStart.X);
        double ch = Math.Abs(pos.Y - _dragStart.Y);

        if (cw < 8 || ch < 8) { e.Handled = true; return; }

        Rect canvasBounds = new Rect(cx, cy, cw, ch);
        Rect imageBounds  = ToImage(canvasBounds);

        if (imageBounds.IsEmpty || imageBounds.Width <= 0 || imageBounds.Height <= 0)
        { e.Handled = true; return; }

        var box = new RegionBox { CanvasBounds = canvasBounds, ImageBounds = imageBounds };
        AddRegionCommand?.Execute(box);
        e.Handled = true;
    }

    // ── Coordinate helpers (delegate to the testable static helper) ──────────

    private Rect ToImage(Rect canvas)
    {
        if (ImageSource is null) return Rect.Empty;
        return RegionCoordinateTransform.CanvasToImage(canvas,
            AssociatedObject.ActualWidth, AssociatedObject.ActualHeight,
            ImageSource.Width, ImageSource.Height,
            ImageSource.PixelWidth, ImageSource.PixelHeight);
    }

    private Rect ToCanvas(Rect image)
    {
        if (ImageSource is null) return Rect.Empty;
        return RegionCoordinateTransform.ImageToCanvas(image,
            AssociatedObject.ActualWidth, AssociatedObject.ActualHeight,
            ImageSource.Width, ImageSource.Height,
            ImageSource.PixelWidth, ImageSource.PixelHeight);
    }
}
