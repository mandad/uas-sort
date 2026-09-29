// tests/UasSort.Testing/Planning/PlanScenario.Planner.cs
namespace UasSort.Testing.Planning;

public sealed partial class PlanScenario
{
    public static Planner CreatePlanner(IPlaceIndex? places = null) =>
        new(new GeoTimeZoneResolver(), places, PcZone, new FakeTimeProvider(new DateTimeOffset(NowUtc)));

    public PlanBase Prepare(IPlaceIndex? places = null) => CreatePlanner(places).Prepare(Build());
}
