using System;
using System.Collections.Generic;
using AncientWarfare3.core.performance;

namespace AncientWarfare3.core.lineage
{
    internal static class ArmyStallWatchdogService
    {
        private const int MaximumFollowerRecoverySamplesPerArmy = 6;

        private sealed class RuntimeState
        {
            internal readonly ArmyWatchdogRecoveryFlow Flow =
                new ArmyWatchdogRecoveryFlow();
            internal readonly ArmyFollowerStallRecoveryIndex
                FollowerRecovery = new ArmyFollowerStallRecoveryIndex();
            internal readonly List<ArmyFollowerStallSample>
                FollowerSamples = new List<ArmyFollowerStallSample>(
                    MaximumFollowerRecoverySamplesPerArmy);
            internal int FollowerSampleCursor;
            internal double LastX;
            internal double LastY;
            internal bool HasPosition;
            internal long LastPositionActorId = -1L;
            internal ArmyWatchdogPositionSource LastPositionSource;
        }

        private static readonly Dictionary<long, RuntimeState> StateByArmy =
            new Dictionary<long, RuntimeState>();
        private static readonly SortedSet<long> ActiveArmyIds =
            new SortedSet<long>();
        private static readonly ArmyTargetCooldownIndex TargetCooldowns =
            new ArmyTargetCooldownIndex();
        private static readonly Dictionary<(long KingdomId,
                long TargetCityId), long> WarByCooledTarget =
            new Dictionary<(long KingdomId, long TargetCityId), long>();
        private static IReadOnlyList<long> _sampleArmyIds =
            Array.Empty<long>();
        private static int _sampleCursor;
        private static readonly ArmyWatchdogSamplingClock SamplingClock =
            new ArmyWatchdogSamplingClock();
        private static readonly ArmyRecoveryDiagnosticGate DiagnosticGate =
            new ArmyRecoveryDiagnosticGate(2d);

        public static void OnMissionAssigned(Army pArmy,
            bool pResetState)
        {
            if (ArmyRtsWarDoctrine.IsAbstractDecisive ||
                !ArmyRtsRuntimeMode.ShouldCommit || pArmy?.data == null)
                return;
            ActiveArmyIds.Add(pArmy.id);
            if (pResetState || !StateByArmy.ContainsKey(pArmy.id))
                StateByArmy[pArmy.id] = new RuntimeState();
            StateByArmy[pArmy.id].Flow.AssignMission();
        }

        public static void OnArmyInvalidated(long pArmyId)
        {
            ActiveArmyIds.Remove(pArmyId);
            StateByArmy.Remove(pArmyId);
            DiagnosticGate.RemoveArmy(pArmyId);
        }

        public static bool IsRegistered(long pArmyId)
        {
            return ActiveArmyIds.Contains(pArmyId) &&
                   StateByArmy.ContainsKey(pArmyId);
        }

        public static bool IsTargetCoolingDown(long pKingdomId,
            long pTargetCityId)
        {
            return TargetCooldowns.IsCoolingDown(pKingdomId,
                pTargetCityId, CurrentWorldDay());
        }

        public static void OnRouteFailed(long pArmyId,
            bool pAllowTransportEscalation = true)
        {
            if (ArmyRtsWarDoctrine.IsAbstractDecisive) return;
            if (!StateByArmy.TryGetValue(pArmyId,
                    out RuntimeState state)) return;
            if (pAllowTransportEscalation &&
                ArmyRtsControllerService.
                    TryBeginCrossIslandTransportAfterRouteFailure(pArmyId))
            {
                ArmyRtsBenchmark.RecordReplan();
                return;
            }
            ArmyStallRecoveryAction action = state.Flow.RecordRouteFailure();
            HandleRecoveryAction(pArmyId, state, action);
        }

        public static void OnWarEnded(War pWar)
        {
            if (pWar?.data == null) return;
            var expired = new List<(long KingdomId, long TargetCityId)>();
            foreach (KeyValuePair<(long KingdomId, long TargetCityId), long>
                         pair in WarByCooledTarget)
                if (pair.Value == pWar.data.id) expired.Add(pair.Key);
            for (int i = 0; i < expired.Count; i++)
            {
                TargetCooldowns.Remove(expired[i].KingdomId,
                    expired[i].TargetCityId);
                WarByCooledTarget.Remove(expired[i]);
            }
        }

        public static void ProcessFrame()
        {
            ProcessFrame(pMaximumArmies: -1, pForceSample: false);
        }

        public static int PendingArmyCount => ActiveArmyIds.Count;

