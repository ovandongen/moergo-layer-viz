using MoergoLayerViz.Core.Layout.Dtsi;
using Xunit;

namespace MoergoLayerViz.Tests;

public class DtsiPhysicalLayoutParserTests
{
    private static DtsiPhysicalLayout LoadFixture(string fileName) =>
        DtsiPhysicalLayoutParser.Parse(File.ReadAllText(fileName));

    [Fact]
    public void Go60_HasSixtyKeys()
    {
        var layout = LoadFixture("go60.dtsi");
        Assert.Equal(60, layout.Keys.Count);
    }

    [Fact]
    public void Go60_FirstEntryMatchesDtsi()
    {
        var layout = LoadFixture("go60.dtsi");
        Assert.Equal(new DtsiKeyPhysical(100, 100, 0, 50, 0, 0, 0), layout.Keys[0]);
    }

    [Fact]
    public void Go60_LastEntryMatchesDtsi()
    {
        var layout = LoadFixture("go60.dtsi");
        Assert.Equal(new DtsiKeyPhysical(100, 100, 1450, 400, 0, 0, 0), layout.Keys[^1]);
    }

    [Fact]
    public void Go60_ParsesNegativeRotationWrapper()
    {
        var layout = LoadFixture("go60.dtsi");
        // Index 54 is the first negative-rotation thumb on the right hand:
        // <&key_physical_attrs 100 100 915 550 (-3700) 915 550>
        Assert.Equal(new DtsiKeyPhysical(100, 100, 915, 550, -3700, 915, 550), layout.Keys[54]);
    }

    [Fact]
    public void Glove80_HasEightyKeys()
    {
        var layout = LoadFixture("glove80.dtsi");
        Assert.Equal(80, layout.Keys.Count);
    }

    [Fact]
    public void Glove80_FirstEntryMatchesDtsi()
    {
        var layout = LoadFixture("glove80.dtsi");
        Assert.Equal(new DtsiKeyPhysical(100, 100, 0, 50, 0, 0, 0), layout.Keys[0]);
    }

    [Fact]
    public void Glove80_LastEntryMatchesDtsi()
    {
        var layout = LoadFixture("glove80.dtsi");
        Assert.Equal(new DtsiKeyPhysical(100, 100, 1700, 550, 0, 0, 0), layout.Keys[^1]);
    }

    [Fact]
    public void Glove80_LeftThumbsShareSinglePivot()
    {
        // The whole point of Phase 1's pivot work: every rotated left-thumb
        // key on Glove80 rotates around the same point outside its own bounds.
        var layout = LoadFixture("glove80.dtsi");
        var leftThumbs = layout.Keys.Where(k => k.Rotation != 0 && k.Rx < 850).ToList();
        Assert.Equal(6, leftThumbs.Count);
        Assert.All(leftThumbs, k =>
        {
            Assert.Equal(450, k.Rx);
            Assert.Equal(925, k.Ry);
        });
    }

    [Fact]
    public void Glove80_RightThumbsShareSinglePivot()
    {
        var layout = LoadFixture("glove80.dtsi");
        var rightThumbs = layout.Keys.Where(k => k.Rotation != 0 && k.Rx >= 850).ToList();
        Assert.Equal(6, rightThumbs.Count);
        Assert.All(rightThumbs, k =>
        {
            Assert.Equal(1350, k.Rx);
            Assert.Equal(925, k.Ry);
        });
    }

    [Fact]
    public void IgnoresSurroundingDevicetreeScaffolding()
    {
        const string text = """
            / {
                physical_layout0: physical_layout_0 {
                    compatible = "zmk,physical-layout";
                    keys = <&key_physical_attrs 100 100 0 0 0 0 0>
                         , <&key_physical_attrs 150 100 200 50 (-4500) 250 100>
                         ;
                };
            };
            """;
        var layout = DtsiPhysicalLayoutParser.Parse(text);
        Assert.Equal(2, layout.Keys.Count);
        Assert.Equal(new DtsiKeyPhysical(150, 100, 200, 50, -4500, 250, 100), layout.Keys[1]);
    }
}
