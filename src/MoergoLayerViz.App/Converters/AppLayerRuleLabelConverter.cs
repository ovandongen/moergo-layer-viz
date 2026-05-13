using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Data.Converters;
using MoergoLayerViz.App.Localization;
using MoergoLayerViz.App.ViewModels;

namespace MoergoLayerViz.App.Converters;

/// <summary>
/// Renders an <see cref="MoergoLayerViz.Core.Settings.AppLayerRule"/>'s target
/// layer as "Symbol (L:1)" — or "(invalid layer)" if the rule points at an
/// index no longer present in the active keymap.
/// </summary>
/// <remarks>
/// Inputs (in order): <c>LayerIndex</c> (int), <c>Layers</c>
/// (<see cref="IEnumerable{LayerViewModel}"/>), and <c>Layers.Count</c>
/// (int — included only to retrigger evaluation when the keymap reloads,
/// since the collection reference itself stays stable).
/// </remarks>
public class AppLayerRuleLabelConverter : IMultiValueConverter
{
    public static readonly AppLayerRuleLabelConverter Instance = new();

    public object Convert(IList<object?> values, System.Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Count < 2 || values[0] is not int layerIndex || values[1] is not IEnumerable layers)
            return "";

        foreach (var item in layers)
        {
            if (item is LayerViewModel lvm && lvm.Index == layerIndex)
                return Loc.Instance.Format("Settings_AutoSwitch_LayerFormat", lvm.DisplayName, lvm.Index);
        }
        return Loc.Instance["Settings_AutoSwitch_LayerInvalid"];
    }
}
