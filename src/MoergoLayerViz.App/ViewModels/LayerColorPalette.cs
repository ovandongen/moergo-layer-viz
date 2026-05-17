namespace MoergoLayerViz.App.ViewModels;

/// <summary>
/// Service contract for per-keyboard, per-layer color overrides on top of
/// the built-in palette. Consumers that need to mutate overrides
/// (settings VM, persistence loaders) should depend on this interface
/// instead of the static <see cref="LayerColorPalette"/> facade, which is
/// kept only so XAML converters and read-only call sites can stay
/// allocation-free.
/// </summary>
public interface ILayerColorService
{
    string GetDefaultColor(int index);
    string GetColor(string profileId, int index);
    void SetOverrides(Dictionary<string, Dictionary<int, string>> overrides);
    void SetOverride(string profileId, int index, string? hex);
    Dictionary<string, Dictionary<int, string>> Snapshot();
}

/// <summary>
/// Default <see cref="ILayerColorService"/> backing the static
/// <see cref="LayerColorPalette"/> facade. Registered as the
/// singleton in <c>AppServices</c> so test/DI consumers see the same
/// state the static does.
/// </summary>
public sealed class LayerColorService : ILayerColorService
{
    private static readonly string[] DefaultColors =
    [
        "#89B4FA", // layer 0 — blue
        "#F38BA8", // layer 1 — pink
        "#A6E3A1", // layer 2 — green
        "#FAB387", // layer 3 — orange
        "#CBA6F7", // layer 4 — purple
        "#F9E2AF", // layer 5 — yellow
        "#94E2D5", // layer 6 — teal
        "#F5C2E7", // layer 7 — magenta
        "#B4BEFE", // layer 8 — lavender
        "#EBA0AC", // layer 9 — maroon
    ];

    private Dictionary<string, Dictionary<int, string>> _overrides = new();

    public string GetDefaultColor(int index)
        => DefaultColors[((index % DefaultColors.Length) + DefaultColors.Length) % DefaultColors.Length];

    public string GetColor(string profileId, int index)
    {
        if (_overrides.TryGetValue(profileId, out var perLayer)
            && perLayer.TryGetValue(index, out var hex)
            && !string.IsNullOrWhiteSpace(hex))
        {
            return hex;
        }
        return GetDefaultColor(index);
    }

    public void SetOverrides(Dictionary<string, Dictionary<int, string>> overrides)
    {
        var copy = new Dictionary<string, Dictionary<int, string>>();
        if (overrides is not null)
        {
            foreach (var (profileId, perLayer) in overrides)
            {
                if (perLayer is null) continue;
                copy[profileId] = new Dictionary<int, string>(perLayer);
            }
        }
        _overrides = copy;
    }

    public void SetOverride(string profileId, int index, string? hex)
    {
        if (string.IsNullOrWhiteSpace(hex))
        {
            if (_overrides.TryGetValue(profileId, out var perLayer))
            {
                perLayer.Remove(index);
                if (perLayer.Count == 0) _overrides.Remove(profileId);
            }
            return;
        }
        if (!_overrides.TryGetValue(profileId, out var existing))
            _overrides[profileId] = existing = new Dictionary<int, string>();
        existing[index] = hex!;
    }

    public Dictionary<string, Dictionary<int, string>> Snapshot()
    {
        var copy = new Dictionary<string, Dictionary<int, string>>();
        foreach (var (k, v) in _overrides)
            copy[k] = new Dictionary<int, string>(v);
        return copy;
    }
}

/// <summary>
/// Default layer tab colors — indexed, wraps after 10. Matches the Svalboard
/// palette for visual consistency with the sister app. Per-keyboard, per-layer
/// overrides set by the user via the Settings window are looked up first;
/// missing entries fall through to the built-in palette.
///
/// <para>This static class is the historical entry point for XAML
/// converters and view code; it now delegates to a single
/// <see cref="LayerColorService"/> instance that DI consumers also
/// resolve. New code with access to the container should prefer
/// <see cref="ILayerColorService"/> directly.</para>
/// </summary>
public static class LayerColorPalette
{
    /// <summary>Single instance shared with DI. Mutations and reads land in the same state.</summary>
    internal static readonly LayerColorService Service = new();

    /// <summary>Built-in palette color for a layer (no override lookup).</summary>
    public static string GetDefaultColor(int index) => Service.GetDefaultColor(index);

    /// <summary>
    /// Effective color for a layer on the given keyboard profile — user override wins,
    /// otherwise falls back to <see cref="GetDefaultColor"/>.
    /// </summary>
    public static string GetColor(string profileId, int index) => Service.GetColor(profileId, index);

    /// <summary>
    /// Replaces the override map wholesale. Called once at startup from
    /// <c>MainWindowViewModel</c> with the persisted nested dictionary.
    /// </summary>
    public static void SetOverrides(Dictionary<string, Dictionary<int, string>> overrides)
        => Service.SetOverrides(overrides);

    /// <summary>
    /// Sets or clears a single override. Pass <c>null</c> or empty hex to remove
    /// the override and revert to the built-in palette for that layer.
    /// </summary>
    public static void SetOverride(string profileId, int index, string? hex)
        => Service.SetOverride(profileId, index, hex);

    /// <summary>Deep copy of the current overrides — used when serializing back to <c>UserSettings</c>.</summary>
    public static Dictionary<string, Dictionary<int, string>> Snapshot() => Service.Snapshot();
}
