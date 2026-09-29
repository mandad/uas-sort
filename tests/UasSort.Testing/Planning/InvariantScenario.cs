// tests/UasSort.Testing/Planning/InvariantScenario.cs
namespace UasSort.Testing.Planning;

public static class InvariantScenario
{
    public static PlanScenario Build()
    {
        var c117 = Clip.Vid("20260725232655", 117, Sites.Council);
        var c118 = Clip.Vid("20260726022937", 118, Sites.Council);
        return new PlanScenario()
            .Library(@"2026\2026-07\2026-07-25 Council Road", c117, c118)
            .Card(c117, c118,
                  Clip.Vid("20260726235645", 1, Sites.Anvil), Clip.Vid("20260727000012", 2, Sites.Anvil),
                  Clip.Vid("20260727002013", 14, Sites.Anvil, moov: false),
                  Clip.Vid("20260523015251", 40, Sites.KodiakTown), Clip.Vid("20260523201928", 52, Sites.KodiakTown),
                  Clip.Vid("20260524190521", 64, new GeoPoint(57.75, -152.50)), Clip.Vid("20260525092718", 85, Sites.KodiakTown),
                  Clip.Vid("20260927140127", 123, Sites.Zachar), Clip.Vid("20260927140144", 124, Sites.Zachar),
                  Clip.Vid("20260927142416", 148, Sites.Zachar), Clip.Vid("20260927160000", 160, Sites.Zachar),
                  Clip.Vid("20260927161000", 161), Clip.Vid("20260927190000", 150, Sites.KodiakTown),
                  Clip.Dng("20260725233000", 116, Sites.Council), Clip.Dng("20260927140000", 122, Sites.Zachar));
    }
}
