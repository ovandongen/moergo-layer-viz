using MoergoLayerViz.Core.Layout;
using Xunit;

namespace MoergoLayerViz.Tests;

/// <summary>
/// Pins the dtsi-driven Go60 geometry so future parser / scale tweaks don't
/// silently move keys. The values here are recorded from the local
/// <c>Resources/go60.dtsi</c> (row-5/thumb block reordered to match JSON
/// binding order) at 0.6 px/centi-u with a (30, 60) canvas inset.
/// </summary>
public class Go60ProfileGeometryTests
{
    private readonly Go60Profile _profile = new();

    [Fact]
    public void HasSixtyKeys()
    {
        Assert.Equal(60, _profile.Keys.Count);
    }

    [Fact]
    public void IndicesAreSequential()
    {
        for (int i = 0; i < _profile.Keys.Count; i++)
            Assert.Equal(i, _profile.Keys[i].Index);
    }

    [Fact]
    public void Row1OuterPinkyLeft_IsDroppedHalfRow()
    {
        // Dtsi (0, 50) → pixel (0*0.6+30, 50*0.6+60) = (30, 90).
        var k = _profile.Keys[0];
        Assert.Equal(30, k.X);
        Assert.Equal(90, k.Y);
        Assert.Equal(Hand.Left, k.Hand);
        Assert.Equal(0, k.RotationDegrees);
        Assert.Equal("Row 1, L col 1 (outer pinky)", k.Description);
    }

    [Fact]
    public void Row1InnerIndexLeft_IsAtTopRow()
    {
        // Binding 5 = dtsi 5 = (500, 0) → (330, 60). Row 1 inner index.
        var k = _profile.Keys[5];
        Assert.Equal(330, k.X);
        Assert.Equal(60, k.Y);
        Assert.Equal(Hand.Left, k.Hand);
        Assert.Equal("Row 1, L col 6 (inner index)", k.Description);
    }

    [Fact]
    public void Row1InnerIndexRight_IsBindingSix()
    {
        // Binding 6 = dtsi 6 = (1150, 0) → R inner index.
        var k = _profile.Keys[6];
        Assert.Equal(Hand.Right, k.Hand);
        Assert.Equal("Row 1, R col 6 (inner index)", k.Description);
    }

    [Fact]
    public void Binding51_IsRightRow5Index()
    {
        // JSON binding 51 = right row 5 index. Local Go60 dtsi is reordered so
        // dtsi index 51 already points at that key (no remap).
        var k = _profile.Keys[51];
        Assert.Equal(Hand.Right, k.Hand);
        Assert.Equal(0, k.RotationDegrees);
        Assert.Equal("Row 5, R col 5 (index)", k.Description);
        // (1250, 400) → (1250*0.6+30, 400*0.6+60) = (780, 300).
        Assert.Equal(780, k.X);
        Assert.Equal(300, k.Y);
    }

    [Fact]
    public void Binding56_IsInnermostLeftThumb()
    {
        // Verified against the shipped Go60.json: binding 56 = LT2 TAB at the
        // rightmost (innermost) left-thumb position.
        var k = _profile.Keys[56];
        Assert.Equal(Hand.Left, k.Hand);
        Assert.Equal("Left thumb 3 (inner)", k.Description);
        Assert.Equal(37, k.RotationDegrees);
        Assert.NotNull(k.RotationOriginX);
        Assert.NotNull(k.RotationOriginY);
    }

    [Fact]
    public void Binding57_IsInnermostRightThumb()
    {
        var k = _profile.Keys[57];
        Assert.Equal(Hand.Right, k.Hand);
        Assert.Equal("Right thumb 1 (inner)", k.Description);
        Assert.Equal(-37, k.RotationDegrees);
    }

    [Fact]
    public void ThumbsCarryDtsiRotationPivot()
    {
        // Go60 thumb pivots equal each key's own center, so the loader still
        // surfaces an explicit rotation origin — it just happens to coincide
        // with the key's centre. Glove80 will share one pivot across thumbs.
        var k = _profile.Keys[54]; // Left thumb 1 (outer): dtsi (520, 410, pivot 520,410)
        Assert.Equal(520 * 0.6 + 30, k.RotationOriginX);
        Assert.Equal(410 * 0.6 + 60, k.RotationOriginY);
    }

    [Fact]
    public void NonRotatedKeysHaveNullPivot()
    {
        // The KeyViewModel translates null → RelativePoint.Center, so the
        // common case stays the same as before Phase 1's pivot work.
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
