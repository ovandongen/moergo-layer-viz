using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using MoergoLayerViz.Core.Diagnostics;

namespace MoergoLayerViz.App.Services.MouseIdle;

/// <summary>
/// Windows implementation. Installs a <c>WH_MOUSE_LL</c> low-level mouse
/// hook so movement is observed even when this process is unfocused or
/// minimised. The hook runs on a dedicated background thread with its own
/// message pump — <c>SetWindowsHookEx(WH_MOUSE_LL)</c> requires that the
/// thread which installs the hook also pump a message loop, otherwise the
/// OS silently uninstalls it after a few seconds.
///
/// <c>WH_MOUSE_LL</c> does NOT require any special permission and does NOT
/// raise the UAC / antivirus warnings that <c>SetWinEventHook</c> can. The
/// hook callback just bumps the last-move timestamp; the base class's
/// state-machine tick handles Idle/Moving transitions.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsMouseIdleMonitor : MouseIdleMonitorBase
{
    private const int WhMouseLl = 14;
    private const int WmMouseMove = 0x0200;

    private Thread? _hookThread;
    private uint _hookThreadId;
    private ManualResetEventSlim? _ready;
    private IntPtr _hookHandle;
    // Keep the delegate alive for the lifetime of the hook so the GC doesn't
    // collect it while Win32 holds a function pointer to it.
    private HookProc? _hookProc;
    private volatile bool _shouldRun;

    protected override void StartPlatformMonitor()
    {
        _shouldRun = true;
        _ready = new ManualResetEventSlim(false);
        _hookThread = new Thread(HookThreadMain)
        {
            IsBackground = true,
            Name = "MouseIdleMonitor",
        };
        _hookThread.Start();
        _ready.Wait();
    }

    protected override void StopPlatformMonitor()
    {
        _shouldRun = false;
        if (_hookThreadId != 0)
            PostThreadMessage(_hookThreadId, 0x0012 /* WM_QUIT */, IntPtr.Zero, IntPtr.Zero);
        _hookThread?.Join(1000);
        _hookThread = null;
        _hookThreadId = 0;
        _hookProc = null;
        _ready?.Dispose();
        _ready = null;
    }

    private void HookThreadMain()
    {
        _hookThreadId = GetCurrentThreadId();
        _hookProc = LowLevelMouseProc;
        _hookHandle = SetWindowsHookEx(WhMouseLl, _hookProc, IntPtr.Zero, 0);
        if (_hookHandle == IntPtr.Zero)
        {
            DiagnosticLog.Warn("MouseIdle", $"SetWindowsHookEx failed (err {Marshal.GetLastWin32Error()})");
            _ready?.Set();
            return;
        }
        _ready?.Set();

        while (_shouldRun && GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
        {
            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }

        if (_hookHandle != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookHandle);
            _hookHandle = IntPtr.Zero;
        }
    }

    private IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == 0 && wParam.ToInt32() == WmMouseMove)
            NotifyMovement();
        return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
    }

    private delegate IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct Msg
    {
        public IntPtr hwnd;
        public uint message;
        public IntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public int pt_x;
        public int pt_y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, HookProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll")]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint min, uint max);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TranslateMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref Msg lpMsg);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();
}
