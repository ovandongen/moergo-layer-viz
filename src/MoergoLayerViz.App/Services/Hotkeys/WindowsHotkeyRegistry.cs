using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Avalonia.Threading;
using MoergoLayerViz.Core.Diagnostics;

namespace MoergoLayerViz.App.Services.Hotkeys;

/// <summary>
/// Windows implementation using User32 <c>RegisterHotKey</c>. A dedicated
/// background thread owns a message-only window and pumps <c>GetMessage</c>;
/// <c>WM_HOTKEY</c> arrives there and we marshal the callback to the
/// Avalonia UI thread. Press-only — Win32 doesn't deliver release events.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal sealed class WindowsHotkeyRegistry : INativeHotkeyRegistry
{
    private const int WmHotkey = 0x0312;
    private const int WmAppRegister = 0x8000 + 1;
    private const int WmAppUnregister = 0x8000 + 2;
    private const int WmAppShutdown = 0x8000 + 3;

    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWin = 0x0008;
    private const uint ModNoRepeat = 0x4000;

    private readonly ConcurrentDictionary<int, Action> _byToken = new();
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _ready = new(false);
    private uint _threadId;
    private int _nextToken = 1;
    private bool _disposed;

    public WindowsHotkeyRegistry()
    {
        _thread = new Thread(MessageLoop) { IsBackground = true, Name = "HotkeyRegistry" };
        _thread.Start();
        _ready.Wait();
    }

    public HotkeyRegistration TryRegister(string keyName, string modifiersName, Action onPress)
    {
        if (_disposed) return HotkeyRegistration.Failed("registry disposed");

        var fIndex = HotkeyNameMap.TryParseFKeyIndex(keyName);
        if (fIndex is null) return HotkeyRegistration.Failed($"unsupported key '{keyName}'");
        // VK_F1 = 0x70, F2 = 0x71, ... F24 = 0x87.
        var vk = (uint)(0x70 + (fIndex.Value - 1));

        uint mods = ModNoRepeat;
        var modsOk = HotkeyNameMap.TryParseModifiers(modifiersName, m =>
        {
            mods |= m switch
            {
                "Ctrl" => ModControl,
                "Alt" => ModAlt,
                "Shift" => ModShift,
                "Meta" => ModWin,
                _ => 0u,
            };
        });
        if (!modsOk) return HotkeyRegistration.Failed($"unsupported modifier '{modifiersName}'");

        var token = Interlocked.Increment(ref _nextToken);
        var request = new RegisterRequest(token, vk, mods, onPress);
        var handle = GCHandle.Alloc(request);
        try
        {
            if (!PostThreadMessage(_threadId, WmAppRegister, IntPtr.Zero, GCHandle.ToIntPtr(handle)))
            {
                handle.Free();
                return HotkeyRegistration.Failed("PostThreadMessage failed");
            }
            request.Done.Wait();
            return request.Result;
        }
        catch
        {
            if (handle.IsAllocated) handle.Free();
            throw;
        }
    }

    public void Unregister(int token)
    {
        if (_disposed) return;
        _byToken.TryRemove(token, out _);
        PostThreadMessage(_threadId, WmAppUnregister, (IntPtr)token, IntPtr.Zero);
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
        PostThreadMessage(_threadId, WmAppShutdown, IntPtr.Zero, IntPtr.Zero);
        _thread.Join(2000);
    }

    private sealed class RegisterRequest
    {
        public readonly int Token;
        public readonly uint Vk;
        public readonly uint Mods;
        public readonly Action OnPress;
        public readonly ManualResetEventSlim Done = new(false);
        public HotkeyRegistration Result;

        public RegisterRequest(int token, uint vk, uint mods, Action onPress)
        {
            Token = token; Vk = vk; Mods = mods; OnPress = onPress;
        }
    }

    private void MessageLoop()
    {
        _threadId = GetCurrentThreadId();
        // Force the thread's message queue to be created before we signal ready.
        PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
        _ready.Set();

        while (true)
        {
            if (GetMessage(out var msg, IntPtr.Zero, 0, 0) <= 0) break;

            switch (msg.message)
            {
                case WmHotkey:
                {
                    var token = (int)msg.wParam;
                    if (_byToken.TryGetValue(token, out var callback))
                        Dispatcher.UIThread.Post(callback);
                    break;
                }
                case WmAppRegister:
                {
                    var handle = GCHandle.FromIntPtr(msg.lParam);
                    var req = (RegisterRequest)handle.Target!;
                    var ok = RegisterHotKey(IntPtr.Zero, req.Token, req.Mods, req.Vk);
                    if (ok)
                    {
                        _byToken[req.Token] = req.OnPress;
                        req.Result = HotkeyRegistration.Ok(req.Token);
                    }
                    else
                    {
                        var err = Marshal.GetLastWin32Error();
                        req.Result = HotkeyRegistration.Failed($"RegisterHotKey failed (Win32 error {err})");
                    }
                    handle.Free();
                    req.Done.Set();
                    break;
                }
                case WmAppUnregister:
                {
                    UnregisterHotKey(IntPtr.Zero, (int)msg.wParam);
                    break;
                }
                case WmAppShutdown:
                    return;
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public int message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool PostThreadMessage(uint idThread, int Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
