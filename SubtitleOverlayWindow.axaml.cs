using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Clippy;

public partial class SubtitleOverlayWindow : Window
{
    private bool _isResizing;
    private Point _resizeStartPos;
    private double _resizeStartWidth;
    private double _resizeStartHeight;

    public Action<string>? OnAskRequested { get; set; }

    public SubtitleOverlayWindow()
    {
        InitializeComponent();
        PointerPressed += OnPointerPressed;

        ResizeGrip.PointerPressed += OnResizeGripPressed;
        ResizeGrip.PointerMoved += OnResizeGripMoved;
        ResizeGrip.PointerReleased += OnResizeGripReleased;
    }

    private void OnFontIncreaseClicked(object? sender, RoutedEventArgs e)
    {
        if (SubtitleText.FontSize < 72)
            SubtitleText.FontSize += 2;
    }

    private void OnFontDecreaseClicked(object? sender, RoutedEventArgs e)
    {
        if (SubtitleText.FontSize > 10)
            SubtitleText.FontSize -= 2;
    }

    private void OnAskClicked(object? sender, RoutedEventArgs e)
    {
        var text = SubtitleText.Text?.Trim();
        if (!string.IsNullOrWhiteSpace(text) && text != "Listening...")
        {
            OnAskRequested?.Invoke(text);
        }
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

    public void PositionAtBottomCenter()
    {
        var screen = Screens.Primary ?? Screens.All.FirstOrDefault();
        if (screen == null) return;

        var workArea = screen.WorkingArea;
        var scaling = screen.Scaling;

        var screenWidth = workArea.Width / scaling;
        var screenHeight = workArea.Height / scaling;
        var screenX = workArea.X / scaling;
        var screenY = workArea.Y / scaling;

        Position = new Avalonia.PixelPoint(
            (int)(screenX + (screenWidth - Width) / 2 * scaling),
            (int)(screenY + (screenHeight - Height - 40) * scaling)
        );
    }

    public void UpdateSubtitle(string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            SubtitleText.Text = text;
        });
    }
}
