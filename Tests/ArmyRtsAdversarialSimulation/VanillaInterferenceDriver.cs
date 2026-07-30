namespace ArmyRtsAdversarialSimulation;

using AncientWarfare3.core.lineage;

internal sealed class VanillaInterferenceDriver
{
    private readonly ScenarioState _state;

    public VanillaInterferenceDriver(ScenarioState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public void AttemptWrites()
    {
        _state.LastVanillaWrite = "none";
        _state.VanillaStrategicMovementCommitted = false;
        _state.RtsStrategicMovementCommitted = false;
        if (_state.Kind != ScenarioKind.OwnershipLifecycle) return;

        SimActor[] actors = _state.Actors.Values
            .Where(actor => actor.Alive && actor.Warrior)
            .OrderBy(actor => actor.Id)
            .ToArray();
        if (actors.Length == 0) return;

        if (_state.ActiveTicks % 37 == 0)
        {
            SimActor actor = actors[(_state.ActiveTicks / 37) %
                                    actors.Length];
            int kind = (_state.ActiveTicks / 37) % 4;
            if (kind == 0)
            {
                _state.LastVanillaWrite = "foreign_decision";
                bool allowed = ArmyRtsRuntimeModeRules.
                    ShouldAllowVanillaDecisionEvaluation(
                        ArmyRtsMode.On, rtsOwnsActor: true);
                _state.LastOwnershipDecision = allowed
                    ? "decision_allowed"
                    : "decision_blocked";
                if (allowed)
                {
                    actor.Task = SimTaskClass.ForeignDecision;
                    actor.ForeignTaskAssignedTick = _state.ActiveTicks;
                    _state.Result.AcceptedStrategicWrites++;
                }
                else
                    _state.Result.RejectedStrategicDecisionWrites++;
            }
            else
            {
                actor.Task = kind switch
                {
                    1 => SimTaskClass.Eating,
                    2 => SimTaskClass.Social,
                    _ => SimTaskClass.Training
                };
                actor.ForeignTaskAssignedTick = _state.ActiveTicks;
                _state.LastVanillaWrite = kind switch
                {
                    1 => "eating_task",
                    2 => "social_task",
                    _ => "training_task"
                };
                if (kind == 1) _state.Result.EatingTaskWrites++;
                if (kind == 2) _state.Result.SocialTaskWrites++;
                if (kind == 3) _state.Result.TrainingTaskWrites++;
            }
        }

        if (_state.ActiveTicks % 101 == 0)
        {
            SimArmy army = _state.Armies[21L];
            const long attemptedTargetCityId = 202L;
            if (army.TargetCityId == attemptedTargetCityId)
                throw new InvalidOperationException(
                    "vanilla city target interference was not different");
            bool allowed = ArmyRtsRuntimeModeRules.
                ShouldUseLegacyStrategicWrites(ArmyRtsMode.On);
            _state.LastVanillaWrite = "different_city_target";
            if (allowed)
            {
                army.TargetCityId = attemptedTargetCityId;
                _state.Result.AcceptedStrategicWrites++;
            }
            else
                _state.Result.RejectedCityTargetWrites++;
        }

        if (_state.ActiveTicks % 20 == 0)
        {
            SimArmy army = _state.Armies[21L];
            SimActor captain = _state.Actors[army.CaptainId];
            bool captainMoveAllowed = ArmyRtsRuntimeModeRules.
                ShouldAllowVanillaStrategicDecision(
                    ArmyRtsMode.On,
                    "warrior_army_leader_move_random");
            if (captainMoveAllowed)
            {
                captain.Position += _state.Random.Next(-2, 3);
                _state.VanillaStrategicMovementCommitted = true;
            }
            else
                _state.Result.RejectedCaptainMovementWrites++;

            SimActor follower = actors.First(actor =>
                actor.Id != army.CaptainId);
            bool followerMoveAllowed = ArmyRtsRuntimeModeRules.
                    ShouldUseLegacyArmyFollowerOrders(ArmyRtsMode.On) &&
                ArmyMarchRules.ShouldRunVanillaFollowerSearch(
                    pMarchOwnedByAw3: true);
            if (followerMoveAllowed)
            {
                follower.Position += _state.Random.Next(-2, 3);
                _state.VanillaStrategicMovementCommitted = true;
            }
            else
                _state.Result.RejectedFollowerMovementWrites++;
            _state.LastVanillaWrite = "captain_and_follower_movement";
        }

        if (_state.ActiveTicks % 211 == 0)
        {
            SimActor actor = actors[(_state.ActiveTicks / 211) %
                                    actors.Length];
            long targetId = _state.Runtime.NextActorId++;
            _state.Actors[targetId] = new SimActor
            {
                Id = targetId,
                KingdomId = actor.KingdomId == 1L ? 2L : 1L,
                ArmyId = -1L,
                Position = actor.Position + 1,
                Warrior = true,
                RtsJobActive = false,
                Task = SimTaskClass.ImmediateCombat
            };
            actor.Task = SimTaskClass.ImmediateCombat;
            actor.InImmediateCombat = true;
            actor.AttackTargetId = targetId;
            actor.TemporaryTaskUntilTick = _state.ActiveTicks + 2;
            actor.ForeignTaskAssignedTick = -1;
            _state.LastVanillaWrite = "immediate_combat";
            _state.Result.ImmediateTaskWrites++;
        }

        if (_state.ActiveTicks % 307 == 0)
        {
            SimActor actor = actors[(_state.ActiveTicks / 307) %
                                    actors.Length];
            actor.Task = SimTaskClass.RequiredBoat;
            actor.InsideBoat = true;
            actor.TemporaryTaskUntilTick = _state.ActiveTicks + 3;
            actor.ForeignTaskAssignedTick = -1;
            _state.LastVanillaWrite = "required_boat";
            _state.Result.BoatTaskWrites++;
        }
    }
}
