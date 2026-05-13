using CommunityToolkit.Mvvm.ComponentModel;
using MoergoLayerViz.Core.Settings;

namespace MoergoLayerViz.App.ViewModels;

/// <summary>
/// UI wrapper around <see cref="AppLayerRule"/> for the rules list.
/// Carries a pre-formatted <see cref="LayerLabel"/> so the row can show
/// "Symbol (L:1)" — or "(invalid layer)" if the rule points at a layer
/// index that no longer exists in the active keymap.
/// </summary>
public sealed partial class AppLayerRuleRow : ObservableObject
{
    public AppLayerRule Rule { get; }
    public string ProcessMatch => Rule.ProcessMatch;
    public int LayerIndex => Rule.LayerIndex;

    [ObservableProperty]
    private string _layerLabel = "";

    public AppLayerRuleRow(AppLayerRule rule, string layerLabel)
    {
        Rule = rule;
        _layerLabel = layerLabel;
    }
}
