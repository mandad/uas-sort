// tests/UasSort.Testing/Planning/PlanScenario.Planner.cs
namespace UasSort.Testing.Planning;

public sealed partial class PlanScenario
{
    public static Planner CreatePlanner(IPlaceIndex? places = null) =>
        new(new GeoTimeZoneResolver(), places, PcZone, new FakeTimeProvider(new DateTimeOffset(NowUtc)));

    public PlanBase Prepare(IPlaceIndex? places = null) => CreatePlanner(places).Prepare(Build());

    public static Plan Derive(PlanBase b, Tuning? t = null, IReadOnlyList<PlanEdit>? edits = null, bool ledgerAccepted = false,
                              IPlaceIndex? places = null) =>
        CreatePlanner(places).Derive(b, t ?? new Tuning(), edits ?? [], new SessionFlags(ledgerAccepted), 1, CancellationToken.None);
}
