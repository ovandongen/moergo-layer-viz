namespace MoergoLayerViz.App.Services.Hotkeys;

public static class NativeHotkeyRegistryFactory
{
    /// <summary>Picks the appropriate registry for the current OS. Linux returns a stub that always fails.</summary>
    public static INativeHotkeyRegistry Create()
    {
        if (OperatingSystem.IsMacOS()) return new MacOsHotkeyRegistry();
        if (OperatingSystem.IsWindows()) return new WindowsHotkeyRegistry();
        return new LinuxHotkeyRegistry();
    }
}
