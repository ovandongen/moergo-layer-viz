using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using MoergoLayerViz.Core.Diagnostics;

namespace MoergoLayerViz.App.Services.Hotkeys;

/// <summary>
/// macOS implementation using Carbon's <c>RegisterEventHotKey</c>. Unlike
/// <c>CGEventTap</c> + <c>NSEvent.addGlobalMonitor</c>, this API does NOT
/// trigger an Accessibility / Input-Monitoring prompt — the hotkey is
/// dispatched by the OS's hotkey manager when the focused app doesn't
/// consume it. One <c>InstallEventHandler</c> covers every registration; we
/// dispatch by <c>EventHotKeyID.id</c>.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("macos")]
internal sealed class MacOsHotkeyRegistry : INativeHotkeyRegistry
{
    // Four-cc 'MLVZ' for our hotkey signatures; arbitrary but must be stable.
    private const uint Signature = ('M' << 24) | ('L' << 16) | ('V' << 8) | 'Z';
    private const uint EventClassKeyboard = ('k' << 24) | ('e' << 16) | ('y' << 8) | 'b';
    private const uint EventHotKeyPressed = 5;

    // Carbon modifier flags (from Events.h).
    private const uint CmdKey = 1 << 8;
    private const uint ShiftKey = 1 << 9;
    private const uint OptionKey = 1 << 11;
    private const uint ControlKey = 1 << 12;

    private readonly object _gate = new();
    private readonly ConcurrentDictionary<int, Entry> _byToken = new();
    private readonly EventHandlerUPP _handlerDelegate; // keep delegate alive for the lifetime of the handler
    private IntPtr _handlerRef;
    private int _nextToken = 1;
    private bool _disposed;

    private sealed class Entry
    {
        public IntPtr HotKeyRef;
        public Action OnPress = () => { };
    }

    public MacOsHotkeyRegistry()
    {
        _handlerDelegate = HandleEvent;
        InstallEventHandler();
    }

    private void InstallEventHandler()
    {
        var spec = new EventTypeSpec { eventClass = EventClassKeyboard, eventKind = EventHotKeyPressed };
        var target = GetApplicationEventTarget();
        var status = InstallEventHandler(target, _handlerDelegate, 1, ref spec, IntPtr.Zero, out _handlerRef);
        if (status != 0)
            DiagnosticLog.Warn("HotkeyMac", $"InstallEventHandler failed (status {status})");
    }

    public HotkeyRegistration TryRegister(string keyName, string modifiersName, Action onPress)
    {
        if (_disposed) return HotkeyRegistration.Failed("registry disposed");

        var fIndex = HotkeyNameMap.TryParseFKeyIndex(keyName);
        if (fIndex is null) return HotkeyRegistration.Failed($"unsupported key '{keyName}'");
        var vk = MapFKeyToCarbonVk(fIndex.Value);
        if (vk is null) return HotkeyRegistration.Failed($"no Carbon VK for '{keyName}'");

        uint modMask = 0;
        var modsOk = HotkeyNameMap.TryParseModifiers(modifiersName, m =>
        {
            modMask |= m switch
            {
                "Ctrl" => ControlKey,
                "Alt" => OptionKey,
                "Shift" => ShiftKey,
                "Meta" => CmdKey,
                _ => 0u,
            };
        });
        if (!modsOk) return HotkeyRegistration.Failed($"unsupported modifier '{modifiersName}'");

        lock (_gate)
        {
            var token = _nextToken++;
            var id = new EventHotKeyID { signature = Signature, id = (uint)token };
            var status = RegisterEventHotKey((uint)vk.Value, modMask, id, GetApplicationEventTarget(), 0, out var hotKeyRef);
            if (status != 0 || hotKeyRef == IntPtr.Zero)
                return HotkeyRegistration.Failed($"RegisterEventHotKey failed (status {status})");

            _byToken[token] = new Entry { HotKeyRef = hotKeyRef, OnPress = onPress };
            return HotkeyRegistration.Ok(token);
        }
    }

