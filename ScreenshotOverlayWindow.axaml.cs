using System.Diagnostics;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace Clippy;

public partial class ScreenshotOverlayWindow : Window
{
    private bool _isResizing;
    private Point _resizeStartPos;
    private double _resizeStartWidth;
    private double _resizeStartHeight;
    private Process? _sayProcess;

    public ScreenshotOverlayWindow()
    {
        InitializeComponent();
        PointerPressed += OnPointerPressed;

        ResizeGrip.PointerPressed += OnResizeGripPressed;
        ResizeGrip.PointerMoved += OnResizeGripMoved;
        ResizeGrip.PointerReleased += OnResizeGripReleased;
    }

    private void OnCloseClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        StopSpeaking();
        Close();
    }

    private void OnSpeakClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SpeakText(DescriptionText.Text ?? "");
    }

    private void OnFontIncreaseClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DescriptionText.FontSize < 72)
            DescriptionText.FontSize += 2;
    }

    private void OnFontDecreaseClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DescriptionText.FontSize > 10)
            DescriptionText.FontSize -= 2;
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

    public void PositionAtCenter()
    {
        if (Screens.Primary is { } screen)
        {
            var workArea = screen.WorkingArea;
            var x = (workArea.Width - (int)Width) / 2 + workArea.X;
            var y = workArea.Y + 80;
            Position = new PixelPoint(x, y);
        }
    }

    public void SetModelLabel(string modelName)
    {
        ModelLabel.Text = $"Vision: {modelName}";
    }

    public void SetPreview(byte[] imageBytes)
    {
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                using var stream = new MemoryStream(imageBytes);
                PreviewImage.Source = new Bitmap(stream);
            }
            catch { }
        });
    }

    public void UpdateDescription(string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            DescriptionText.Text = text;
        });
    }

    public void SpeakText(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text == "Analyzing screenshot...")
            return;

        StopSpeaking();

        try
        {
            _sayProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "say",
                Arguments = $"\"{text.Replace("\"", "\\\"")}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch { }
    }

    public void StopSpeaking()
    {
        if (_sayProcess != null && !_sayProcess.HasExited)
        {
            try { _sayProcess.Kill(); } catch { }
        }
        _sayProcess = null;
    }
}
