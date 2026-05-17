using System.ComponentModel;
using System.Globalization;
using MoergoLayerViz.App.Resources;

namespace MoergoLayerViz.App.Localization;

/// <summary>
/// Service contract for runtime-switchable localization. Consumers that
/// don't need to be the AXAML `{StaticResource Loc}` source should depend
/// on this interface and resolve from the container; the concrete
/// <see cref="Loc"/> class keeps the static facade alive for compiled
/// AXAML bindings.
/// </summary>
public interface ILocalization
{
    /// <summary>Looks up a localized string by resource key. Returns <c>[key]</c> if missing.</summary>
    string this[string key] { get; }

    /// <summary>Looks up a format string by key and applies <paramref name="args"/>.</summary>
    string Format(string key, params object[] args);
}

/// <summary>
/// Lightweight localization singleton for AXAML binding.
/// Usage in AXAML: Text="{Binding [Key_Name], Source={StaticResource Loc}}"
/// Supports runtime language switching via SetCulture().
/// </summary>
public class Loc : INotifyPropertyChanged, ILocalization
{
    /// <summary>Shared culture across all instances (AXAML resource creates its own instance).</summary>
    private static CultureInfo? _culture;

    /// <summary>All live instances, so SetCulture can notify the AXAML-created one too.</summary>
    private static readonly List<WeakReference<Loc>> _instances = [];

    public static Loc Instance { get; } = new();

    /// <summary>Raised after culture changes.</summary>
    public static event Action? CultureChanged;

    public Loc()
    {
        _instances.Add(new WeakReference<Loc>(this));
    }

    public string this[string key] =>
        Strings.ResourceManager.GetString(key, _culture) ?? $"[{key}]";

    /// <summary>Switches the UI language at runtime.</summary>
    public void SetCulture(string cultureCode)
    {
        _culture = string.IsNullOrEmpty(cultureCode) || cultureCode == "en"
            ? CultureInfo.InvariantCulture
            : new CultureInfo(cultureCode);

        _instances.RemoveAll(w => !w.TryGetTarget(out _));
        foreach (var weakRef in _instances)
        {
            if (weakRef.TryGetTarget(out var loc))
                loc.PropertyChanged?.Invoke(loc, new PropertyChangedEventArgs("Item[]"));
        }

        CultureChanged?.Invoke();
    }

    /// <summary>Gets a localized format string and applies arguments.</summary>
    public string Format(string key, params object[] args)
    {
        var template = this[key];
        return string.Format(template, args);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
