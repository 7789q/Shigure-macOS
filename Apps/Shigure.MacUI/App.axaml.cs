using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Shigure.MacApp;

namespace Shigure.MacUI;

public sealed partial class App : Application
{
    private const string ApplicationLeaseName = "Shigure.MacUI.Application";

    private MainWindow? _mainWindow;
    private SingleInstanceLease? _applicationLease;
    private SparkleUpdateController? _updateController;
    private string _updateUnavailableReason = "当前构建未包含应用更新组件。";
    private bool _quitting;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _applicationLease = SingleInstanceLease.TryAcquire(ApplicationLeaseName);
            if (_applicationLease is null)
            {
                desktop.Shutdown(2);
                return;
            }

            desktop.Exit += (_, _) =>
                Interlocked.Exchange(ref _applicationLease, null)?.Dispose();
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _mainWindow = new MainWindow();
            desktop.MainWindow = _mainWindow;
            _mainWindow.Show();
            _updateController = SparkleUpdateController.TryCreate(out _updateUnavailableReason);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void ShowMainWindow(object? sender, EventArgs e)
    {
        _mainWindow?.ShowAndActivate();
    }

    private void ShowOverlay(object? sender, EventArgs e)
    {
        _mainWindow?.ShowOverlay();
    }

    private async void CheckForUpdates(object? sender, EventArgs e)
    {
        if (_updateController is not null)
        {
            _updateController.CheckForUpdates();
            return;
        }

        if (_mainWindow is not null)
        {
            _mainWindow.ShowAndActivate();
            await _mainWindow.ShowMessageAsync("应用更新不可用", _updateUnavailableReason);
        }
    }

    private async void QuitApplication(object? sender, EventArgs e) =>
        await RequestQuitAsync();

    internal async Task RequestQuitAsync()
    {
        if (_quitting || ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
        {
            return;
        }

        _quitting = true;
        try
        {
            if (_mainWindow is not null)
            {
                if (!await _mainWindow.ConfirmShutdownAsync())
                {
                    _quitting = false;
                    return;
                }

                await _mainWindow.PrepareForShutdownAsync();
            }
        }
        catch
        {
            _quitting = false;
            throw;
        }

        _updateController?.Dispose();
        _updateController = null;
        desktop.Shutdown();
    }
}
