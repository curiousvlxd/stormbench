namespace StormBench.Domain.Scenarios;

/// <summary>Where the simulator is with the storm of the test console.</summary>
public enum ScenarioState
{
    Idle,

    /// <summary>Computing the Base Case of the storm window as the reference.</summary>
    Preparing,
    Running,
    Finished,
    Stopped,
    Failed
}

public static class ScenarioStateExtensions
{
    extension(ScenarioState state)
    {
        /// <summary>A storm is being prepared or run: the console takes no second one.</summary>
        public bool IsBusy => state is ScenarioState.Preparing or ScenarioState.Running;

        /// <summary>The storm is over, completed or not.</summary>
        public bool HasEnded => state is ScenarioState.Finished or ScenarioState.Stopped or ScenarioState.Failed;
    }
}