    public void Unregister(int token)
    {
        if (!_byToken.TryRemove(token, out var entry)) return;
        if (entry.HotKeyRef != IntPtr.Zero) UnregisterEventHotKey(entry.HotKeyRef);
    }

    public void UnregisterAll()
    {
        foreach (var token in _byToken.Keys.ToArray()) Unregister(token);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterAll();
        if (_handlerRef != IntPtr.Zero)
        {
            RemoveEventHandler(_handlerRef);
            _handlerRef = IntPtr.Zero;
        }
    }

    private int HandleEvent(IntPtr nextHandler, IntPtr theEvent, IntPtr userData)
    {
        try
        {
            var id = default(EventHotKeyID);
            var size = (uint)Marshal.SizeOf<EventHotKeyID>();
            var status = GetEventParameter(theEvent, ParamHotKey, TypeHotKey,
                IntPtr.Zero, size, IntPtr.Zero, ref id);
            if (status != 0) return 0;
            if (id.signature != Signature) return 0;

            if (_byToken.TryGetValue((int)id.id, out var entry))
            {
                var callback = entry.OnPress;
                Dispatcher.UIThread.Post(callback);
            }
        }
        catch (Exception ex)
        {
            DiagnosticLog.Warn("HotkeyMac", $"event handler threw: {ex.Message}");
        }
        return 0;
    }

    private static int? MapFKeyToCarbonVk(int fIndex) => fIndex switch
    {
        1 => 0x7A,
        2 => 0x78,
        3 => 0x63,
        4 => 0x76,
        5 => 0x60,
        6 => 0x61,
        7 => 0x62,
        8 => 0x64,
        9 => 0x65,
        10 => 0x6D,
        11 => 0x67,
        12 => 0x6F,
        13 => 0x69,
        14 => 0x6B,
        15 => 0x71,
        16 => 0x6A,
        17 => 0x40,
        18 => 0x4F,
        19 => 0x50,
        20 => 0x5A,
        // F21–F24 have no documented Carbon VKs; macOS reserves them for OEM use.
        _ => null,
    };

    // 'hkey' four-cc + UInt32 type.
    private static readonly uint ParamHotKey = ('h' << 24) | ('k' << 16) | ('e' << 8) | 'y';
    private static readonly uint TypeHotKey = ('h' << 24) | ('k' << 16) | ('e' << 8) | 'y';

    [StructLayout(LayoutKind.Sequential)]
    private struct EventHotKeyID
    {
        public uint signature;
        public uint id;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct EventTypeSpec
    {
        public uint eventClass;
        public uint eventKind;
    }

    private delegate int EventHandlerUPP(IntPtr nextHandler, IntPtr theEvent, IntPtr userData);

    private const string Carbon = "/System/Library/Frameworks/Carbon.framework/Carbon";

    [DllImport(Carbon)]
    private static extern IntPtr GetApplicationEventTarget();

    [DllImport(Carbon)]
    private static extern int InstallEventHandler(
        IntPtr target,
        EventHandlerUPP handler,
        uint numTypes,
        ref EventTypeSpec types,
        IntPtr userData,
        out IntPtr handlerRef);

    [DllImport(Carbon)]
    private static extern int RemoveEventHandler(IntPtr handlerRef);

    [DllImport(Carbon)]
    private static extern int RegisterEventHotKey(
        uint inHotKeyCode,
        uint inHotKeyModifiers,
        EventHotKeyID inHotKeyID,
        IntPtr inTarget,
        uint inOptions,
        out IntPtr outRef);

    [DllImport(Carbon)]
    private static extern int UnregisterEventHotKey(IntPtr inHotKeyRef);

    [DllImport(Carbon)]
    private static extern int GetEventParameter(
        IntPtr inEvent,
        uint inName,
        uint inDesiredType,
        IntPtr outActualType,
        uint inBufferSize,
        IntPtr outActualSize,
        ref EventHotKeyID outData);
}
