using MoergoLayerViz.App.Services.Hotkeys;
using MoergoLayerViz.Core.Diagnostics;

namespace MoergoLayerViz.App.Services;

/// <summary>
/// Owns the single show/hide global hotkey. Sits on top of
/// <see cref="INativeHotkeyRegistry"/>, which uses native OS APIs (Carbon
/// <c>RegisterEventHotKey</c> on macOS, User32 <c>RegisterHotKey</c> on
/// Windows). These APIs require no Accessibility / Input-Monitoring
/// permission on macOS.
/// </summary>
public interface IGlobalHotkeyService : IDisposable
{
    /// <summary>Fired on the Avalonia UI thread when the hotkey is pressed.</summary>
    Action? HotkeyPressed { get; set; }

    /// <summary>Sets the key/modifier to register. Call <see cref="Start"/> to register, or call after Start to live-rebind.</summary>
    void UpdateHotkey(string keyName, string modifiersName);

    /// <summary>Registers the configured hotkey. Idempotent.</summary>
    void Start();

    /// <summary>Releases the registration. Idempotent.</summary>
    void Stop();
}

public sealed class GlobalHotkeyService : IGlobalHotkeyService
{
    private readonly INativeHotkeyRegistry _registry;
    private int _token;
    private string _keyName = "F12";
    private string _modifiersName = "None";

    public GlobalHotkeyService(INativeHotkeyRegistry registry)
    {
        _registry = registry;
    }

    public Action? HotkeyPressed { get; set; }

    public void UpdateHotkey(string keyName, string modifiersName)
    {
        _keyName = keyName;
        _modifiersName = modifiersName;
        if (_token != 0)
        {
            _registry.Unregister(_token);
            _token = 0;
            Register();
        }
    }

    public void Start()
    {
        if (_token != 0) return;
        Register();
    }

    public void Stop()
    {
        if (_token == 0) return;
        _registry.Unregister(_token);
        _token = 0;
    }

    private void Register()
    {
        var result = _registry.TryRegister(_keyName, _modifiersName, () => HotkeyPressed?.Invoke());
        if (result.Success)
        {
            _token = result.Token;
            DiagnosticLog.Info("Hotkey", $"registered show/hide hotkey '{_keyName}' modifiers='{_modifiersName}'");
        }
        else
        {
            DiagnosticLog.Warn("Hotkey", $"failed to register '{_keyName}': {result.ErrorMessage}");
        }
    }

    public void Dispose() => Stop();
}
