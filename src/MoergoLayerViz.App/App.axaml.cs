using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MoergoLayerViz.App.Localization;
using MoergoLayerViz.App.Services;
using MoergoLayerViz.App.Services.Hotkeys;
using MoergoLayerViz.App.Services.MouseIdle;
using MoergoLayerViz.App.ViewModels;
using MoergoLayerViz.App.Views;
using MoergoLayerViz.Core.Diagnostics;
using MoergoLayerViz.Core.Settings;
using Projektanker.Icons.Avalonia;
using Projektanker.Icons.Avalonia.FontAwesome;
using ZmkHidProtocol.ActiveWindow;

namespace MoergoLayerViz.App;

public partial class App : Application
{
    private GlobalHotkeyService? _hotkeyService;

    public override void Initialize()
    {
        // Register Font Awesome icon provider before XAML is loaded so any
        // <i:Icon> controls inside templates resolve fa-* identifiers.
        IconProvider.Current.Register<FontAwesomeIconProvider>();

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                DiagnosticLog.Error("Unhandled", $"AppDomain unhandled: {e.ExceptionObject}");
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                DiagnosticLog.Error("Unhandled", $"Unobserved task: {e.Exception}");
                e.SetObserved();
            };
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                DiagnosticLog.Error("Unhandled", $"UI dispatcher: {e.Exception}");
                e.Handled = true;
            };

            var settingsService = new SettingsService();
            var initialSettings = settingsService.Load();
            Loc.Instance.SetCulture(initialSettings.Language);

            var envLevel = Environment.GetEnvironmentVariable("MOERGO_LOG_LEVEL");
            if (!string.IsNullOrEmpty(envLevel) && Enum.TryParse<LogLevel>(envLevel, true, out var envLogLevel))
                DiagnosticLog.SetMinimumLevel(envLogLevel);
            else if (Enum.TryParse<LogLevel>(initialSettings.LogLevel, true, out var logLevel))
                DiagnosticLog.SetMinimumLevel(logLevel);

            // Native global-hotkey registry (Carbon on macOS, User32 on
            // Windows, no-op on Linux).
            var hotkeyRegistry = NativeHotkeyRegistryFactory.Create();
            desktop.Exit += (_, _) => hotkeyRegistry.Dispose();

            // Construct only — MainWindowViewModel calls Start/Stop on demand
            // based on the master toggle and rule-list state. App owns disposal.
            IActiveWindowMonitor? activeWindowMonitor = null;
            try
            {
                activeWindowMonitor = ActiveWindowMonitorFactory.Create();
                desktop.Exit += (_, _) => activeWindowMonitor.Dispose();
            }
            catch (Exception ex)
            {
                DiagnosticLog.Warn("Startup", $"ActiveWindowMonitor unavailable: {ex.Message}");
            }

            // Global mouse-idle monitor (NSEvent / CGEventGetLocation polling
            // on macOS, WH_MOUSE_LL on Windows, no-op stub on Linux). The
            // engine starts/stops it on demand based on the master toggle +
            // HID-connected state. App owns disposal.
            IMouseIdleMonitor mouseIdleMonitor = MouseIdleMonitorFactory.Create();
            desktop.Exit += (_, _) => mouseIdleMonitor.Dispose();

            DiagnosticLog.Info("Startup", "Creating MainWindowViewModel...");
            var viewModel = new MainWindowViewModel(settingsService, activeWindowMonitor, mouseIdleMonitor);
            var mainWindow = new MainWindow { DataContext = viewModel };
            desktop.MainWindow = mainWindow;

            // Restore saved window position/size (or center on first launch)
            if (initialSettings.WindowX.HasValue && initialSettings.WindowY.HasValue)
            {
                mainWindow.WindowStartupLocation = WindowStartupLocation.Manual;
                mainWindow.Position = new PixelPoint((int)initialSettings.WindowX.Value, (int)initialSettings.WindowY.Value);
            }
            else
            {
                mainWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            if (initialSettings.WindowWidth.HasValue)
                mainWindow.Width = initialSettings.WindowWidth.Value;
            if (initialSettings.WindowHeight.HasValue)
                mainWindow.Height = initialSettings.WindowHeight.Value;

            DiagnosticLog.Info("Startup", $"Window position: {mainWindow.Position.X},{mainWindow.Position.Y} size: {mainWindow.Width}x{mainWindow.Height}");

            // Validate restored position is on a visible screen
            mainWindow.Opened += (_, _) =>
            {
                if (mainWindow.Screens.ScreenFromWindow(mainWindow) is null)
                    mainWindow.Position = new PixelPoint(0, 0);
            };

            void SaveWindowState()
            {
                if (mainWindow.WindowState != WindowState.Minimized)
                {
                    var s = settingsService.Load();
                    settingsService.Save(s with
                    {
                        WindowX = mainWindow.Position.X,
                        WindowY = mainWindow.Position.Y,
                        WindowWidth = mainWindow.Width,
                        WindowHeight = mainWindow.Height,
                    });
                }
            }

            var closingHandled = false;
            mainWindow.Closing += (sender, e) =>
            {
                if (closingHandled) return;
                closingHandled = true;
                e.Cancel = true;
                SaveWindowState();
                viewModel.Shutdown();
                ((Window)sender!).Close();
            };

            viewModel.QuitRequested = () =>
            {
                SaveWindowState();
                viewModel.Shutdown();
                Environment.Exit(0);
            };

            // Tray icon: localize menu headers + pipe clicks to the VM
            var trayIcons = TrayIcon.GetIcons(this);
            if (trayIcons?.Count > 0)
            {
                var trayIcon = trayIcons[0];
                trayIcon.Icon = new WindowIcon(
                    AssetLoader.Open(new Uri("avares://MoergoLayerViz.App/Assets/icon.png")));
                LocalizeTrayMenu(trayIcon);
                // Named delegate so we can detach on Exit. Without the -=, every
                // runtime culture switch leaks the prior handler's tray-icon
                // capture and re-localizes already-collected closures.
                Action onCultureChanged = () =>
                    Dispatcher.UIThread.Post(() => LocalizeTrayMenu(trayIcon));
                Loc.CultureChanged += onCultureChanged;
                desktop.Exit += (_, _) => Loc.CultureChanged -= onCultureChanged;
            }

            viewModel.ShowWindowRequested = () =>
            {
                mainWindow.Show();
                mainWindow.Activate();
                if (mainWindow.WindowState == WindowState.Minimized)
                    mainWindow.WindowState = WindowState.Normal;
            };

            viewModel.ToggleWindowRequested = () =>
            {
                if (mainWindow.IsVisible)
                {
                    mainWindow.Hide();
                }
                else
                {
                    mainWindow.Show();
                    mainWindow.Activate();
                    if (mainWindow.WindowState == WindowState.Minimized)
                        mainWindow.WindowState = WindowState.Normal;
                }
            };

            viewModel.LoadLayoutRequested = async () =>
            {
                var file = await mainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = Loc.Instance["LoadDialog_Title"],
                    AllowMultiple = false,
                    FileTypeFilter =
                    [
                        new FilePickerFileType(Loc.Instance["LoadDialog_FileType"])
                        {
                            Patterns = ["*.json"]
                        }
                    ],
                });
                if (file.Count > 0)
                    viewModel.LoadLayoutFromPath(file[0].Path.LocalPath);
            };

            // Single non-modal Settings window. Re-clicking the toolbar button
            // brings the existing window to front rather than spawning a new one.
            SettingsWindow? settingsWindow = null;
            viewModel.OpenSettingsRequested = () =>
            {
                if (settingsWindow is { } existing && existing.IsVisible)
                {
                    existing.Activate();
                    return;
                }
                var settingsVm = new SettingsViewModel(settingsService, viewModel);
                settingsWindow = new SettingsWindow
                {
                    DataContext = settingsVm,
                    Topmost = mainWindow.Topmost,
                };
                // Keep the settings window above the main window when the
                // user has the main window pinned, otherwise it opens behind
                // and looks lost.
                void SyncTopmost(object? _, System.ComponentModel.PropertyChangedEventArgs e)
                {
                    if (e.PropertyName == nameof(MainWindowViewModel.IsAlwaysOnTop) && settingsWindow is { } w)
                        w.Topmost = viewModel.IsAlwaysOnTop;
                }
                viewModel.PropertyChanged += SyncTopmost;
                settingsWindow.Closed += (_, _) =>
                {
                    viewModel.PropertyChanged -= SyncTopmost;
                    settingsVm.Dispose();
                    settingsWindow = null;
                };
                settingsWindow.Show(mainWindow);
            };

            viewModel.CopyDiagnosticsRequested = async () =>
            {
                try
                {
                    var report = DiagnosticLog.CollectDiagnosticReport(viewModel.BuildDiagnosticsSnapshot());
                    var clipboard = mainWindow.Clipboard;
                    if (clipboard is not null)
                    {
                        await clipboard.SetTextAsync(report);
                        viewModel.StatusMessage = Loc.Instance["Status_DiagnosticsCopied"];
                    }
                }
                catch (Exception ex)
                {
                    viewModel.StatusMessage = $"Could not copy diagnostics: {ex.Message}";
                }
            };

            // Global show/hide hotkey. The registry's Linux stub returns
            // failure on TryRegister, so the service no-ops on Linux without
            // a special case here.
            _hotkeyService = new GlobalHotkeyService(hotkeyRegistry);
            _hotkeyService.HotkeyPressed = () => viewModel.ToggleWindowRequested?.Invoke();
            _hotkeyService.UpdateHotkey(initialSettings.HotkeyKey, initialSettings.HotkeyModifiers);
            _hotkeyService.Start();
            viewModel.HotkeyKeyChanged += newKey =>
                _hotkeyService.UpdateHotkey(newKey, viewModel.HotkeyModifiers);
            desktop.Exit += (_, _) => _hotkeyService.Dispose();

            // Restore the last-loaded layout (or show a "pick a file" prompt).
            Dispatcher.UIThread.Post(() => viewModel.InitializeAsync(), DispatcherPriority.Background);

            DataContext = viewModel;
            DiagnosticLog.Info("Startup", "OnFrameworkInitializationCompleted done");
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static readonly (string Key, string ResKey)[] TrayMenuKeys =
    [
        ("Show", "Tray_ShowLayers"),
        ("Quit", "Tray_Quit"),
    ];

    private static void LocalizeTrayMenu(TrayIcon trayIcon)
    {
        trayIcon.ToolTipText = Loc.Instance["Tray_Tooltip"];
        if (trayIcon.Menu is not { } menu) return;

        var menuItems = menu.Items.OfType<NativeMenuItem>().ToList();
        for (var i = 0; i < menuItems.Count && i < TrayMenuKeys.Length; i++)
            menuItems[i].Header = Loc.Instance[TrayMenuKeys[i].ResKey];
    }
}
