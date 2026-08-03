using System;

namespace AncientWarfare3.core.lineage
{
    public sealed class ArmyRtsProgressDeadline
    {
        private bool _active;
        private int _bestProgress;
        private double _lastProgressRealtime;
        private double _lastRealtime;

        public bool Observe(bool active, int progress, double realtime,
            double timeoutSeconds)
        {
            double now = NormalizeRealtime(realtime);
            if (!active)
            {
                Reset();
                _lastRealtime = now;
                return false;
            }

            int current = Math.Max(0, progress);
            if (!_active || current > _bestProgress)
            {
                _active = true;
                _bestProgress = current;
                _lastProgressRealtime = now;
                return false;
            }
            return now - _lastProgressRealtime >=
                   Math.Max(0d, timeoutSeconds);
        }

        public void Reset()
        {
            _active = false;
            _bestProgress = 0;
            _lastProgressRealtime = 0d;
        }

        private double NormalizeRealtime(double realtime)
        {
            double now = double.IsNaN(realtime) ||
                         double.IsInfinity(realtime)
                ? _lastRealtime
                : Math.Max(0d, realtime);
            if (now < _lastRealtime) now = _lastRealtime;
            _lastRealtime = now;
            return now;
        }
    }

    public sealed class ArmyRtsRegroupRecoveryDeadline
    {
        private bool _active;
        private int _bestOrganization;
        private int _bestSupply;
        private double _lastProgressRealtime;
        private double _lastRealtime;

        public bool Observe(bool active, int organization, int supply,
            double realtime, double timeoutSeconds)
        {
            double now = NormalizeRealtime(realtime);
            if (!active)
            {
                Reset();
                _lastRealtime = now;
                return false;
            }

            int currentOrganization = Math.Max(0,
                Math.Min(100, organization));
            int currentSupply = Math.Max(0, Math.Min(100, supply));
            if (!_active || currentOrganization > _bestOrganization ||
                currentSupply > _bestSupply)
            {
                _active = true;
                _bestOrganization = Math.Max(_bestOrganization,
                    currentOrganization);
                _bestSupply = Math.Max(_bestSupply, currentSupply);
                _lastProgressRealtime = now;
                return false;
            }
            return now - _lastProgressRealtime >=
                   Math.Max(0d, timeoutSeconds);
        }

        public void Reset()
        {
            _active = false;
            _bestOrganization = 0;
            _bestSupply = 0;
            _lastProgressRealtime = 0d;
        }

        private double NormalizeRealtime(double realtime)
        {
            double now = double.IsNaN(realtime) ||
                         double.IsInfinity(realtime)
                ? _lastRealtime
                : Math.Max(0d, realtime);
            if (now < _lastRealtime) now = _lastRealtime;
            _lastRealtime = now;
            return now;
        }
    }

    public sealed class ArmyRtsReplenishmentBypassLatch
    {
        public bool Active { get; private set; }

        public bool Update(bool replenishmentWindow,
            bool needsReplenishment, bool minimumForceReady,
            bool bypassTriggered)
        {
            if (!replenishmentWindow || !needsReplenishment ||
                !minimumForceReady)
            {
                Active = false;
                return false;
            }
            if (bypassTriggered) Active = true;
            return Active;
        }
    }

    public static class ArmyRtsRules
    {
        public const int MaximumFieldArmiesPerKingdom = 12;
        public const int OrdinaryAssaultReservationCap = 3;
        public const int StrategicAssaultReservationCap = 4;
        public const int DeploymentQuorumPercent = 80;
        public const int RetreatOrganization =
            ArmyOperationalThresholds.RetreatOrganization;
        public const int RegroupOrganization =
            ArmyOperationalThresholds.RegroupOrganization;
        public const int LowSupply = ArmyOperationalThresholds.LowSupply;
        public const int CriticalSupply =
            ArmyOperationalThresholds.CriticalSupply;
        public const int CatastrophicLossPercent = 70;
        public const double ReadinessStallTimeoutSeconds = 6d;
        public const double MaximumPreDepartureWaitWorldSeconds = 120d;
        public const double JobOwnershipRepairIntervalSeconds = 12d;

