namespace MoergoLayerViz.App.Services.MouseIdle;

public static class MouseIdleMonitorFactory
{
    /// <summary>Picks the appropriate monitor for the current OS. Linux returns a no-op stub.</summary>
    public static IMouseIdleMonitor Create()
    {
        if (OperatingSystem.IsMacOS()) return new MacOsMouseIdleMonitor();
        if (OperatingSystem.IsWindows()) return new WindowsMouseIdleMonitor();
        return new LinuxMouseIdleMonitor();
    }
}
