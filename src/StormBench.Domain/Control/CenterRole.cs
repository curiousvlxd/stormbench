using StormBench.Domain.Scenarios;

namespace StormBench.Domain.Control;

/// <summary>Who proposes in mode 3: Scada's own optimiser, unless the console runs an algorithm of another center.</summary>
public static class CenterRole
{
    /// <summary>
    /// True while the simulated field runs a storm with an algorithm whose proposals another center
    /// makes. Without a console run (a plain mode-3 run, or the console idle) Scada is the center.
    /// </summary>
    public static bool AnotherCenterProposes(ScenarioState state, AlgorithmId? algorithm, ScenarioCatalog? catalog) =>
        state.IsBusy
        && catalog?.Algorithm(algorithm)?.Center is { } center
        && center is not ControlCenter.Scada;
}
