using MoergoLayerViz.App.Services.Hotkeys;

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

    /// <summary>
    /// F-keys offered in the hotkey picker, in display order (F13–F24 first
    /// since they're effectively always free, then F1–F12). Filtered per
    /// platform to drop keys we either can't register or that another
    /// process / the OS already claims as a bare F-key.
    ///
    /// macOS: filter is built from <c>CopySymbolicHotKeys</c>, which returns
    /// the OS's live symbolic hotkey table (defaults merged with user
    /// overrides). F21–F24 are also dropped — no documented Carbon VK.
    ///
    /// Windows: filter is built by probing <c>RegisterHotKey</c> for each
    /// F-key with no modifier — any key another process has claimed returns
    /// ERROR_HOTKEY_ALREADY_REGISTERED. <paramref name="currentlyOwnedKey"/>
    /// is the key our own <c>GlobalHotkeyService</c> currently holds; we
    /// pass it through so the picker doesn't strike it out.
    ///
    /// Linux: no filtering (the registry stub fails cleanly regardless, and
    /// the hotkey UI is hidden when global hotkeys aren't supported).
    /// </summary>
    public static IReadOnlyList<string> GetAvailableFKeys(string? currentlyOwnedKey = null)
    {
        var high = Enumerable.Range(13, 12); // F13..F24
        var low = Enumerable.Range(1, 12);   // F1..F12
        if (OperatingSystem.IsMacOS())
        {
            var claimed = MacOsSymbolicHotKeys.GetClaimedFKeyIndices();
            return high.Concat(low)
                .Where(n => MacOsFKeyMap.GetCarbonVk(n) is not null && !claimed.Contains(n))
                .Select(n => $"F{n}")
                .ToArray();
        }
        if (OperatingSystem.IsWindows())
        {
            var claimed = WindowsFreeFKeys.GetClaimedFKeyIndices(currentlyOwnedKey);
            return high.Concat(low)
                .Where(n => !claimed.Contains(n))
                .Select(n => $"F{n}")
                .ToArray();
        }
        return high.Concat(low).Select(n => $"F{n}").ToArray();
    }
}
