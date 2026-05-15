namespace MoergoLayerViz.App.Services.Hotkeys;

/// <summary>
/// Linux stub: global hotkey registration requires per-WM cooperation
/// (Wayland portals, X11 grab) that isn't worth shipping today. Always
/// returns failure; the show/hide hotkey is documented as inert on Linux.
/// </summary>
internal sealed class LinuxHotkeyRegistry : INativeHotkeyRegistry
{
    public HotkeyRegistration TryRegister(string keyName, string modifiersName, Action onPress)
        => HotkeyRegistration.Failed("global hotkeys are not supported on Linux");

    public void Unregister(int token) { }
    public void UnregisterAll() { }
    public void Dispose() { }
}
