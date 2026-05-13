namespace MoergoLayerViz.App.Services;

/// <summary>
/// Static OS capability flags. Centralized so the UI layer doesn't sprout
/// scattered <c>OperatingSystem.IsX</c> checks — anything that toggles UI
/// affordances based on platform should query this class.
/// </summary>
public static class PlatformCapabilities
{
    /// <summary>
    /// False on Linux — Wayland blocks process-global key hooks from
    /// unfocused windows, so the show/hide hotkey isn't wired and its UI
    /// is hidden. macOS and Windows host SharpHook + libuiohook fine.
    /// </summary>
    public static bool IsGlobalHotkeySupported { get; } = !OperatingSystem.IsLinux();
}