        public static void ProcessFrame(int pMaximumArmies,
            bool pForceSample)
        {
            if (ArmyRtsWarDoctrine.IsAbstractDecisive ||
                !ArmyRtsRuntimeMode.ShouldCommit || World.world == null)
                return;
            double now = CurrentRealtime();
            bool paused = World.world.isPaused();
            bool startSample = SamplingClock.TryStartSample(now, paused);
            if (paused)
            {
                ResetSampleBatch();
                return;
            }
            if (SamplingClock.ResumedThisUpdate)
            {
                ResetSampleBatch();
                return;
            }
            if (pForceSample)
            {
                var forcedSnapshot = new long[ActiveArmyIds.Count];
                ActiveArmyIds.CopyTo(forcedSnapshot);
                _sampleArmyIds = forcedSnapshot;
                _sampleCursor = 0;
            }
            else if (_sampleCursor >= _sampleArmyIds.Count)
            {
                if (!startSample) return;
                var snapshot = new long[ActiveArmyIds.Count];
                ActiveArmyIds.CopyTo(snapshot);
                _sampleArmyIds = snapshot;
                _sampleCursor = 0;
            }

            int pending = _sampleArmyIds.Count - _sampleCursor;
            int sampleCount = pMaximumArmies < 0
                ? RuntimePerformanceBudgetRules.
                    ResolveWatchdogArmiesPerFrame(pending)
                : Math.Min(Math.Max(0, pMaximumArmies), pending);
            int end = Math.Min(_sampleArmyIds.Count,
                _sampleCursor + sampleCount);
            for (; _sampleCursor < end; _sampleCursor++)
                SampleArmy(_sampleArmyIds[_sampleCursor]);
        }

        public static void RebuildRuntime()
        {
            ClearRuntime();
        }

        public static void ClearRuntime()
        {
            StateByArmy.Clear();
            ActiveArmyIds.Clear();
            TargetCooldowns.Clear();
            WarByCooledTarget.Clear();
            _sampleArmyIds = Array.Empty<long>();
            _sampleCursor = 0;
            SamplingClock.Clear();
            DiagnosticGate.Clear();
        }

        private static void SampleArmy(long pArmyId)
        {
            if (!StateByArmy.TryGetValue(pArmyId,
                    out RuntimeState state))
            {
                state = new RuntimeState();
                state.Flow.AssignMission();
                StateByArmy[pArmyId] = state;
            }
            if (!ArmyRtsControllerService.TryGetWatchdogSample(
                    pArmyId, out ArmyWatchdogControllerSample sample))
            {
                bool missionValid = ArmyRtsControllerService.
                    HasActiveMission(pArmyId);
                state.Flow.ObserveUnavailable(missionValid);
                if (!ArmyRtsControllerService.HasActiveMission(pArmyId))
                    OnArmyInvalidated(pArmyId);
                return;
            }
            SampleFollowerRecovery(pArmyId, state, CurrentRealtime());
            bool hadPosition = state.HasPosition &&
                state.LastPositionActorId == sample.PositionActorId &&
                state.LastPositionSource == sample.PositionSource;
            int previousRouteCursor = state.Flow.StallState.LastRouteCursor;
            double movement = hadPosition
                ? Distance(state.LastX, state.LastY, sample.PositionX,
                    sample.PositionY)
                : 0d;
            state.LastX = sample.PositionX;
            state.LastY = sample.PositionY;
            state.HasPosition = true;
            state.LastPositionActorId = sample.PositionActorId;
            state.LastPositionSource = sample.PositionSource;
            if (sample.TransportOwned)
            {
                state.Flow.SuspendForExternalOwnership();
                return;
            }
            if (!sample.CombatActive && sample.PositionActorId >= 0L &&
                Enum.TryParse(sample.LocalPathStatus,
                    out ArmySharedRouteInstallStatus localPathStatus) &&
                ArmySharedPathRules.ShouldRecoverStaleInstalledRoute(
                    localPathStatus, combatActive: false,
                    transportActive: false) &&
                ArmyRtsControllerService.RecoverEmptySharedRoute(pArmyId,
                    sample.PositionActorId))
            {
                state.Flow.SuspendForExternalOwnership();
                return;
            }
            if (hadPosition &&
                !sample.CombatActive &&
                (sample.RouteReady || sample.RoutePending ||
                 sample.CommandExpected) &&
                previousRouteCursor != int.MinValue &&
                previousRouteCursor == sample.RouteCursor &&
                movement < ArmyStallWatchdogRules.MinimumProgressTiles)
                ArmyRtsBenchmark.RecordNoProgressSeconds(
                    ArmyStallWatchdogRules.SampleIntervalSeconds);
            ArmyStallRecoveryAction action = state.Flow.ObserveSample(
                true, sample.PositionSource, movement,
                sample.RouteCursor, sample.RouteReady,
                routePending: sample.RoutePending,
                commandExpected: sample.CommandExpected,
                commandOwned: sample.CommandOwned,
                combatActive: sample.CombatActive,
                objectiveOpen: sample.ObjectiveOpen,
                requiresTransport: sample.RequiresTransport,
                objectiveProgressExpected: sample.ObjectiveProgressExpected,
                objectiveProgress: sample.ObjectiveProgress);
            HandleRecoveryAction(pArmyId, state, action, sample);
        }

