using AncientWarfare3.core.lineage;

namespace ArmyRtsAdversarialSimulation;

internal enum ScenarioKind
{
    OracleProbe,
    LandContinuation,
    LargeLandStress,
    OwnershipLifecycle,
    RouteFailure,
    CrossOceanQueue,
    WarCompletion,
    RallyRecruitment
}

internal enum SimRouteState
{
    None,
    Waiting,
    Ready,
    Failed,
    Arrived
}

internal enum SimTransportState
{
    None,
    Requested,
    BuildAttempted,
    Reserved,
    Loading,
    Sailing,
    Returning,
    Unloading,
    Completed,
    ClassifiedFailure
}

internal enum SimTaskClass
{
    RtsMission,
    RtsFormation,
    ForeignDecision,
    Eating,
    Social,
    Training,
    ImmediateCombat,
    DamageOrDeath,
    RequiredBoat
}

internal enum SimWarCase
{
    None,
    Goals,
    Exhaustion
}

internal sealed class TraceRing
{
    private const int Capacity = 64;
    private readonly Queue<string> _entries = new();

    public int Count => _entries.Count;
    public IReadOnlyList<string> Entries => _entries.ToArray();

    public void Append(string value)
    {
        if (_entries.Count == Capacity) _entries.Dequeue();
        _entries.Enqueue(value ?? "");
    }
}

internal sealed class ScenarioState
{
    public int Seed { get; init; }
    public ScenarioKind Kind { get; init; }
    public int Tick { get; set; }
    public int ActiveTicks { get; set; }
    public bool Paused { get; set; }
    public string LastVanillaWrite { get; set; } = "none";
    public string LastOwnershipDecision { get; set; } = "none";
    public bool VanillaStrategicMovementCommitted { get; set; }
    public bool RtsStrategicMovementCommitted { get; set; }
    public Random Random { get; init; }
    public TraceRing Trace { get; } = new();
    public Dictionary<long, SimArmy> Armies { get; } = new();
    public Dictionary<long, SimActor> Actors { get; } = new();
    public Dictionary<long, SimCity> Cities { get; } = new();
    public Dictionary<long, SimBoat> Boats { get; } = new();
    public SimWar War { get; set; } = new();
    public ScenarioRuntime Runtime { get; } = new();
    public ScenarioResult Result { get; set; } = new();

    public static ScenarioState CreateSmoke(int seed)
    {
        return new ScenarioState
        {
            Seed = seed,
            Random = new Random(seed),
            Kind = ScenarioKind.OracleProbe
        };
    }
}

internal sealed class SimActor
{
    public long Id { get; init; }
    public long ArmyId { get; set; } = -1L;
    public long KingdomId { get; set; } = -1L;
    public bool Alive { get; set; } = true;
    public bool Warrior { get; set; } = true;
    public bool King { get; set; }
    public bool CityLeader { get; set; }
    public bool InImmediateCombat { get; set; }
    public long AttackTargetId { get; set; } = -1L;
    public bool InsideBoat { get; set; }
    public bool RtsJobActive { get; set; } = true;
    public int Position { get; set; }
    public int ForeignTaskAssignedTick { get; set; } = -1;
    public int TemporaryTaskUntilTick { get; set; } = -1;
    public SimTaskClass Task { get; set; } = SimTaskClass.RtsFormation;
}

internal sealed class SimArmy
{
    public long Id { get; init; }
    public long KingdomId { get; init; }
    public long WarId { get; init; }
    public long CaptainId { get; set; }
    public long OriginalValidCaptainId { get; set; }
    public long TargetCityId { get; set; } = -1L;
    public ArmyRtsRole Role { get; set; } = ArmyRtsRole.Assault;
    public ArmyRtsPosture Posture { get; set; } = ArmyRtsPosture.Attack;
    public ArmyRtsState State { get; set; } = ArmyRtsState.Rally;
    public List<long> Members { get; } = new();
    public int TargetStrength { get; set; }
    public int Living { get; set; }
    public int Rallied { get; set; }
    public int Embarked { get; set; }
    public int Landed { get; set; }
    public int Supply { get; set; } = 100;
    public int Organization { get; set; } = 100;
    public int Position { get; set; }
    public SimRouteState RouteState { get; set; }
    public int RouteCursor { get; set; }
    public int RecoveryCount { get; set; }
    public ArmyStallRecoveryAction LastRecovery { get; set; }
    public SimTransportState TransportState { get; set; }
    public long AssignedBoatId { get; set; } = -1L;
    public int TaskRepairCursor { get; set; }
    public int StateTicks { get; set; }
    public int RouteDestinationPosition { get; set; }
    public bool PursuitCompleted { get; set; }
    public bool MissionValid { get; set; } = true;
    public bool SettlementPending { get; set; }
    public bool ReplenishmentRequested { get; set; }
    public TraceRing Trace { get; } = new();
}

internal sealed class SimCity
{
    public long Id { get; init; }
    public long HomeKingdomId { get; init; }
    public long ControllerKingdomId { get; set; }
    public int Island { get; init; }
    public bool EnemyMilitaryPresent { get; set; }
    public bool WarGoal { get; init; }
    public bool Capital { get; init; }
    public bool EligibleTarget { get; set; } = true;
    public int Position { get; init; }
    public int FrontId { get; init; }
    public int Occupation { get; set; }
    public int EnemyMilitaryStrength { get; set; }
    public HashSet<long> ReservedArmyIds { get; } = new();
}

internal sealed class SimBoat
{
    public long Id { get; init; }
    public bool CombatShip { get; init; }
    public int Capacity { get; init; }
    public long ReservedArmyId { get; set; } = -1L;
    public int Island { get; set; }
    public int Position { get; set; }
    public int TotalTrips { get; set; }
}

