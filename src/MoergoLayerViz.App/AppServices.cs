using Microsoft.Extensions.DependencyInjection;
using MoergoLayerViz.App.Localization;
using MoergoLayerViz.App.Services;
using MoergoLayerViz.App.Services.Hotkeys;
using MoergoLayerViz.App.Services.MouseIdle;
using MoergoLayerViz.App.ViewModels;
using MoergoLayerViz.Core.Diagnostics;
using MoergoLayerViz.Core.Settings;
using ZmkHidProtocol.ActiveWindow;

namespace MoergoLayerViz.App;

/// <summary>
/// Composition root for App-level services. Anything new gets registered
/// here as the canonical pattern; no inline <c>new</c> in <c>App.axaml.cs</c>.
///
/// <para>Phase B (interface extraction) is complete for
/// <see cref="IGlobalHotkeyService"/>, <see cref="ILayerPushCoordinator"/>,
/// <see cref="IMouseLayerEngine"/>, and <see cref="IKeyHighlightTracker"/>.
/// Those four are not registered in the container because they each need
/// state owned by <see cref="ViewModels.MainWindowViewModel"/> at
/// construction time (initial profile, <c>getRenderedLayer</c> closure, or
/// the live <c>Keys</c> collection). They remain MainVM-scoped, but the
/// interface extraction means anything that <i>consumes</i> them
/// (SettingsViewModel, tests, future split-out engines) can hold the
/// abstraction.</para>
///
/// <para>Phase D registers <see cref="MainWindowViewModel"/> as a
/// singleton and <see cref="SettingsViewModel"/> as transient — App.axaml
/// resolves both through the container instead of new'ing them inline.</para>
///
/// <para>Disposal: the provider owns the singletons it constructs.
/// App.axaml disposes the provider on <c>desktop.Exit</c>; individual
/// per-service Exit handlers are no longer required.</para>
/// </summary>
internal static class AppServices
{
    public static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<INativeHotkeyRegistry>(_ => NativeHotkeyRegistryFactory.Create());
        services.AddSingleton<IMouseIdleMonitor>(_ => MouseIdleMonitorFactory.Create());
        services.AddSingleton<IGlobalHotkeyService, GlobalHotkeyService>();

        // Active-window monitor can fail to construct on some platforms
        // (missing permissions, headless test runners, …). We register it
        // only when construction succeeds; consumers resolve via
        // GetService (nullable) so the absence path stays exercised.
        IActiveWindowMonitor? activeWindowMonitor = null;
        try
        {
            activeWindowMonitor = ActiveWindowMonitorFactory.Create();
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn("Startup", $"ActiveWindowMonitor unavailable: {ex.Message}");
        }
        if (activeWindowMonitor is not null)
            services.AddSingleton(activeWindowMonitor);

        // Both registered against the same instance the static facade
        // delegates to, so DI consumers and the existing XAML / converter
        // call sites see identical state.
        services.AddSingleton<ILocalization>(Loc.Instance);
        services.AddSingleton<ILayerColorService>(LayerColorPalette.Service);

        services.AddSingleton<MainWindowViewModel>(sp =>
            new MainWindowViewModel(
                sp.GetRequiredService<ISettingsService>(),
                sp.GetService<IActiveWindowMonitor>(),
                sp.GetService<IMouseIdleMonitor>()));

        services.AddTransient<SettingsViewModel>(sp =>
            new SettingsViewModel(
                sp.GetRequiredService<ISettingsService>(),
                sp.GetRequiredService<MainWindowViewModel>()));

        return services.BuildServiceProvider();
    }
}
