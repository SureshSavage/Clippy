using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Clippy;

public partial class AnswerOverlayWindow : Window
{
    public AnswerOverlayWindow()
    {
        InitializeComponent();
        PointerPressed += OnPointerPressed;
        ResizeGrip.PointerPressed += OnResizeGripPressed;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
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
            BeginResizeDrag(WindowEdge.SouthEast, e);
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