        public static bool ShouldReopenJobOwnershipRepair(
            bool jobsInitialized, double currentWorldTime,
            double nextRepairWorldTime)
        {
            if (!jobsInitialized || double.IsNaN(currentWorldTime) ||
                double.IsInfinity(currentWorldTime)) return false;
            return currentWorldTime >= nextRepairWorldTime;
        }

        public static int DesiredFieldArmyCount(int pCityCount,
            int pActiveFrontCount)
        {
            int cities = Math.Max(0, pCityCount);
            int fronts = Math.Max(0, pActiveFrontCount);
            int desired = 1 + cities / 4 + Math.Max(0, fronts - 1);
            return Math.Max(1, Math.Min(MaximumFieldArmiesPerKingdom,
                desired));
        }

        public static int AssaultReservationCap(bool pIsCapital,
            bool pIsWarGoal)
        {
            return pIsCapital || pIsWarGoal
                ? StrategicAssaultReservationCap
                : OrdinaryAssaultReservationCap;
        }

        public static bool HasDeploymentQuorum(int pReady, int pLiving)
        {
            if (pLiving <= 0 || pReady < 0) return false;
            return (long)Math.Min(pReady, pLiving) * 100L >=
                   (long)pLiving * DeploymentQuorumPercent;
        }

        public static int ResolveMissionTargetStrength(
            int persistedTargetStrength, int anchorTargetStrength,
            int rosterLiving)
        {
            return Math.Max(ArmyLogisticsRules.MinimumOperationalForce,
                Math.Max(Math.Max(0, persistedTargetStrength),
                    Math.Max(Math.Max(0, anchorTargetStrength),
                        Math.Max(0, rosterLiving))));
        }

        public static bool NeedsReplenishment(int living,
            int targetStrength)
        {
            int target = Math.Max(0, targetStrength);
            if (target == 0) return false;
            return (long)Math.Max(0, living) * 100L <
                   (long)target * DeploymentQuorumPercent;
        }

        public static bool ShouldContinueRequestedReplenishment(
            bool replenishmentRequested, int currentStrength,
            int targetStrength)
        {
            if (!replenishmentRequested) return false;
            return Math.Max(0, currentStrength) <
                   Math.Max(0, targetStrength);
        }

        public static bool ShouldContinueRequestedReplenishment(
            bool replenishmentRequested, int currentStrength,
            int targetStrength, bool reserveAvailable)
        {
            return reserveAvailable &&
                   ShouldContinueRequestedReplenishment(
                       replenishmentRequested, currentStrength,
                       targetStrength);
        }

        public static bool HasDepartureStrength(int living,
            int targetStrength, bool minimumForceReady,
            bool replenishmentBypassActive)
        {
            if (!minimumForceReady) return false;
            // A stalled recruitment request may unblock route and formation
            // recovery, but it must never authorize an understrength assault.
            return !NeedsReplenishment(living, targetStrength);
        }

        public static bool ShouldRemainInReplenishment(
            bool needsReplenishment, bool departureStrengthReady)
        {
            return needsReplenishment || !departureStrengthReady;
        }

        public static bool ShouldRemainInReplenishment(
            bool needsReplenishment, bool departureStrengthReady,
            bool reserveAvailable)
        {
            return reserveAvailable &&
                   ShouldRemainInReplenishment(needsReplenishment,
                       departureStrengthReady);
        }

        public static bool ShouldHandoffObjective(ArmyRtsState nextState,
            bool targetComplete, bool targetValid)
        {
            return nextState == ArmyRtsState.Idle &&
                   (targetComplete || !targetValid);
        }

        public static bool HasRallyReadiness(bool departureStrengthReady,
            bool formationObservationComplete, bool formationRallyReady)
        {
            return departureStrengthReady && formationObservationComplete &&
                   formationRallyReady;
        }

        public static bool HasIncrementalRallyReadiness(
            bool departureStrengthReady, int rosterLiving,
            int ralliedFollowers, bool captainPresent)
        {
            return departureStrengthReady &&
                   HasIncrementalEscortQuorum(rosterLiving,
                       ralliedFollowers, captainPresent);
        }

