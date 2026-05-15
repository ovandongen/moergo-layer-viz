using System.Runtime.InteropServices;
using MoergoLayerViz.Core.Diagnostics;

namespace MoergoLayerViz.App.Services.Hotkeys;

/// <summary>
/// Probes Windows for F-keys that another process has already claimed via
/// <c>RegisterHotKey</c>. We register-and-immediately-unregister each F1..F24
/// with no modifiers from the calling thread; combos already owned by
/// someone else return ERROR_HOTKEY_ALREADY_REGISTERED (1409) and land in
/// the claimed set.
///
/// The probe sees our own live registration too. Callers pass the name of
/// the key we currently own via <paramref name="ignoreKey"/> so the picker
/// keeps the user's own current pick selectable.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
internal static class WindowsFreeFKeys
{
    private const uint ModNoRepeat = 0x4000;
    // Probe id at the top of the allowed user range (0x0000..0xBFFF) so it
    // never collides with the incrementing tokens WindowsHotkeyRegistry hands out.
    private const int ProbeId = 0xBFFE;

    public static IReadOnlySet<int> GetClaimedFKeyIndices(string? ignoreKey = null)
    {
        var claimed = new HashSet<int>();
        var ignoreIndex = ignoreKey is null ? null : HotkeyNameMap.TryParseFKeyIndex(ignoreKey);

        // Probe on a dedicated thread with a freshly-created message queue.
        // RegisterHotKey with hWnd=NULL routes WM_HOTKEY to the calling
        // thread's queue, so we want a clean, controlled context — avoids
        // any chance the UI thread's existing queue state affects the
        // register/unregister cycle.
        var thread = new Thread(() =>
        {
            PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
            for (var n = 1; n <= 24; n++)
            {
                if (n == ignoreIndex) continue;
                var vk = (uint)(0x70 + (n - 1));
                var ok = RegisterHotKey(IntPtr.Zero, ProbeId, ModNoRepeat, vk);
                if (ok) UnregisterHotKey(IntPtr.Zero, ProbeId);
                else claimed.Add(n);
            }
        }) { IsBackground = true, Name = "HotkeyProbe" };
        thread.Start();
        thread.Join();

        DiagnosticLog.Info("HotkeyWin",
            $"claimed F-keys: [{string.Join(",", claimed.OrderBy(x => x))}]"
            + (ignoreKey is null ? "" : $" (ignoring own '{ignoreKey}')"));
        return claimed;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

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
}
