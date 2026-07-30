namespace ArmyRtsAdversarialSimulation;

internal sealed class SimulationEngine
{
    private readonly ScenarioState _state;
    private readonly ProgressOracle _oracle;
    private readonly VanillaInterferenceDriver _vanilla;

    public SimulationEngine(ScenarioState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
        _oracle = new ProgressOracle(state);
        _vanilla = new VanillaInterferenceDriver(state);
    }

    public List<string> LastStageOrder { get; } = new();

    public void Step()
    {
        _state.Tick++;
        LastStageOrder.Clear();
        if (_state.Paused) return;

        _state.ActiveTicks++;
        Stage("events", () => ScenarioFactory.ApplyEvents(_state));
        Stage("vanilla", _vanilla.AttemptWrites);
        Stage("ownership", () => ScenarioFactory.ApplyOwnership(_state));
        Stage("strategy", () => ScenarioFactory.ApplyStrategy(_state));
        Stage("movement", () => ScenarioFactory.AdvanceWorld(_state));
        Stage("watchdog", _oracle.SampleDeadlines);
        Stage("invariants", _oracle.AssertHardInvariants);
        Stage("trace", _oracle.AppendChangedState);
    }

    private void Stage(string name, Action action)
    {
        LastStageOrder.Add(name);
        action();
    }
}
