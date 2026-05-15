namespace MoergoLayerViz.App.Services.Hotkeys;

/// <summary>
/// Cross-platform global-hotkey registration. Implementations use the
/// platform's native hotkey API (Carbon <c>RegisterEventHotKey</c> on macOS,
/// User32 <c>RegisterHotKey</c> on Windows, no-op on Linux). Unlike a
/// keyboard hook these APIs require no Accessibility / Input-Monitoring
/// permission on macOS and no `WH_KEYBOARD_LL` hook on Windows.
/// </summary>
public interface INativeHotkeyRegistry : IDisposable
{
    /// <summary>
    /// Registers a hotkey. <paramref name="keyName"/> is a neutral name like
    /// "F12", "F18". <paramref name="modifiersName"/> is a "+"-joined list of
    /// "Ctrl", "Alt", "Shift", "Meta" (or "None" / empty for no modifier).
    /// Returns an opaque token for <see cref="Unregister"/>, or
    /// <see cref="HotkeyRegistration.Failed"/> when registration could not
    /// succeed (key already owned, unsupported name, platform stub).
    /// </summary>
    HotkeyRegistration TryRegister(string keyName, string modifiersName, Action onPress);

    /// <summary>Releases a previously-registered hotkey. No-op if the token is unknown or already released.</summary>
    void Unregister(int token);

    /// <summary>Releases every registration. Called on shutdown.</summary>
    void UnregisterAll();
}

/// <summary>
/// Result of <see cref="INativeHotkeyRegistry.TryRegister"/>. On success
/// holds an opaque token (positive). On failure holds 0 and a human-readable
/// <see cref="ErrorMessage"/> for the Settings UI.
/// </summary>
public readonly record struct HotkeyRegistration(int Token, string? ErrorMessage)
{
    public bool Success => Token > 0;

    public static HotkeyRegistration Ok(int token) => new(token, null);
    public static HotkeyRegistration Failed(string message) => new(0, message);
}
