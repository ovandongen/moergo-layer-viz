using System.Text.Json.Serialization;

namespace MoergoLayerViz.Core.Settings;

/// <summary>
/// Where the auto-switch engine pushes the keyboard when focus moves away
/// from any rule-matched app. Persisted per-keyboard in
/// <see cref="UserSettings.AutoSwitchFallback"/>; missing entries resolve to
/// <see cref="Previous"/>.
/// </summary>
/// <remarks>
/// Serialized as the case-insensitive enum name. Existing settings files
/// holding the literal strings "Previous" / "Base" deserialize cleanly.
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter<AutoSwitchFallbackMode>))]
public enum AutoSwitchFallbackMode
{
    /// <summary>Restore the layer that was active when the session began.</summary>
    Previous,

    /// <summary>Always push layer 0.</summary>
    Base,
}
