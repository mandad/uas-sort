using Xunit.Sdk;

namespace UasSort.Core.Tests.Media;

/// <summary>Unwraps the <see cref="GpsProbe"/> union in assertions.</summary>
internal static class ProbeAssert
{
    public static GpsFix Fix(GpsProbe probe) => probe is GpsFix fix ? fix : throw new XunitException("Expected a GpsFix.");

    public static NoFix NoFix(GpsProbe probe) => probe is NoFix none ? none : throw new XunitException("Expected a NoFix.");

    public static void Near(GeoPoint expected, GeoPoint actual, double tolerance = 1e-9)
    {
        Assert.Equal(expected.Lat, actual.Lat, tolerance);
        Assert.Equal(expected.Lon, actual.Lon, tolerance);
    }
}
