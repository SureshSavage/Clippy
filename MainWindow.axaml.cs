using System.Diagnostics;
using System.IO;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Clippy;

public partial class MainWindow : Window
{
    public event Action? HideToTrayRequested;

    private static readonly string ScreenshotDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "Clippy_Screenshots");

    public MainWindow()
    {
        InitializeComponent();
    }

    private void OnHideClicked(object? sender, RoutedEventArgs e)
    {
        HideToTrayRequested?.Invoke();
    }

    private async void OnClipItClicked(object? sender, RoutedEventArgs e)
    {
        // Hide the window so it doesn't appear in the screenshot
        Hide();

        // Brief delay to let the window fully disappear
        await Task.Delay(300);

        Directory.CreateDirectory(ScreenshotDir);
        var fileName = $"clip_{DateTime.Now:yyyyMMdd_HHmmss}.png";
        var filePath = Path.Combine(ScreenshotDir, fileName);

        // macOS screencapture: -x = no sound
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = "screencapture",
            Arguments = $"-x \"{filePath}\"",
            UseShellExecute = false,
            CreateNoWindow = true
        });

        if (process != null)
            await process.WaitForExitAsync();

        // Show the window again
        Show();
        Activate();
    }
}
