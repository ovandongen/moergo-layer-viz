using MoergoLayerViz.Core.Keymap;
using MoergoLayerViz.Core.Settings;

namespace MoergoLayerViz.App.Services;

/// <summary>
/// Owns the merge of (auto-detected signal macros) ∪ (user's persisted manual
/// layer-signal mappings) for the active keyboard profile. The merged table
/// is what the live hotkey tracker actually consults; the auto-only table is
/// kept around so callers can ask "is this layer already covered by auto?".
///
/// <para>Auto wins: if a layer has an auto-detected signal macro, the user's
/// manual entry for that layer is silently ignored. Keycode dedup is first-
/// writer-wins (auto entries first, then manual), so a manual entry that
/// tries to reuse an auto keycode is dropped.</para>
/// </summary>
public sealed class MergedSignalTableManager
{
    private readonly ISettingsService _settingsService;
    private LayerSignalTable _autoTable = new(new Dictionary<string, SignalKeyMapping>());
    private LayerSignalTable _mergedTable = new(new Dictionary<string, SignalKeyMapping>());
    private string _activeProfileId;

    /// <summary>Raised after every merged-table rebuild. Subscribers typically
    /// push the new table into the live hotkey tracker and bump any
    /// settings-tab pickers that depend on the "taken keycode" set.</summary>
    public event Action<LayerSignalTable>? MergedTableChanged;

    public MergedSignalTableManager(ISettingsService settingsService, string activeProfileId)
    {
        _settingsService = settingsService;
        _activeProfileId = activeProfileId;
    }

    /// <summary>Auto-detected mappings from the loaded layout. Untouched by manual entries.</summary>
    public LayerSignalTable AutoTable => _autoTable;

    /// <summary>Effective table: auto + active manual mappings for the current profile.</summary>
    public LayerSignalTable MergedTable => _mergedTable;

    /// <summary>Replace the auto table (e.g. on layout load, profile switch, or layout unload) and rebuild the merge.</summary>
    public void SetAutoTable(LayerSignalTable autoTable)
    {
        _autoTable = autoTable;
        Rebuild();
    }

    /// <summary>Re-key manual lookups against a different keyboard profile and rebuild the merge.</summary>
    public void SetActiveProfile(string profileId)
    {
        if (string.Equals(profileId, _activeProfileId, StringComparison.OrdinalIgnoreCase)) return;
        _activeProfileId = profileId;
        Rebuild();
    }

    /// <summary>First auto-detected signal keycode that activates <paramref name="layerIndex"/>, or null.</summary>
    public string? GetAutoSignalKeycodeForLayer(int layerIndex)
    {
        foreach (var (kc, m) in _autoTable.Mappings)
            if (m.TargetLayer == layerIndex) return kc;
        return null;
    }

    /// <summary>User's manual signal keycode for the given layer on the current profile, or null.</summary>
    public string? GetManualSignalKeycodeForLayer(int layerIndex)
    {
        var s = _settingsService.Load();
        return s.ManualLayerSignals.TryGetValue(_activeProfileId, out var perLayer)
               && perLayer.TryGetValue(layerIndex, out var kc)
            ? kc
            : null;
    }

    /// <summary>
    /// Sets or clears the user's manual signal-keycode binding for the given
    /// layer on the active profile. Persists, rebuilds the merged table, and
    /// raises <see cref="MergedTableChanged"/>. Auto-detected mappings always
    /// win, so this is a no-op (silently persisted but ineffective) for
    /// layers already covered by auto-detection.
    /// </summary>
    public void SetManualLayerSignal(int layerIndex, string? keycode)
    {
        var profileId = _activeProfileId;
        var s = _settingsService.Load();

        var clone = new Dictionary<string, Dictionary<int, string>>();
        foreach (var (pid, perLayer) in s.ManualLayerSignals)
            clone[pid] = new Dictionary<int, string>(perLayer);

        if (string.IsNullOrWhiteSpace(keycode))
        {
            if (clone.TryGetValue(profileId, out var inner))
            {
                inner.Remove(layerIndex);
                if (inner.Count == 0) clone.Remove(profileId);
            }
        }
        else
        {
            if (!clone.TryGetValue(profileId, out var inner))
                clone[profileId] = inner = new Dictionary<int, string>();
            inner[layerIndex] = keycode!;
        }

        _settingsService.Save(s with { ManualLayerSignals = clone });
        Rebuild();
    }

    private void Rebuild()
    {
        var merged = new Dictionary<string, SignalKeyMapping>(StringComparer.OrdinalIgnoreCase);
        foreach (var (kc, m) in _autoTable.Mappings) merged[kc] = m;

        // Layers already covered by auto-detection — manual entries for these
        // are silently ignored (auto wins, per the UX rule "when autoswitch
        // works, user can not override").
        var autoLayers = new HashSet<int>(_autoTable.Mappings.Values.Select(m => m.TargetLayer));

        var s = _settingsService.Load();
        if (s.ManualLayerSignals.TryGetValue(_activeProfileId, out var perLayer))
        {
            foreach (var (layerIdx, keycode) in perLayer)
            {
                if (autoLayers.Contains(layerIdx)) continue;
                if (string.IsNullOrWhiteSpace(keycode)) continue;
                if (merged.ContainsKey(keycode)) continue;  // first writer wins for keycode dedup
                // Manual mappings target layers reached via &to/&tog which have
                // no release event, so toggle-on-press semantics fit better
                // than momentary hold.
                merged[keycode] = new SignalKeyMapping(keycode, layerIdx, IsMomentary: false, "manual");
            }
        }

        _mergedTable = new LayerSignalTable(merged);
        MergedTableChanged?.Invoke(_mergedTable);
    }
}
