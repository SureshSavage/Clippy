using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Clippy;

public partial class AnswerOverlayWindow : Window
{
    private bool _isResizing;
    private Point _resizeStartPos;
    private double _resizeStartWidth;
    private double _resizeStartHeight;

    public AnswerOverlayWindow()
    {
        InitializeComponent();
        PointerPressed += OnPointerPressed;

        ResizeGrip.PointerPressed += OnResizeGripPressed;
        ResizeGrip.PointerMoved += OnResizeGripMoved;
        ResizeGrip.PointerReleased += OnResizeGripReleased;
    }

    private void OnFontIncreaseClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (AnswerText.FontSize < 72)
            AnswerText.FontSize += 2;
    }

    private void OnFontDecreaseClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (AnswerText.FontSize > 10)
            AnswerText.FontSize -= 2;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_isResizing) return;

        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
        }
    }

    private void OnResizeGripPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            e.Handled = true;
            _isResizing = true;
            _resizeStartPos = e.GetPosition(this);
            _resizeStartWidth = Width;
            _resizeStartHeight = Height;
            e.Pointer.Capture(ResizeGrip);
        }
    }

    private void OnResizeGripMoved(object? sender, PointerEventArgs e)
    {
        if (!_isResizing) return;

        var currentPos = e.GetPosition(this);
        var deltaX = currentPos.X - _resizeStartPos.X;
        var deltaY = currentPos.Y - _resizeStartPos.Y;

        var newWidth = Math.Max(MinWidth, _resizeStartWidth + deltaX);
        var newHeight = Math.Max(MinHeight, _resizeStartHeight + deltaY);

        Width = newWidth;
        Height = newHeight;
    }

    private void OnResizeGripReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_isResizing)
        {
            _isResizing = false;
            e.Pointer.Capture(null);
        }
    }

    public void PositionBelowSubtitle(SubtitleOverlayWindow subtitleWindow)
    {
        var subtitlePos = subtitleWindow.Position;
        var subtitleHeight = (int)subtitleWindow.Height;

        Position = new Avalonia.PixelPoint(
            subtitlePos.X,
            subtitlePos.Y + subtitleHeight
        );
    }

    public void UpdateAnswer(string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            AnswerText.Text = text;
        });
    }
}
