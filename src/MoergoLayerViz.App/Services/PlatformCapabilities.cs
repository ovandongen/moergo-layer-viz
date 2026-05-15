namespace MoergoLayerViz.App.Services;

/// <summary>
/// Static OS capability flags. Centralized so the UI layer doesn't sprout
/// scattered <c>OperatingSystem.IsX</c> checks — anything that toggles UI
/// affordances based on platform should query this class.
/// </summary>
public static class PlatformCapabilities
{
    /// <summary>
    /// False on Linux — global hotkey registration requires per-WM
    /// cooperation (Wayland portals, X11 grabs) that we don't ship today.
    /// macOS uses Carbon RegisterEventHotKey, Windows uses User32
    /// RegisterHotKey.
    /// </summary>
    public static bool IsGlobalHotkeySupported { get; } = !OperatingSystem.IsLinux();
}
