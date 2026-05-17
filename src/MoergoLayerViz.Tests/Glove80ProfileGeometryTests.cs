using MoergoLayerViz.Core.Layout;
using Xunit;

namespace MoergoLayerViz.Tests;

/// <summary>
/// Pins the dtsi-driven Glove80 geometry. Glove80 uses identity binding→dtsi
/// mapping (unlike Go60, which needs a 9-entry remap), so dtsi keys[N] is
/// directly the geometry for binding N. All keys at 0.6 px/centi-u with a
/// (30, 60) canvas inset, same conversion as Go60.
/// </summary>
public class Glove80ProfileGeometryTests
{
    private readonly Glove80Profile _profile = new();

    [Fact]
    public void HasEightyKeys()
    {
        Assert.Equal(80, _profile.Keys.Count);
    }

    [Fact]
    public void IndicesAreSequential()
    {
        for (int i = 0; i < _profile.Keys.Count; i++)
            Assert.Equal(i, _profile.Keys[i].Index);
    }

    [Fact]
    public void Binding0_IsLeftOuterPinkyDropped()
    {
        // dtsi (0, 50) → pixel (30, 90). F1 in base layer.
        var k = _profile.Keys[0];
        Assert.Equal(30, k.X);
        Assert.Equal(90, k.Y);
        Assert.Equal(Hand.Left, k.Hand);
        Assert.Equal(0, k.RotationDegrees);
        Assert.Equal("Row 1, L col 1 (outer pinky)", k.Description);
    }

    [Fact]
    public void Binding5_IsRightInnerIndexTopRow()
    {
        // dtsi 5 = (1300, 0) → R index, row 1.
        var k = _profile.Keys[5];
        Assert.Equal(Hand.Right, k.Hand);
        Assert.Equal(810, k.X); // 1300 * 0.6 + 30
        Assert.Equal(60, k.Y);
        Assert.Equal("Row 1, R col 5 (index)", k.Description);
    }

    [Fact]
    public void Binding52_IsLeftThumbARowOuter()
    {
        // dtsi 52: (400, 450) rot +30° about pivot (450, 925).
        var k = _profile.Keys[52];
        Assert.Equal(Hand.Left, k.Hand);
        Assert.Equal(30, k.RotationDegrees);
        Assert.Equal("Left thumb A1 (outer)", k.Description);
        Assert.Equal(450 * 0.6 + 30, k.RotationOriginX);
        Assert.Equal(925 * 0.6 + 60, k.RotationOriginY);
    }

    [Fact]
    public void Binding54_IsLeftThumbARowInner()
    {
        // dtsi 54: (400, 450) rot +60° — innermost left thumb on row A.
        var k = _profile.Keys[54];
        Assert.Equal(Hand.Left, k.Hand);
        Assert.Equal(60, k.RotationDegrees);
        Assert.Equal("Left thumb A3 (inner)", k.Description);
    }

    [Fact]
    public void Binding55_IsRightThumbARowInner()
    {
        // dtsi 55: (1300, 450) rot −60° — innermost right thumb on row A.
        var k = _profile.Keys[55];
        Assert.Equal(Hand.Right, k.Hand);
        Assert.Equal(-60, k.RotationDegrees);
        Assert.Equal("Right thumb A1 (inner)", k.Description);
        // All RH thumbs share pivot (1350, 925).
        Assert.Equal(1350 * 0.6 + 30, k.RotationOriginX);
        Assert.Equal(925 * 0.6 + 60, k.RotationOriginY);
    }

    [Fact]
    public void Binding57_IsRightThumbARowOuter()
    {
        var k = _profile.Keys[57];
        Assert.Equal(Hand.Right, k.Hand);
        Assert.Equal(-30, k.RotationDegrees);
        Assert.Equal("Right thumb A3 (outer)", k.Description);
    }

    [Fact]
    public void Binding63_IsRightOuterPinkyRow5()
    {
        // dtsi 63: (1700, 450) — right outer pinky, row 5, dropped half row.
        var k = _profile.Keys[63];
        Assert.Equal(Hand.Right, k.Hand);
        Assert.Equal(0, k.RotationDegrees);
        Assert.Equal(1700 * 0.6 + 30, k.X);
        Assert.Equal(450 * 0.6 + 60, k.Y);
        Assert.Equal("Row 5, R col 1 (outer pinky)", k.Description);
    }

    [Fact]
    public void Binding64_IsLeftMagicRow6Outer()
    {
        // dtsi 64: (0, 550) — bottom-left dropped half row, &magic on base layer.
        var k = _profile.Keys[64];
        Assert.Equal(Hand.Left, k.Hand);
        Assert.Equal(30, k.X);
        Assert.Equal(550 * 0.6 + 60, k.Y);
        Assert.Equal("Row 6, L col 1 (outer pinky)", k.Description);
    }

    [Fact]
    public void Binding71_IsLeftThumbBRowInner()
    {
        // dtsi 71: (400, 550) rot +60° — innermost lower-row left thumb.
        var k = _profile.Keys[71];
        Assert.Equal(Hand.Left, k.Hand);
        Assert.Equal(60, k.RotationDegrees);
        Assert.Equal("Left thumb B3 (inner)", k.Description);
    }

    [Fact]
    public void Binding79_IsRightOuterPinkyRow6()
    {
        // dtsi 79: (1700, 550) — bottom-right, &kp PG_DN on base layer.
        var k = _profile.Keys[79];
        Assert.Equal(Hand.Right, k.Hand);
        Assert.Equal(1700 * 0.6 + 30, k.X);
        Assert.Equal(550 * 0.6 + 60, k.Y);
        Assert.Equal("Row 6, R col 1 (outer pinky)", k.Description);
    }

    [Fact]
    public void AllThumbsShareTheirHandsPivot()
    {
        // Twelve thumbs in total, all sharing one pivot per hand. This is a
        // Glove80-specific quirk (Go60 thumb pivots coincide with each key's
        // own centre).
        int[] leftThumbs = { 52, 53, 54, 69, 70, 71 };
        int[] rightThumbs = { 55, 56, 57, 72, 73, 74 };
        var expectedLeftOx = 450 * 0.6 + 30;
        var expectedLeftOy = 925 * 0.6 + 60;
        var expectedRightOx = 1350 * 0.6 + 30;
        var expectedRightOy = 925 * 0.6 + 60;
        foreach (var idx in leftThumbs)
        {
            Assert.Equal(expectedLeftOx, _profile.Keys[idx].RotationOriginX);
            Assert.Equal(expectedLeftOy, _profile.Keys[idx].RotationOriginY);
        }
        foreach (var idx in rightThumbs)
        {
            Assert.Equal(expectedRightOx, _profile.Keys[idx].RotationOriginX);
            Assert.Equal(expectedRightOy, _profile.Keys[idx].RotationOriginY);
        }
    }

    [Fact]
    public void NonRotatedKeysHaveNullPivot()
    {
        var k = _profile.Keys[0];
        Assert.Null(k.RotationOriginX);
        Assert.Null(k.RotationOriginY);
    }

    [Fact]
    public void AllKeysFitWithinCanvas()
    {
        foreach (var k in _profile.Keys)
        {
            Assert.InRange(k.X, 0, _profile.CanvasWidth - k.Width);
            Assert.InRange(k.Y, 0, _profile.CanvasHeight - k.Height);
        }
    }
}
