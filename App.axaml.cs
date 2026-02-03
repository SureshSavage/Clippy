using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace Clippy;

public class App : Application
{
    private TrayIcon? _trayIcon;
    private MainWindow? _mainWindow;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _mainWindow = new MainWindow();
            _mainWindow.HideToTrayRequested += OnHideToTray;
            desktop.MainWindow = _mainWindow;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            SetupTrayIcon();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTrayIcon()
    {
        var showItem = new NativeMenuItem("Show Clippy");
        showItem.Click += (_, _) => ShowWindow();

        var exitItem = new NativeMenuItem("Exit");
        exitItem.Click += (_, _) => ExitApp();

        var menu = new NativeMenu();
        menu.Items.Add(showItem);
        menu.Items.Add(new NativeMenuItemSeparator());
        menu.Items.Add(exitItem);

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "icon.png");

        _trayIcon = new TrayIcon
        {
            ToolTipText = "Clippy",
            Icon = new WindowIcon(iconPath),
            Menu = menu,
            IsVisible = false
        };

        _trayIcon.Clicked += (_, _) => ShowWindow();
    }

    private void OnHideToTray()
    {
        _mainWindow?.Hide();
        if (_trayIcon != null)
            _trayIcon.IsVisible = true;
    }

    private void ShowWindow()
    {
        if (_trayIcon != null)
            _trayIcon.IsVisible = false;

        _mainWindow?.Show();
        _mainWindow?.Activate();
    }

    private void ExitApp()
    {
        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = false;
            _trayIcon.Dispose();
        }

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.Shutdown();
    }
}
