namespace MoergoLayerViz.App.Services.MouseIdle;

public static class MouseIdleMonitorFactory
{
    /// <summary>Picks the appropriate monitor for the current OS.</summary>
    public static IMouseIdleMonitor Create()
    {
        if (OperatingSystem.IsMacOS()) return new MacOsMouseIdleMonitor();
        if (OperatingSystem.IsWindows()) return new WindowsMouseIdleMonitor();
        if (OperatingSystem.IsLinux()) return new LinuxMouseIdleMonitor();
        throw new PlatformNotSupportedException("No mouse idle monitor for this platform.");
    }
}
