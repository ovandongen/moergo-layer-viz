using System.IO;
using MoergoLayerViz.App.ViewModels;
using MoergoLayerViz.Core.Keymap;
using MoergoLayerViz.Core.Settings;
using Xunit;

namespace MoergoLayerViz.Tests;

/// <summary>
/// User-defined hold-taps whose hold side is a layer-switch behavior
/// (e.g. <c>&amp;space_v3_TKZ</c> with bindings <c>["&amp;mo", "&amp;kp"]</c>)
/// must surface the *layer name* in the rendered subscript and the per-tab
/// label must include the layer index. Regression for the TailorKey v5.2²
/// shape.
/// </summary>
public class HoldTapLayerNameTests
{
    private sealed class InMemorySettingsService : ISettingsService
    {
        public UserSettings Current { get; set; } = new() { Keyboard = "GO60" };
        public UserSettings Load() => Current;
        public void Save(UserSettings settings) => Current = settings;
    }

    private const string HoldTapMoJson = """
    {
      "keyboard": "go60",
      "layer_names": ["Base", "Symbol"],
      "layers": [
        [ { "value": "&space_v3_TKZ", "params": [{ "value": 1 }, { "value": "SPACE" }] } ],
        [ { "value": "&trans" } ]
      ],
      "holdTaps": [
        {
          "name": "&space_v3_TKZ",
          "description": "thumb space layer-tap",
          "bindings": ["&mo", "&kp"],
          "tappingTermMs": 200,
          "flavor": "balanced"
        }
      ]
    }
    """;

    [Fact]
    public void HoldTapWithMoOnHoldSide_ShowsLayerNameAsSubscript()
    {
        var vm = LoadVm();

        vm.Layers[0].SelectCommand.Execute(null);
        var key = vm.Keys[0];

        // Subscript is what we actually changed: must be the layer *name*,
        // not the layer *number* the recursive holdLabel fallback used to
        // produce.
        Assert.Equal("Symbol", key.Subscript);
        Assert.Equal("Hold-Tap", key.TopLeftLabel);
        Assert.False(string.IsNullOrEmpty(key.Label));
        Assert.NotEqual("1", key.Subscript);
    }

    [Fact]
    public void BareMo_RendersLayerName_NotIndex()
    {
        const string json = """
        {
          "keyboard": "go60",
          "layer_names": ["Base", "Symbol", "Mouse"],
          "layers": [
            [ { "value": "&mo", "params": [{ "value": 2 }] } ],
            [ { "value": "&trans" } ],
            [ { "value": "&trans" } ]
          ]
        }
        """;
        var path = Path.Combine(Path.GetTempPath(), $"bare_mo_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        try
        {
            var vm = new MainWindowViewModel(new InMemorySettingsService());
            vm.LoadLayoutFromPath(path);
            vm.Layers[0].SelectCommand.Execute(null);

            Assert.Equal("Mouse", vm.Keys[0].Label);
            Assert.Equal("Momentary", vm.Keys[0].TopLeftLabel);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    [Fact]
    public void LayerTabs_PrefixDisplayNameWithIndexAndSeparator()
    {
        var vm = LoadVm();

        Assert.Equal("0 : Base", vm.Layers[0].DisplayName);
        Assert.Equal("1 : Symbol", vm.Layers[1].DisplayName);
    }

    [Theory]
    [InlineData("Symbol", "Symbol")]            // single word → unchanged
    [InlineData("Mouse", "Mouse")]              // single word → unchanged
    [InlineData("HRM_WinLinx", "HRM Win Linx")] // underscore + camelCase
    [InlineData("HRM_left_pinky", "HRM left pinky")] // pure underscore
    [InlineData("WinLinx", "Win Linx")]          // pure camelCase
    [InlineData("HRM_v1_TKZ", "HRM v1 TKZ")]    // digits + uppercase run
    [InlineData("", "")]                          // empty stays empty
    [InlineData("UPPER", "UPPER")]               // all-uppercase acronym preserved
    public void FormatLayerName_InsertsBreaksOnUnderscoreAndCamelCase(string input, string expected)
    {
        Assert.Equal(expected, KeyLabelFormatter.FormatLayerName(input));
    }

    [Fact]
    public void BareMo_LongCamelCaseLayerName_IsSplitForWrapping()
    {
        const string json = """
        {
          "keyboard": "go60",
          "layer_names": ["Base", "HRM_WinLinx"],
          "layers": [
            [ { "value": "&mo", "params": [{ "value": 1 }] } ],
            [ { "value": "&trans" } ]
          ]
        }
        """;
        var path = Path.Combine(Path.GetTempPath(), $"long_layer_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        try
        {
            var vm = new MainWindowViewModel(new InMemorySettingsService());
            vm.LoadLayoutFromPath(path);
            vm.Layers[0].SelectCommand.Execute(null);

            // Label must contain wrap opportunities so TextWrapping="Wrap" can
            // break it onto multiple lines instead of overflowing the key.
            Assert.Equal("HRM Win Linx", vm.Keys[0].Label);
        }
        finally { try { File.Delete(path); } catch { } }
    }

    private static MainWindowViewModel LoadVm()
    {
        var path = Path.Combine(Path.GetTempPath(), $"holdtap_layer_{Guid.NewGuid():N}.json");
        File.WriteAllText(path, HoldTapMoJson);
        try
        {
            var vm = new MainWindowViewModel(new InMemorySettingsService());
            vm.LoadLayoutFromPath(path);
            Assert.True(vm.HasLayoutLoaded);
            return vm;
        }
        finally
        {
            try { File.Delete(path); } catch { }
        }
    }
}