        public static bool HasIncrementalEscortQuorum(int rosterLiving,
            int ralliedFollowers, bool captainPresent)
        {
            if (!captainPresent) return false;
            int followers = Math.Max(0, ralliedFollowers);
            int rallied = followers == int.MaxValue
                ? int.MaxValue
                : followers + 1;
            return HasDeploymentQuorum(rallied, rosterLiving);
        }

        public static bool CanCaptainAdvanceWithEscort(
            bool requiresEscort, int rosterLiving, int nearbyFollowers,
            bool captainPresent, bool immediateCombat,
            bool transportOwnsMovement)
        {
            if (!requiresEscort || transportOwnsMovement) return true;
            if (!captainPresent || rosterLiving <
                    ArmyLogisticsRules.MinimumOperationalForce ||
                nearbyFollowers <= 0) return false;
            return HasIncrementalEscortQuorum(rosterLiving,
                nearbyFollowers, captainPresent);
        }

        public static bool IsViableAttackAssignment(
            bool attackAssignment, bool warActive,
            ArmyRtsObjectiveState objectiveState, int living,
            int targetStrength, int minimumOperationalForce)
        {
            bool minimumReady = Math.Max(0, living) >=
                                Math.Max(1, minimumOperationalForce);
            return attackAssignment && warActive &&
                   objectiveState == ArmyRtsObjectiveState.OpenAttack &&
                   HasDepartureStrength(living, targetStrength,
                       minimumReady, replenishmentBypassActive: false);
        }

        public static bool HasRallyReadiness(bool departureStrengthReady)
        {
            return departureStrengthReady;
        }

        public static bool ShouldForcePreDeparture(bool authoritative,
            ArmyRtsState state, bool minimumForceReady,
            bool captainPresent, double issuedWorldTime,
            double currentWorldTime)
        {
            return ShouldForcePreDeparture(authoritative, state,
                minimumForceReady, captainPresent, escortQuorum: false,
                issuedWorldTime, currentWorldTime);
        }

        public static bool ShouldForcePreDeparture(bool authoritative,
            ArmyRtsState state, bool minimumForceReady,
            bool captainPresent, bool escortQuorum,
            double issuedWorldTime, double currentWorldTime)
        {
            if (!authoritative || !minimumForceReady || !captainPresent ||
                !escortQuorum ||
                (state != ArmyRtsState.Rally &&
                 state != ArmyRtsState.Replenish) ||
                double.IsNaN(issuedWorldTime) ||
                double.IsInfinity(issuedWorldTime) || issuedWorldTime < 0d ||
                double.IsNaN(currentWorldTime) ||
                double.IsInfinity(currentWorldTime)) return false;
            return currentWorldTime - issuedWorldTime >=
                   MaximumPreDepartureWaitWorldSeconds;
        }

        public static bool ShouldForceDeployment(bool authoritative,
            ArmyRtsState state, bool minimumForceReady,
            bool captainPresent, bool routeArrived,
            double deploymentStartedWorldTime, double currentWorldTime)
        {
            return ShouldForceDeployment(authoritative, state,
                minimumForceReady, captainPresent, escortQuorum: false,
                routeArrived, deploymentStartedWorldTime,
                currentWorldTime);
        }

        public static bool ShouldForceDeployment(bool authoritative,
            ArmyRtsState state, bool minimumForceReady,
            bool captainPresent, bool escortQuorum, bool routeArrived,
            double deploymentStartedWorldTime, double currentWorldTime)
        {
            if (!authoritative || state != ArmyRtsState.Deploy ||
                !minimumForceReady || !captainPresent || !escortQuorum ||
                !routeArrived ||
                double.IsNaN(deploymentStartedWorldTime) ||
                double.IsInfinity(deploymentStartedWorldTime) ||
                deploymentStartedWorldTime < 0d ||
                double.IsNaN(currentWorldTime) ||
                double.IsInfinity(currentWorldTime)) return false;
            return currentWorldTime - deploymentStartedWorldTime >=
                   MaximumPreDepartureWaitWorldSeconds;
        }

        public static bool HasRegroupReadiness(bool departureStrengthReady,
            ArmyRtsRole role, bool directorForceReady,
            bool retreatMission)
        {
            return departureStrengthReady &&
                   (retreatMission || role != ArmyRtsRole.Reinforcement ||
                    directorForceReady);
        }