        private static void SampleFollowerRecovery(long pArmyId,
            RuntimeState pState, double pRealtime)
        {
            if (pState == null) return;
            int maximumFollowers = RuntimePerformanceBudgetRules.
                ResolveFollowerChecksPerArmy(
                    MaximumFollowerRecoverySamplesPerArmy);
            int sampled = ArmyRtsControllerService.
                CollectFollowerStallSamples(pArmyId,
                    pState.FollowerSampleCursor,
                    maximumFollowers,
                    pState.FollowerSamples,
                    out int nextCursor);
            pState.FollowerSampleCursor = nextCursor;
            for (int i = 0; i < sampled; i++)
            {
                ArmyFollowerStallSample sample = pState.FollowerSamples[i];
                ArmyFollowerStallRecoveryAction action =
                    pState.FollowerRecovery.Observe(sample.ActorId,
                        sample.PositionX, sample.PositionY,
                        sample.RecoveryEligible, sample.CombatActive,
                        sample.TransportActive, pRealtime);
                LogFollowerRecoveryAction(pArmyId, sample, action);
                if (action == ArmyFollowerStallRecoveryAction.ResetRoute)
                {
                    ArmyRtsControllerService.RecoverFormationMember(pArmyId,
                        sample.ActorId);
                    continue;
                }
                if (action == ArmyFollowerStallRecoveryAction.AlternateSlot)
                {
                    ArmyRtsControllerService.RecoverFormationMember(pArmyId,
                        sample.ActorId, pPreferAlternateSlot: true);
                    continue;
                }
                if (action !=
                    ArmyFollowerStallRecoveryAction.TeleportToCaptain)
                    continue;
                if (!ArmyRtsControllerService.TryTeleportFormationMember(
                        pArmyId, sample.ActorId))
                    ArmyRtsControllerService.RecoverFormationMember(pArmyId,
                        sample.ActorId);
                pState.FollowerRecovery.Remove(sample.ActorId);
            }
        }

        private static void LogFollowerRecoveryAction(long pArmyId,
            ArmyFollowerStallSample pSample,
            ArmyFollowerStallRecoveryAction pAction)
        {
            if (!AWPerformanceSettings.ArmyRtsDiagnosticsEnabled ||
                pAction == ArmyFollowerStallRecoveryAction.None) return;
            AncientWarfare3.ModClass.LogWarning(
                "[Army RTS follower recovery] army=" + pArmyId +
                " actor=" + (pSample?.ActorId ?? -1L) +
                " follower_recovery_action=" + pAction +
                " combat_active=" + (pSample?.CombatActive ?? false) +
                " transport_active=" +
                (pSample?.TransportActive ?? false));
        }

        private static void ResetSampleBatch()
        {
            _sampleArmyIds = Array.Empty<long>();
            _sampleCursor = 0;
        }

