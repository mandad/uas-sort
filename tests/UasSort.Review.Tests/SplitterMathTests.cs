// tests/UasSort.Review.Tests/SplitterMathTests.cs — the Review page's pane-splitter arithmetic (Ref §9.3 panes; Task U1)
namespace UasSort.Review.Tests;

public sealed class SplitterMathTests
{
    private const double Inf = double.PositiveInfinity;

    // ReviewPage columns: TimelineColumn 380 px (min 280) | splitter (Auto) | map/photos 1* (min 420)
    private static readonly PaneTrack Timeline = new(380, PaneUnit.Pixel, 380, 280, Inf);
    private static readonly PaneTrack RightSide = new(1, PaneUnit.Star, 900, 420, Inf);

    // ReviewPage rows: MapRow 0.45* (min 160) | tuning, splitter (Auto) | ClipRow 0.55* (min 160), 600 px between them
    private static readonly PaneTrack MapRow = new(0.45, PaneUnit.Star, 270, 160, Inf);
    private static readonly PaneTrack ClipRow = new(0.55, PaneUnit.Star, 330, 160, Inf);

    [Fact]
    public void PixelThenStar_Drag_GrowsThePixelPane_AndLeavesTheStarValue()
    {
        var r = SplitterMath.Apply(Timeline, RightSide, 60);
        Assert.Equal(440, r.First);
        Assert.Equal(1, r.Second);
        Assert.Equal(60, r.Applied);
    }

    [Fact]
    public void PixelThenStar_DragLeft_StopsAtTheFirstMinimum()
    {
        var r = SplitterMath.Apply(Timeline, RightSide, -500);
        Assert.Equal(280, r.First);
        Assert.Equal(-100, r.Applied);
    }

    [Fact]
    public void PixelThenStar_DragRight_StopsAtTheSecondMinimum()
    {
        var r = SplitterMath.Apply(Timeline, RightSide, 1000);
        Assert.Equal(380 + 900 - 420, r.First);
        Assert.Equal(1, r.Second);
        Assert.Equal(480, r.Applied);
    }

    [Fact]
    public void FirstMaximum_IsHonoured()
    {
        var r = SplitterMath.Apply(Timeline with { Max = 500 }, RightSide, 1000);
        Assert.Equal(500, r.First);
        Assert.Equal(120, r.Applied);
    }

    [Fact]
    public void SecondMaximum_IsHonoured()
    {
        var r = SplitterMath.Apply(Timeline, RightSide with { Max = 950 }, -500);
        Assert.Equal(330, r.First);                              // the right side may grow by 50 only
        Assert.Equal(-50, r.Applied);
    }

    [Fact]
    public void StarThenStar_Drag_MovesThePixels_AndKeepsTheStarSum()
    {
        var r = SplitterMath.Apply(MapRow, ClipRow, 40);
        Assert.Equal(40, r.Applied);
        Assert.Equal(310.0 / 600, r.First, 1e-12);
        Assert.Equal(290.0 / 600, r.Second, 1e-12);
        Assert.Equal(1.0, r.First + r.Second, 1e-12);
    }

    [Fact]
    public void StarThenStar_StopsAtEitherMinimum()
    {
        var up = SplitterMath.Apply(MapRow, ClipRow, -1000);
        Assert.Equal(-110, up.Applied);
        Assert.Equal(160.0 / 600, up.First, 1e-12);
        var down = SplitterMath.Apply(MapRow, ClipRow, 1000);
        Assert.Equal(170, down.Applied);
        Assert.Equal(160.0 / 600, down.Second, 1e-12);
    }

    [Fact]
    public void StarThenStar_WithAnyStarSum_KeepsTheSum()
    {
        var r = SplitterMath.Apply(MapRow with { Value = 45 }, ClipRow with { Value = 55 }, 30);
        Assert.Equal(100 * 300.0 / 600, r.First, 1e-9);
        Assert.Equal(100 * 300.0 / 600, r.Second, 1e-9);
    }

    [Fact]
    public void StarThenPixel_ChangesOnlyThePixelPane()
    {
        var r = SplitterMath.Apply(new PaneTrack(1, PaneUnit.Star, 700, 0, Inf), new PaneTrack(300, PaneUnit.Pixel, 300, 100, Inf), 40);
        Assert.Equal(1, r.First);
        Assert.Equal(260, r.Second);
        Assert.Equal(40, r.Applied);
    }

    [Fact]
    public void PixelThenPixel_ChangesBoth()
    {
        var r = SplitterMath.Apply(new PaneTrack(200, PaneUnit.Pixel, 200, 0, Inf), new PaneTrack(300, PaneUnit.Pixel, 300, 0, Inf), -50);
        Assert.Equal(150, r.First);
        Assert.Equal(350, r.Second);
        Assert.Equal(-50, r.Applied);
    }

    [Fact]
    public void BasedOnActualSize_NotOnTheRequestedPixelValue()
    {
        // The Grid laid the pixel column out narrower than asked (no room): the drag starts from what the user sees.
        var r = SplitterMath.Apply(Timeline with { Value = 600, Actual = 400 }, RightSide with { Actual = 500 }, 50);
        Assert.Equal(450, r.First);
        Assert.Equal(50, r.Applied);
    }

    [Fact]
    public void MinimumsThatCannotBothBeMet_LeaveTheSizesUnchanged()
    {
        var r = SplitterMath.Apply(Timeline with { Actual = 300 }, RightSide with { Actual = 300 }, 40);
        Assert.Equal(380, r.First);
        Assert.Equal(1, r.Second);
        Assert.Equal(0, r.Applied);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void ZeroOrNonFiniteDelta_ChangesNothing(double delta)
    {
        var r = SplitterMath.Apply(MapRow, ClipRow, delta);
        Assert.Equal(0.45, r.First);
        Assert.Equal(0.55, r.Second);
        Assert.Equal(0, r.Applied);
    }

    [Fact]
    public void NothingLaidOutYet_ChangesNothing()
    {
        var r = SplitterMath.Apply(MapRow with { Actual = 0, Min = 0 }, ClipRow with { Actual = 0, Min = 0 }, 40);
        Assert.Equal(0.45, r.First);
        Assert.Equal(0.55, r.Second);
        Assert.Equal(0, r.Applied);
    }
}