        public static bool ShouldClearRallyFormationAnchor(
            ArmyRtsState currentState, ArmyRtsState nextState,
            bool routeSubmitted, bool routeArrived)
        {
            return currentState == ArmyRtsState.Rally &&
                   nextState == ArmyRtsState.March &&
                   !routeSubmitted && !routeArrived;
        }

        public static bool ShouldAdvanceStrategicRoute(
            ArmyRtsState currentState, ArmyRtsState nextState,
            bool rallyReady)
        {
            if (nextState == ArmyRtsState.Retreat ||
                nextState == ArmyRtsState.Pursue) return true;
            if (nextState != ArmyRtsState.March ||
                currentState == ArmyRtsState.Retreat ||
                currentState == ArmyRtsState.Regroup) return false;
            return currentState != ArmyRtsState.Rally || rallyReady;
        }

        public static bool ShouldRetryMissingStrategicRoute(
            ArmyRtsState currentState, ArmyRtsState nextState,
            bool routeSubmitted, bool routeArrived, bool transportActive)
        {
            if (routeSubmitted || routeArrived || transportActive)
                return false;
            return nextState == ArmyRtsState.March ||
                   nextState == ArmyRtsState.Pursue ||
                   nextState == ArmyRtsState.Retreat;
        }

        public static int ResolveStableStrategicEndpoint(int lockedTileId,
            bool lockedEndpointLive, int candidateTileId)
        {
            return lockedEndpointLive && lockedTileId >= 0
                ? lockedTileId
                : candidateTileId;
        }

        public static bool HasDeploymentReadiness(
            bool formationObservationComplete, bool formationDeployed)
        {
            return formationObservationComplete && formationDeployed;
        }

        public static bool ShouldRequestReplenishment(
            bool authoritative, ArmyRtsState nextState,
            bool requestAlreadyIssued, int missingStrength)
        {
            return authoritative && OwnsReplenishmentRequest(nextState) &&
                   !requestAlreadyIssued && missingStrength > 0;
        }

        public static bool SupportsReplenishment(ArmyRtsState pState)
        {
            return pState == ArmyRtsState.Rally ||
                   pState == ArmyRtsState.Replenish ||
                   pState == ArmyRtsState.Regroup ||
                   pState == ArmyRtsState.March ||
                   pState == ArmyRtsState.Deploy ||
                   pState == ArmyRtsState.Assault ||
                   pState == ArmyRtsState.Hold ||
                   pState == ArmyRtsState.Pursue;
        }

        public static bool OwnsReplenishmentRequest(ArmyRtsState pState)
        {
            return pState == ArmyRtsState.Replenish ||
                   pState == ArmyRtsState.Regroup;
        }

        public static bool ShouldBypassStalledReadiness(
            bool authoritative, bool minimumForceReady,
            bool readinessComplete, bool progressStalled)
        {
            return authoritative && minimumForceReady &&
                   !readinessComplete && progressStalled;
        }

        public static bool ShouldRetryStalledReplenishment(
            bool authoritative, bool minimumForceReady,
            bool departureStrengthReady,
            bool requestAlreadyIssued, bool progressStalled)
        {
            return authoritative && minimumForceReady &&
                   !departureStrengthReady &&
                   requestAlreadyIssued && progressStalled;
        }

        public static bool ShouldPursueCompletedTarget(
            bool targetComplete, bool pursuitAlreadyCompleted,
            bool assaultRole, bool supplyReady,
            bool inMissionCorridor,
            bool hostileWarriorInsideTargetCity)
        {
            return targetComplete && !pursuitAlreadyCompleted &&
                   assaultRole && supplyReady && inMissionCorridor &&
                   hostileWarriorInsideTargetCity;
        }

        public static bool ShouldOwnMilitaryActor(bool authoritative,
            bool actorValid, bool currentProfessionIsWarrior,
            bool hasArmyIndex, bool armyMissionActive,
            bool isCivilAuthority = false,
            bool isCurrentCaptain = false)
        {
            bool militaryRole = isCurrentCaptain ||
                                currentProfessionIsWarrior &&
                                !isCivilAuthority;
            return authoritative && actorValid && militaryRole &&
                   hasArmyIndex && armyMissionActive;
        }