        private static void HandleRecoveryAction(long pArmyId,
            RuntimeState pState, ArmyStallRecoveryAction pAction,
            ArmyWatchdogControllerSample pSample = null)
        {
            if (pAction == ArmyStallRecoveryAction.None) return;
            if (pSample == null)
                ArmyRtsControllerService.TryGetWatchdogSample(pArmyId,
                    out pSample);
            LogRecoveryAction(pArmyId, pState, pAction, pSample);
            if (pSample != null && ArmyStallWatchdogRules.
                    ShouldUseMemberRecovery(pSample.PositionSource,
                        pAction, pSample.ObjectiveOpen))
            {
                ArmyRtsControllerService.RecoverFormationMember(pArmyId,
                    pSample.PositionActorId);
                pState.Flow.SuspendForExternalOwnership();
                return;
            }
            if (pAction == ArmyStallRecoveryAction.ReassertCommand)
            {
                ArmyRtsControllerService.ReassertMissionCommand(pArmyId,
                    pSample?.PositionActorId ?? -1L);
                return;
            }
            if (pAction == ArmyStallRecoveryAction.ChangeTarget)
            {
                TryCoolDownAndHandoff(pSample, pArmyId,
                    ArmyRtsMissionReleaseCause.TargetInvalid);
                return;
            }
            if (pAction == ArmyStallRecoveryAction.EnterTransport)
            {
                if (ArmyRtsControllerService.RequestTransportRecovery(
                        pArmyId))
                {
                    ArmyRtsBenchmark.RecordReplan();
                    return;
                }
                TryCoolDownAndHandoff(pSample, pArmyId,
                    ArmyRtsMissionReleaseCause.PathFailed);
                return;
            }
            if (pAction == ArmyStallRecoveryAction.Retreat)
            {
                if (pSample != null) CoolDownAndRetreat(pSample);
                else RetreatArmy(pArmyId);
                return;
            }
            bool alternate = ArmyStallWatchdogRules.
                ShouldUseAlternateEndpoint(pAction,
                    pSample?.CommandExpected == true,
                    pSample?.CommandOwned == true);
            if (ArmyRtsControllerService.RequestRouteReplan(pArmyId,
                    alternate))
            {
                ArmyRtsBenchmark.RecordReplan();
                return;
            }
            ArmyStallRecoveryAction failed =
                pState.Flow.RecordRouteFailure();
            LogRecoveryAction(pArmyId, pState, failed, pSample);
            if (failed == ArmyStallRecoveryAction.ChangeTarget)
            {
                TryCoolDownAndHandoff(pSample, pArmyId,
                    ArmyRtsMissionReleaseCause.PathFailed);
                return;
            }
            if (failed == ArmyStallRecoveryAction.Retreat)
            {
                if (pSample != null) CoolDownAndRetreat(pSample);
                else RetreatArmy(pArmyId);
            }
        }

        private static bool TryCoolDownAndHandoff(
            ArmyWatchdogControllerSample pSample, long pArmyId,
            ArmyRtsMissionReleaseCause pCause)
        {
            if (!ArmyRtsMissionLockRules.CanHandoffAfterRecovery(pCause,
                    pSample?.ObjectiveOpen == true))
                return false;
            if (pSample != null)
            {
                TargetCooldowns.CoolDown(pSample.KingdomId,
                    pSample.TargetCityId, CurrentWorldDay());
                WarByCooledTarget[(pSample.KingdomId,
                    pSample.TargetCityId)] = pSample.WarId;
            }
            return ArmyRtsControllerService.RequestObjectiveHandoff(pArmyId);
        }

