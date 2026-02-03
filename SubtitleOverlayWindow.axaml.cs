using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace Clippy;

public partial class SubtitleOverlayWindow : Window
{
    public SubtitleOverlayWindow()
    {
        InitializeComponent();
        PointerPressed += OnPointerPressed;
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            BeginMoveDrag(e);
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