        public static bool ShouldRegroupInsteadOfRetreat(
            bool pLocalForceAdvantage, bool pOpenObjective)
        {
            return pLocalForceAdvantage && pOpenObjective;
        }

        public static bool HasLocalForceAdvantage(int pFriendlyForce,
            int pEnemyForce)
        {
            return pFriendlyForce > 0 &&
                   pFriendlyForce > System.Math.Max(0, pEnemyForce);
        }

        public static ArmyRtsState ResolveState(
            ArmyRtsTransitionFacts pFacts)
        {
            if (pFacts == null || !pFacts.HasMission)
                return ArmyRtsState.Idle;

            if (pFacts.CurrentState == ArmyRtsState.Retreat)
                return pFacts.RetreatArrived
                    ? ArmyRtsState.Regroup
                    : ArmyRtsState.Retreat;

            if (pFacts.CurrentState == ArmyRtsState.Regroup)
            {
                bool holdAdvantage = ShouldRegroupInsteadOfRetreat(
                    pFacts.LocalForceAdvantage, pFacts.OpenObjective);
                if (pFacts.RegroupRecoveryStalled && !holdAdvantage)
                    return ArmyRtsState.Retreat;
                bool retreatStrengthReady =
                    pFacts.Posture == ArmyRtsPosture.Retreat &&
                    pFacts.MinimumForceReady;
                bool recovered =
                    (pFacts.RegroupReady || retreatStrengthReady) &&
                                 pFacts.Organization >=
                                 RegroupOrganization &&
                                 pFacts.Supply > CriticalSupply;
                return recovered
                    ? ArmyRtsState.Rally
                    : ArmyRtsState.Regroup;
            }

            if (pFacts.CurrentState == ArmyRtsState.Pursue &&
                pFacts.PursuitComplete)
                return pFacts.PursuitRequiresRegroup
                    ? ArmyRtsState.Regroup
                    : ArmyRtsState.Hold;

            if (pFacts.Posture == ArmyRtsPosture.Retreat)
                return ArmyRtsState.Retreat;

            bool logisticsRetreat = pFacts.Supply <= CriticalSupply ||
                pFacts.Organization <= RetreatOrganization &&
                !pFacts.SurvivalException;
            if (logisticsRetreat && !ShouldRegroupInsteadOfRetreat(
                    pFacts.LocalForceAdvantage, pFacts.OpenObjective))
                return ArmyRtsState.Retreat;
            if (logisticsRetreat)
                return ArmyRtsState.Regroup;

            if (!pFacts.TargetValid) return ArmyRtsState.Idle;
            if (pFacts.TargetComplete)
            {
                if (pFacts.PursuitAllowed) return ArmyRtsState.Pursue;
                return ArmyRtsState.Idle;
            }
            bool operationCommitted = IsOperationCommitted(
                pFacts.CurrentState);
            if (operationCommitted && !pFacts.MinimumForceReady &&
                !pFacts.SurvivalException)
                return ArmyRtsState.Retreat;
            if (pFacts.NeedsReplenishment)
                return ArmyRtsState.Replenish;
            if (pFacts.CurrentState == ArmyRtsState.March &&
                !pFacts.RouteArrived)
                return ArmyRtsState.March;
            if (!operationCommitted &&
                (!pFacts.ForceReady || !pFacts.RallyReady))
                return ArmyRtsState.Rally;
            if (!pFacts.RouteArrived) return ArmyRtsState.March;
            if (!pFacts.DeploymentReady) return ArmyRtsState.Deploy;

            if (pFacts.EnemyContact ||
                pFacts.Posture == ArmyRtsPosture.Attack ||
                pFacts.Role == ArmyRtsRole.Assault)
                return ArmyRtsState.Assault;
            return ArmyRtsState.Hold;
        }

        private static bool IsOperationCommitted(ArmyRtsState pState)
        {
            return pState == ArmyRtsState.March ||
                   pState == ArmyRtsState.Deploy ||
                   pState == ArmyRtsState.Assault ||
                   pState == ArmyRtsState.Hold ||
                   pState == ArmyRtsState.Pursue;
        }
    }
}