        private static void LogRecoveryAction(long pArmyId,
            RuntimeState pState, ArmyStallRecoveryAction pAction,
            ArmyWatchdogControllerSample pSample)
        {
            if (!AWPerformanceSettings.ArmyRtsDiagnosticsEnabled) return;
            long targetCityId = pSample?.TargetCityId ?? -1L;
            if (!DiagnosticGate.ShouldLog(pArmyId, pAction, targetCityId,
                    CurrentRealtime())) return;
            Army army = FindArmy(pArmyId);
            Actor captain = null;
            try { captain = army?.getCaptain(); }
            catch { }
            ArmyFormationObservationProgress formationProgress =
                ArmyFormationService.GetObservationProgress(army);
            ArmyStallWatchdogState stall = pState?.Flow?.StallState;
            AncientWarfare3.ModClass.LogWarning(
                "[Army RTS stall recovery] army=" + pArmyId +
                " captain=" + (captain?.data?.id ?? -1L) +
                " war=" + (pSample?.WarId ?? -1L) +
                " target_city=" + targetCityId +
                " action=" + pAction +
                " position_source=" +
                (pSample?.PositionSource.ToString() ?? "None") +
                " position_actor=" +
                (pSample?.PositionActorId ?? -1L) +
                " formation_living=" +
                (pSample?.FormationLiving ?? 0) +
                " formation_rallied=" +
                (pSample?.FormationRallied ?? 0) +
                " no_progress_seconds=" +
                (stall?.NoProgressSeconds ?? 0d) +
                " route_cursor=" + (pSample?.RouteCursor ?? -1) +
                " route_ready=" + (pSample?.RouteReady ?? false) +
                " route_pending=" + (pSample?.RoutePending ?? false) +
                " command_expected=" +
                (pSample?.CommandExpected ?? false) +
                " command_owned=" + (pSample?.CommandOwned ?? false) +
                " objective_open=" + (pSample?.ObjectiveOpen ?? false) +
                " objective_progress_expected=" +
                (pSample?.ObjectiveProgressExpected ?? false) +
                " objective_progress=" +
                (pSample?.ObjectiveProgress ?? 0d) +
                " transport_required=" +
                (pSample?.RequiresTransport ?? false) +
                " transport_owned=" +
                (pSample?.TransportOwned ?? false) +
                " state=" + (pSample?.State.ToString() ?? "Idle") +
                " role=" + (pSample?.Role.ToString() ?? "Reserve") +
                " director_force_ready=" +
                (pSample?.DirectorForceReady ?? false) +
                " minimum_force_ready=" +
                (pSample?.MinimumForceReady ?? false) +
                " departure_ready=" +
                (pSample?.DepartureReady ?? false) +
                " target_strength=" + (pSample?.TargetStrength ?? 0) +
                " roster_living=" + (pSample?.RosterLiving ?? 0) +
                " supply=" + (pSample?.Supply ?? 0) +
                " organization=" + (pSample?.Organization ?? 0) +
                " route_submitted=" +
                (pSample?.RouteSubmitted ?? false) +
                " route_arrived=" + (pSample?.RouteArrived ?? false) +
                " formation_observed=" +
                (pSample?.FormationObserved ?? false) +
                " formation_observation_pending=" +
                (!formationProgress.Complete) +
                " formation_scan_members=" +
                formationProgress.MemberCount +
                " formation_scan_cursor=" + formationProgress.Cursor +
                " formation_scan_restarts=" +
                formationProgress.RestartCount +
                " replenishment_bypass=" +
                (pSample?.ReplenishmentBypass ?? false) +
                " local_path_status=" +
                (pSample?.LocalPathStatus ?? "Unavailable") +
                " local_path_count=" +
                (pSample?.LocalPathCount ?? 0) +
                " local_path_index=" +
                (pSample?.LocalPathIndex ?? 0) +
                " local_path_following=" +
                (pSample?.LocalPathFollowing ?? false) +
                " local_path_moving=" +
                (pSample?.LocalPathMoving ?? false) +
                " local_target=" +
                (pSample?.LocalTargetTileId ?? -1));
        }

        private static void RetreatArmy(long pArmyId)
        {
            if (!ArmyRtsControllerService.TryGetWatchdogSample(
                    pArmyId, out ArmyWatchdogControllerSample sample))
                return;
            CoolDownAndRetreat(sample);
        }

        private static void CoolDownAndRetreat(
            ArmyWatchdogControllerSample pSample)
        {
            Army army = FindArmy(pSample.ArmyId);
            bool playerCommand = ArmyRtsControllerService.TryGetMission(
                army, out ArmyRtsMission mission) &&
                ArmyRtsWarDoctrineRules.IsExplicitPlayerRetreat(mission);
            if (!ArmyRtsWarDoctrineRules.AllowWithdrawal(
                    ArmyRtsWarDoctrine.Current,
                    ArmyRtsWithdrawalOrigin.Watchdog, playerCommand))
                return;
            ArmyRtsControllerService.MarkRouteImpossible(pSample.ArmyId);
            TargetCooldowns.CoolDown(pSample.KingdomId,
                pSample.TargetCityId, CurrentWorldDay());
            WarByCooledTarget[(pSample.KingdomId,
                pSample.TargetCityId)] = pSample.WarId;
            ArmyRetreatService.AssignArmyRetreat(army,
                pSample.TargetCityId, ArmyRtsWithdrawalOrigin.Watchdog);
        }

        private static double Distance(double pFirstX, double pFirstY,
            double pSecondX, double pSecondY)
        {
            double x = pSecondX - pFirstX;
            double y = pSecondY - pFirstY;
            return Math.Sqrt(x * x + y * y);
        }

        private static Army FindArmy(long pArmyId)
        {
            try { return World.world?.armies?.get(pArmyId); }
            catch { return null; }
        }

        private static double CurrentRealtime()
        {
            try { return UnityEngine.Time.realtimeSinceStartupAsDouble; }
            catch { return 0d; }
        }

        private static long CurrentWorldDay()
        {
            try
            {
                double time = Math.Max(0d,
                    World.world?.getCurWorldTime() ?? 0d);
                double days = Math.Floor(time * 6d);
                return days >= long.MaxValue ? long.MaxValue : (long)days;
            }
            catch { return 0L; }
        }
    }
}
