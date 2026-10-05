// tests/UasSort.Core.Tests/Cleanup/Photos/ExifStampTests.cs
namespace UasSort.Core.Tests.Cleanup.Photos;

public sealed class ExifStampTests
{
    private static readonly DateTime S = new(2026, 9, 27, 14, 5, 0);

    private static ExifStamp St(string? sub, string model = "FC9113", int second = 0) => new(S.AddSeconds(second), sub, model);

    [Fact]
    public void From_NeedsADtoAndAModel_AndDropsFractionsFromTheSecond()
    {
        var info = new StillInfo(S.AddMilliseconds(300), null, new NoFix(NoFixReason.NoGpsTag), " FC9113 ", null, "3");
        var stamp = ExifStamp.From(info)!;
        Assert.Equal(S, stamp.Second);
        Assert.Equal(("3", "FC9113"), (stamp.SubSec, stamp.Model));
        Assert.Null(ExifStamp.From(info with { DtoNaive = null }));
        Assert.Null(ExifStamp.From(info with { Model = null }));
        Assert.Null(ExifStamp.From(null));
    }

    [Theory]
    [InlineData("5", "500", true)]
    [InlineData("05", "5", false)]
    [InlineData(null, "123", true)]
    [InlineData("123", null, true)]
    [InlineData("1234567", "12345678", true)]
    [InlineData("100", "600", false)]
    public void SameShot_ComparesSubSecondsOnlyWhenBothHaveThem(string? a, string? b, bool expected)
        => Assert.Equal(expected, St(a).SameShot(St(b)));

    [Fact]
    public void SameShot_NeedsTheSameSecondAndModel()
    {
        Assert.False(St(null).SameShot(St(null, second: 1)));
        Assert.False(St(null).SameShot(St(null, model: "FC8282")));
        Assert.True(St(null).SameShot(St(null, model: "fc9113")));
        Assert.True(St("1").SameSecondAndModel(St("9")));
        Assert.True(St("1").BothHaveSubSec(St("9")));
        Assert.False(St(null).BothHaveSubSec(St("9")));
    }
}
