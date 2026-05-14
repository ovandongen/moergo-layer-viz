using System.Diagnostics;
using MoergoLayerViz.Core.Diagnostics;

namespace MoergoLayerViz.App;

/// <summary>
/// Bridges <see cref="System.Diagnostics.Trace"/> output from the
/// ZmkHidProtocol library (which uses <c>LibLog</c> → <c>Trace.WriteLine</c>
/// to avoid forcing a logging dependency on consumers) into the app's own
/// <see cref="DiagnosticLog"/>. Messages prefixed with
/// <c>[ZmkHidProtocol:&lt;subsystem&gt;]</c> get parsed back into a
/// subsystem tag; anything else is logged under <c>Trace</c>.
/// </summary>
internal sealed class ZmkHidProtocolTraceListener : TraceListener
{
    private const string Prefix = "[ZmkHidProtocol:";

    public override void Write(string? message) => WriteLine(message);

    public override void WriteLine(string? message)
    {
        if (!TrySplitPrefix(message, out var subsystem, out var body)) return;
        DiagnosticLog.Info(subsystem, body);
    }

    public override void TraceEvent(TraceEventCache? eventCache, string source, TraceEventType eventType, int id, string? message)
    {
        if (!TrySplitPrefix(message, out var subsystem, out var body)) return;
        switch (eventType)
        {
            case TraceEventType.Error:
            case TraceEventType.Critical:
                DiagnosticLog.Error(subsystem, body);
                break;
            case TraceEventType.Warning:
                DiagnosticLog.Warn(subsystem, body);
                break;
            default:
                DiagnosticLog.Info(subsystem, body);
                break;
        }
    }

    private static bool TrySplitPrefix(string? message, out string subsystem, out string body)
    {
        subsystem = ""; body = "";
        if (string.IsNullOrEmpty(message) || !message.StartsWith(Prefix)) return false;
        int end = message.IndexOf(']', Prefix.Length);
        if (end < 0) return false;
        subsystem = message.Substring(Prefix.Length, end - Prefix.Length);
        body = end + 2 <= message.Length ? message.Substring(end + 2) : "";
        return true;
    }
}
