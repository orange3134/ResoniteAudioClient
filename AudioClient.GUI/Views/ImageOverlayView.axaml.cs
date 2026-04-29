using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AudioClient.GUI.ViewModels;

namespace AudioClient.GUI.Views;

public partial class ImageOverlayView : UserControl
{
    private const double ZoomScale = 2.5;
    private const double DragThreshold = 4;

    private readonly ScaleTransform _scaleTransform = new(1, 1);
    private readonly TranslateTransform _translateTransform = new();

    private bool _isZoomed;
    private bool _isPointerDown;
    private bool _isDragging;
    private Point _pressViewportPoint;
    private Point _pressImagePoint;
    private Point _startTranslate;

    public ImageOverlayView()
    {
        InitializeComponent();

        var transforms = new TransformGroup();
        transforms.Children.Add(_scaleTransform);
        transforms.Children.Add(_translateTransform);
        PreviewImage.RenderTransform = transforms;

        DataContextChanged += (_, _) => ResetZoom();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && IsVisible)
                ResetZoom();
        };
    }

    private void PreviewImage_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        _isPointerDown = true;
        _isDragging = false;
        _pressViewportPoint = e.GetPosition(Viewport);
        _pressImagePoint = e.GetPosition(PreviewImage);
        _startTranslate = new Point(_translateTransform.X, _translateTransform.Y);
        e.Pointer.Capture(PreviewImage);
        e.Handled = true;
    }

    private void PreviewImage_PointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isPointerDown || !_isZoomed)
            return;

        var point = e.GetPosition(Viewport);
        var delta = point - _pressViewportPoint;
        if (!_isDragging && (Math.Abs(delta.X) > DragThreshold || Math.Abs(delta.Y) > DragThreshold))
            _isDragging = true;

        if (_isDragging)
        {
            _translateTransform.X = _startTranslate.X + delta.X;
            _translateTransform.Y = _startTranslate.Y + delta.Y;
        }

        e.Handled = true;
    }

    private void PreviewImage_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isPointerDown)
            return;

        _isPointerDown = false;
        e.Pointer.Capture(null);

        if (!_isDragging)
            ToggleZoom(_pressImagePoint);

        e.Handled = true;
    }

    private void Viewport_PointerWheelChanged(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y < 0 && _isZoomed)
        {
            ResetZoom();
            e.Handled = true;
        }
    }

    private void ToggleZoom(Point imagePoint)
    {
        if (_isZoomed)
        {
            ResetZoom();
            return;
        }

        var imageOrigin = PreviewImage.TranslatePoint(new Point(0, 0), Viewport) ?? new Point();
        var viewportCenter = new Point(Viewport.Bounds.Width / 2, Viewport.Bounds.Height / 2);

        _scaleTransform.ScaleX = ZoomScale;
        _scaleTransform.ScaleY = ZoomScale;
        _translateTransform.X = viewportCenter.X - imageOrigin.X - imagePoint.X * ZoomScale;
        _translateTransform.Y = viewportCenter.Y - imageOrigin.Y - imagePoint.Y * ZoomScale;
        _isZoomed = true;
    }

    private void ResetZoom()
    {
        _scaleTransform.ScaleX = 1;
        _scaleTransform.ScaleY = 1;
        _translateTransform.X = 0;
        _translateTransform.Y = 0;
        _isZoomed = false;
        _isPointerDown = false;
        _isDragging = false;
    }
}
