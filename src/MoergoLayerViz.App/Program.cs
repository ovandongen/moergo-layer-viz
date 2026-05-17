using System.Diagnostics;
using Avalonia;
using MoergoLayerViz.Core.Diagnostics;
using MoergoLayerViz.Core.Settings;

namespace MoergoLayerViz.App;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var mutex = new Mutex(true, "MoergoLayerViz-SingleInstance", out bool isNew);
        if (!isNew) return;

        DiagnosticLog.LogEnvironment();
        // ZmkHidProtocol's LibLog routes through System.Diagnostics.Trace.
        // Without a listener, every RawHid log line vanishes — install a
        // bridge that forwards them into DiagnosticLog so HID discovery /
        // connect / read activity is visible in diagnostic.log.
        Trace.Listeners.Add(new ZmkHidProtocolTraceListener());
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .LogToTrace();

        // Allow users to force software rendering via env var or settings.json.
        if (OperatingSystem.IsWindows())
        {
            var renderMode = Environment.GetEnvironmentVariable("MOERGO_RENDER_MODE");
            var source = "MOERGO_RENDER_MODE env";

            if (string.IsNullOrEmpty(renderMode))
            {
                try
                {
                    renderMode = new SettingsService().Load().RenderingMode;
                    source = "settings.json";
                }
                catch
                {
                    // Earliest-startup settings read; can't recover and a crash
                    // here would block launch, so fall back to auto-detect.
                    renderMode = "auto";
                    source = "default (settings read failed)";
                }
            }

            if (renderMode?.Equals("software", StringComparison.OrdinalIgnoreCase) == true)
            {
                DiagnosticLog.Info("Startup", $"Rendering mode: software (via {source})");
                builder = builder.With(new Win32PlatformOptions
                {
                    RenderingMode = [Win32RenderingMode.Software]
                });
            }
            else
            {
                DiagnosticLog.Info("Startup", $"Rendering mode: {renderMode ?? "auto"} (via {source})");
            }
        }

        return builder;
    }
}
