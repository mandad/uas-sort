namespace UasSort.Core;

/// <summary>The planning seam: PlanSession derives through it; Planner implements it; tests inject GatedPlanDeriver.</summary>
public interface IPlanDeriver
{
    Plan Derive(PlanBase b, Tuning t, IReadOnlyList<PlanEdit> edits, SessionFlags flags, int revision, CancellationToken ct);
}