internal sealed class SimWar
{
    public long Id { get; init; } = 1L;
    public long AttackerKingdomId { get; init; } = 1L;
    public long DefenderKingdomId { get; init; } = 2L;
    public int AgeYears { get; set; }
    public int SignedScore { get; set; }
    public int AttackerExhaustion { get; set; }
    public int DefenderExhaustion { get; set; }
    public int ExpectedGoalCount { get; set; }
    public List<WarGoalSettlementFacts> Goals { get; } = new();
    public bool PeaceQueued { get; set; }
    public int PeaceQueuedActiveTick { get; set; } = -1;
    public bool Settled { get; set; }
    public int SettlementAttempts { get; set; }
}

internal sealed class ScenarioRuntime
{
    public HashSet<ArmyRtsState> VisitedStates { get; } = new();
    public HashSet<long> CompletedCityIds { get; } = new();
    public HashSet<long> DistinctTargetIds { get; } = new();
    public HashSet<long> AssignedArmyIds { get; } = new();
    public Dictionary<long, List<ArmyStallRecoveryAction>>
        RecoveryActionsByArmy { get; } = new();
    public Dictionary<long, ArmyStallWatchdogState> WatchdogsByArmy
        { get; } = new();
    public Dictionary<long, long> TargetCooldownUntil { get; } = new();
    public List<SimTransportRequest> TransportRequests { get; } = new();
    public bool CasualtiesInjected { get; set; }
    public int RoutePhase { get; set; }
    public int BuildFailuresRemaining { get; set; } = 1;
    public long NextBoatId { get; set; } = 1L;
    public SimWarCase WarCase { get; set; }
    public int AttackerLosses { get; set; }
    public int DefenderLosses { get; set; }
    public long NextActorId { get; set; } = 100_000L;
    public long RallyRecruitmentOriginalArmyId { get; set; } = -1L;
    public long RallyRecruitmentReplacementArmyId { get; set; } = -1L;
    public bool RallyRecruitmentPartialLossInjected { get; set; }
    public bool RallyRecruitmentFullWipeInjected { get; set; }
    public bool RallyRecruitmentReplacementCreated { get; set; }
    public bool RallyRecruitmentFirstAssaultObserved { get; set; }
}

internal sealed class SimTransportRequest
{
    public long ArmyId { get; init; }
    public long TargetCityId { get; init; }
    public int RequestedTick { get; init; }
    public int LastBuildAttemptTick { get; set; } = -1;
    public int AssignedTick { get; set; } = -1;
    public long AssignedBoatId { get; set; } = -1L;
    public SimTransportState State { get; set; } =
        SimTransportState.Requested;
    public int TripCount { get; set; }
    public string LastOutcome { get; set; } = "requested";
    public HashSet<long> LandedActorIds { get; } = new();
    public List<long> EmbarkedActorIds { get; } = new();
}

internal sealed class ScenarioResult
{
    public string Name { get; set; } = "";
    public int Seed { get; set; }
    public int Ticks { get; set; }
    public bool ValidSettlement { get; set; }
    public int CompletedObjectives { get; set; }
    public int RecoveryActions { get; set; }
    public int DistinctTargetAssignments { get; set; }
    public int DistinctArmyAssignments { get; set; }
    public int RepeatedOccupiedTargets { get; set; }
    public int AcceptedStrategicWrites { get; set; }
    public int RejectedStrategicDecisionWrites { get; set; }
    public int RepairedForeignTasks { get; set; }
    public int EatingTaskWrites { get; set; }
    public int SocialTaskWrites { get; set; }
    public int ImmediateTaskWrites { get; set; }
    public int BoatTaskWrites { get; set; }
    public int ImmediateTaskOverwrites { get; set; }
    public int StaleCombatTaskRepairs { get; set; }
    public int BoatTaskOverwrites { get; set; }
    public int TrainingTaskWrites { get; set; }
    public int RejectedCityTargetWrites { get; set; }
    public int RejectedCaptainMovementWrites { get; set; }
    public int RejectedFollowerMovementWrites { get; set; }
    public int MovementOwnershipConflicts { get; set; }
    public int CaptainReplacements { get; set; }
    public int RejectedValidCaptainReplacements { get; set; }
    public int EmptyShells { get; set; }
    public string RouteRecoverySequence { get; set; } = "";
    public int RepeatedRecoveryCycles { get; set; }
    public int TransportBuildAttempts { get; set; }
    public int CombatShipTransportAssignments { get; set; }
    public int StrandedMembers { get; set; }
    public int ReusedBoatTrips { get; set; }
    public int NavalPreemptions { get; set; }
    public int Teleports { get; set; }
    public int OffensiveMissionsAfterPeace { get; set; }
    public int SettlementAttempts { get; set; }
    public int InitialAttackingStrength { get; set; }
    public int InitialDefendingStrength { get; set; }
    public int RallyQuorumTick { get; set; } = -1;
    public int FirstMarchTick { get; set; } = -1;
    public int CommanderDeparturesBeforeRallyQuorum { get; set; }
    public int RalliedMembersAtFirstMarch { get; set; }
    public int UnrejoinedMembersAtFirstAssault { get; set; } = -1;
    public int PartialBattleLosses { get; set; }
    public int PartialBattleRecruits { get; set; }
    public int ReplacementArmiesCreated { get; set; }
    public int ReplacementArmyStrength { get; set; }
    public string VisitedStates { get; set; } = "";
    public string FinalStateSummary { get; set; } = "";
}
