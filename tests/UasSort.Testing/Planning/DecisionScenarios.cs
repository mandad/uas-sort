// tests/UasSort.Testing/Planning/DecisionScenarios.cs

namespace UasSort.Testing.Planning;

/// <summary>The spike's Decisions fixture: Z = vid 0123, 0124, 0148 at ZACHAR; ZREL = 2026\2026-09\2026-09-27 Zachar Bay.</summary>
public static class DecisionScenarios
{
    public const string Zrel = @"2026\2026-09\2026-09-27 Zachar Bay";

    public static RawItem[] Z =>
    [
        Clip.Vid("20260927140127", 123, Sites.Zachar), Clip.Vid("20260927140144", 124, Sites.Zachar), Clip.Vid("20260927142416", 148, Sites.Zachar),
    ];

    public static PlanScenario ZLibrary() => new PlanScenario().Library(Zrel, Z);

    public static PlanScenario ZWithLedger() =>
        Z.Aggregate(ZLibrary(), (s, z) => s.LedgerFile(z, Zrel, Sites.Zachar, "America/Anchorage"));
}
