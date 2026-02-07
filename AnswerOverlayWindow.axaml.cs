using System.Diagnostics;
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
    private Process? _sayProcess;

    public Action? OnCloseRequested { get; set; }

    public AnswerOverlayWindow()
    {
        InitializeComponent();
        PointerPressed += OnPointerPressed;

        ResizeGrip.PointerPressed += OnResizeGripPressed;
        ResizeGrip.PointerMoved += OnResizeGripMoved;
        ResizeGrip.PointerReleased += OnResizeGripReleased;
    }

    private void OnCloseClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        OnCloseRequested?.Invoke();
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

    public void SetModelLabel(string modelName)
    {
        ModelLabel.Text = $"Model: {modelName}";
    }

    public void UpdateAnswer(string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            AnswerText.Text = text;
        });
    }

    private void OnSpeakClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        SpeakAnswer();
    }

    public void SpeakAnswer()
    {
        var text = AnswerText.Text ?? "";
        // Extract just the answer portion after "A: "
        var idx = text.IndexOf("A: ", StringComparison.Ordinal);
        var answer = idx >= 0 ? text.Substring(idx + 3).Trim() : text.Trim();

        if (string.IsNullOrWhiteSpace(answer) || answer == "Thinking..." || answer.StartsWith("Waiting"))
            return;

        StopSpeaking();

        try
        {
            _sayProcess = Process.Start(new ProcessStartInfo
            {
                FileName = "say",
                Arguments = $"\"{answer.Replace("\"", "\\\"")}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            });
        }
        catch
        {
            // say command not available — ignore
        }
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
