using MoergoLayerViz.Core.Keymap;
using MoergoLayerViz.Core.Layout;
using Xunit;

namespace MoergoLayerViz.Tests;

public class MoergoJsonLoaderTests
{
    [Fact]
    public void LoadsDecorationLabel_FromBinding()
    {
        const string json = """
        {
          "keyboard": "go60",
          "layer_names": ["Base"],
          "layers": [[
            {
              "value": "&kp",
              "params": [ { "value": "LS", "params": [ { "value": "LC", "params": [ { "value": "V" } ] } ] } ],
              "decoration": { "label": "Paste", "icon": "fa-paste", "background": "#fffec9" }
            },
            { "value": "&trans" }
          ]]
        }
        """;

        var config = MoergoJsonLoader.LoadFromJson(json);
        var bindings = config.Layers[0].Bindings;

        Assert.Equal("Paste", bindings[0].DecorationLabel);
        Assert.Equal("&kp", bindings[0].Behavior);
        Assert.Equal(new[] { "LS", "LC", "V" }, bindings[0].Params);

        Assert.Null(bindings[1].DecorationLabel);
    }

    [Fact]
    public void LoadsGo60FromJson_ProducesFiveLayersWithSixtyKeysEach()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Go60.json");
        Assert.True(File.Exists(path), $"Test fixture missing: {path}");

        var config = MoergoJsonLoader.LoadFromFile(path);
        Assert.Equal("go60", config.KeyboardId);
        Assert.Equal(5, config.LayerCount);
        Assert.All(config.Layers, l => Assert.Equal(60, l.Bindings.Count));
    }

    [Fact]
    public void LoadsGlove80FromJson_ProducesNineLayersWithEightyKeysEach()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Glove80.json");
        Assert.True(File.Exists(path), $"Test fixture missing: {path}");

        var config = MoergoJsonLoader.LoadFromFile(path);
        Assert.Equal("glove80", config.KeyboardId);
        Assert.Equal(9, config.LayerCount);
        Assert.All(config.Layers, l => Assert.Equal(80, l.Bindings.Count));
    }

    [Fact]
    public void Go60Profile_Returns60KeyPositions()
    {
        var profile = new Go60Profile();
        Assert.Equal(60, profile.Keys.Count);
        Assert.Equal(60, profile.KeyCount);
        // Indexes should be 0..59 contiguous
        for (int i = 0; i < profile.Keys.Count; i++)
            Assert.Equal(i, profile.Keys[i].Index);
    }

    [Fact]
    public void Glove80Profile_Returns80KeyPositions()
    {
        var profile = new Glove80Profile();
        Assert.Equal(80, profile.Keys.Count);
        Assert.Equal(80, profile.KeyCount);
        for (int i = 0; i < profile.Keys.Count; i++)
            Assert.Equal(i, profile.Keys[i].Index);

        foreach (var k in profile.Keys)
        {
            Assert.InRange(k.X, 0, profile.CanvasWidth - k.Width);
            Assert.InRange(k.Y, 0, profile.CanvasHeight - k.Height);
        }

        // 6 thumb keys per hand = 12 rotated.
        Assert.Equal(12, profile.Keys.Count(k => k.RotationDegrees != 0));
    }
}
