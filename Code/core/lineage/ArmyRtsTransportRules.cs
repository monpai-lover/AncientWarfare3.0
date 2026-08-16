namespace AncientWarfare3.core.lineage
{
    public enum ArmyRtsTransportPhase
    {
        None = 0,
        AwaitingPickup = 1,
        Embarking = 2,
        Sailing = 3,
        Landing = 4
    }

    public enum ArmyRtsTransportExpectedMemberAction
    {
        RemoveInvalid = 0,
        AwaitTransport = 1,
        HoldLanded = 2
    }

    public enum ArmyRtsTransportVoyageAction
    {
        Active = 0,
        Complete = 1,
        Retry = 2
    }

    public enum ArmyRtsPassengerTaskAction
    {
        None = 0,
        WaitForLoading = 1,
        StartVanillaEmbark = 2,
        PreserveVanillaBoatTask = 3,
        PreserveLandingTask = 4,
        ResumeRts = 5
    }

    public sealed class ArmyRtsTransportActiveClock
    {
        private double _pausedSeconds;
        private double _pauseStartedRealtime;
        private double _lastObservedRealtime;
        private bool _initialized;
        private bool _paused;

        public void Observe(double pRealtime, bool pPaused)
        {
            double now = NormalizeTime(pRealtime);
            if (!_initialized || now < _lastObservedRealtime)
            {
                Reset();
                _initialized = true;
                _lastObservedRealtime = now;
                _paused = pPaused;
                if (pPaused) _pauseStartedRealtime = now;
                return;
            }

            if (pPaused && !_paused)
            {
                _paused = true;
                _pauseStartedRealtime = now;
            }
            else if (!pPaused && _paused)
            {
                _pausedSeconds += System.Math.Max(0d,
                    now - _pauseStartedRealtime);
                _paused = false;
            }
            _lastObservedRealtime = now;
        }

        public double Current(double pRealtime)
        {
            double now = NormalizeTime(pRealtime);
            if (!_initialized) return now;
            double pausedSeconds = _pausedSeconds;
            if (_paused)
                pausedSeconds += System.Math.Max(0d,
                    now - _pauseStartedRealtime);
            return System.Math.Max(0d, now - pausedSeconds);
        }

        public void Reset()
        {
            _pausedSeconds = 0d;
            _pauseStartedRealtime = 0d;
            _lastObservedRealtime = 0d;
            _initialized = false;
            _paused = false;
        }

        private static double NormalizeTime(double pValue)
        {
            return double.IsNaN(pValue) || double.IsInfinity(pValue)
                ? 0d
                : System.Math.Max(0d, pValue);
        }
    }

    public static class ArmyRtsTransportRules
    {
        public const int InvalidBoatPriority = 0;
        public const int CombatBoatPriority = 1;
        public const int DedicatedTransportPriority = 2;
        public const double PendingTimeoutSeconds = 120d;
        public const double AssignedTimeoutSeconds = 240d;
        public const int MaximumPreEmbarkTimeouts = 3;

        public static int BoatTransportPriority(bool isBoat,
            bool isDedicatedTransport, bool skipsFightLogic)
        {
            if (isDedicatedTransport)
                return DedicatedTransportPriority;
            if (!isBoat || skipsFightLogic)
                return InvalidBoatPriority;
            return CombatBoatPriority;
        }

        public static ArmyRtsTransportPhase ResolvePhase(bool voyageActive,
            bool hasAssignedBoat, bool anyEmbarked, bool anyLanded)
        {
            if (!voyageActive) return ArmyRtsTransportPhase.None;
            if (anyEmbarked) return ArmyRtsTransportPhase.Sailing;
            if (anyLanded) return ArmyRtsTransportPhase.Landing;
            return hasAssignedBoat
                ? ArmyRtsTransportPhase.Embarking
                : ArmyRtsTransportPhase.AwaitingPickup;
        }

        public static bool ShouldUseTransportBeforeLandRoute(
            bool authoritative, bool strategicMovementReady,
            bool actorTileValid, bool targetTileValid, bool sameIsland,
            bool transportRouteConfirmed = false)
        {
            return authoritative && strategicMovementReady &&
                   actorTileValid && targetTileValid && !sameIsland &&
                   transportRouteConfirmed;
        }

        public static bool ShouldEscalateCrossIslandRouteFailure(
            bool routeFailed, bool sameIsland,
            bool transportAlreadyActive)
        {
            return routeFailed && !sameIsland &&
                   !transportAlreadyActive;
        }

        public static bool ShouldUseSelectedTransport(bool authoritative,
            bool strategicMovementReady, bool actorTileValid,
            bool targetTileValid, bool transportSelected)
        {
            return authoritative && strategicMovementReady &&
                   actorTileValid && targetTileValid &&
                   transportSelected;
        }

        public static bool ShouldAllowVanillaLandAttack(
            bool sourceTileValid, bool targetTileValid, bool sameIsland)
        {
            return sourceTileValid && targetTileValid && sameIsland;
        }

        public static bool ShouldInitiateTransportImmediately(
            bool routeRequiresTransport, bool voyageAlreadyActive,
            bool captainCanBeginTransport)
        {
            return routeRequiresTransport && !voyageAlreadyActive &&
                   captainCanBeginTransport;
        }

        public static bool CanCreateVoyageState(bool actorInsideBoat,
            bool physicalRouteAvailable)
        {
            return actorInsideBoat || physicalRouteAvailable;
        }

        public static bool ShouldReplaceActiveVoyageTarget(
            bool activeTargetMatches, bool callerMayBegin,
            bool callerIsCaptain, bool forceTransport,
            bool hasEmbarkedMembers)
        {
            return !activeTargetMatches && callerMayBegin &&
                   callerIsCaptain && forceTransport &&
                   !hasEmbarkedMembers;
        }

        public static bool ShouldRunActorTransportHandler(
            bool activeVoyage, bool transportRouteConfirmed)
        {
            return activeVoyage || transportRouteConfirmed;
        }

        public static bool IsProtectedVanillaPassengerTask(string pTaskId)
        {
            return pTaskId == "check_warrior_transport" ||
                   pTaskId == "force_into_a_boat" ||
                   pTaskId == "embark_into_boat" ||
                   pTaskId == "sit_inside_boat";
        }

        public static ArmyRtsPassengerTaskAction ResolvePassengerTaskAction(
            bool transportOwned, bool insideBoat, bool requestLoading,
            bool protectedBoatTask, bool landingTask,
            bool stableTargetLand)
        {
            if (!transportOwned) return ArmyRtsPassengerTaskAction.None;
            if (insideBoat || protectedBoatTask)
                return ArmyRtsPassengerTaskAction.PreserveVanillaBoatTask;
            if (landingTask)
                return ArmyRtsPassengerTaskAction.PreserveLandingTask;
            if (stableTargetLand)
                return ArmyRtsPassengerTaskAction.ResumeRts;
            return requestLoading
                ? ArmyRtsPassengerTaskAction.StartVanillaEmbark
                : ArmyRtsPassengerTaskAction.WaitForLoading;
        }

        public static bool ShouldDrivePassengerTaskInMilitaryP0(
            bool insideBoat, bool protectedBoatTask, bool landingTask)
        {
            return !insideBoat && (protectedBoatTask || landingTask);
        }

        public static bool ShouldProcessFrame(int activeVoyageCount)
        {
            return activeVoyageCount > 0;
        }

        public static bool ShouldProcessInMilitaryP0(bool largeStepMode,
            int activeVoyageCount)
        {
            return largeStepMode && activeVoyageCount > 0;
        }

        public static bool ShouldSuppressCombatForVoyage(
            bool actorIsExpectedPassenger, bool voyageComplete)
        {
            return actorIsExpectedPassenger && !voyageComplete;
        }

        public static bool ShouldAdmitTransportTarget(bool sameIsland,
            bool reachableFrom)
        {
            return !sameIsland;
        }

        public static bool ShouldOwnActor(bool authoritative,
            bool actorValid, bool targetValid, bool insideBoat,
            bool sameIsland, bool forceTransport = false)
        {
            return authoritative && actorValid && targetValid &&
                   (insideBoat || forceTransport || !sameIsland);
        }

        public static ArmyRtsTransportExpectedMemberAction
            ResolveExpectedMemberAction(bool memberValid,
                bool landedOnTargetIsland)
        {
            if (!memberValid)
                return ArmyRtsTransportExpectedMemberAction.RemoveInvalid;
            return landedOnTargetIsland
                ? ArmyRtsTransportExpectedMemberAction.HoldLanded
                : ArmyRtsTransportExpectedMemberAction.AwaitTransport;
        }

        public static bool ShouldCountAsLanded(bool sameTargetIsland,
            bool actorOnStableLand, bool targetOnStableLand)
        {
            return sameTargetIsland && actorOnStableLand &&
                   targetOnStableLand;
        }

        public static ArmyRtsTransportVoyageAction ResolveVoyageAction(
            int validExpectedMembers, int landedExpectedMembers,
            bool timedOut)
        {
            int valid = System.Math.Max(0, validExpectedMembers);
            int landed = System.Math.Max(0, landedExpectedMembers);
            if (valid == 0 || landed >= valid)
                return ArmyRtsTransportVoyageAction.Complete;
            return timedOut
                ? ArmyRtsTransportVoyageAction.Retry
                : ArmyRtsTransportVoyageAction.Active;
        }

        public static bool ShouldEscalatePreEmbarkTimeout(
            int completedTimeouts, bool anyMemberEmbarked)
        {
            return !anyMemberEmbarked && completedTimeouts >=
                MaximumPreEmbarkTimeouts;
        }

        public static bool ShouldReuseRequest(bool requestExists,
            bool exactTargetTile)
        {
            return requestExists && exactTargetTile;
        }

        public static bool ShouldRemoveFromRequest(bool requestExists,
            bool insideBoat, bool exactTargetTile)
        {
            return requestExists && !insideBoat && !exactTargetTile;
        }

        public static bool ShouldPreserveDirectorOmission(
            bool activeVoyage, bool liveArmy, bool missionValid,
            bool targetComplete)
        {
            return activeVoyage && liveArmy && missionValid &&
                   !targetComplete;
        }

        public static bool ShouldRetainActiveVoyageMission(
            bool activeVoyage, bool currentMissionValid,
            bool currentTargetComplete, bool currentTargetCoolingDown,
            bool currentHomelandEmergency,
            bool proposedHomelandEmergency)
        {
            if (!activeVoyage || !currentMissionValid ||
                currentTargetComplete || currentTargetCoolingDown)
                return false;
            return !proposedHomelandEmergency ||
                   currentHomelandEmergency;
        }

        public static bool SamePhysicalDestination(long previousArmyId,
            long previousKingdomId, long previousWarId,
            long previousTargetCityId, long nextArmyId,
            long nextKingdomId, long nextWarId, long nextTargetCityId)
        {
            return previousArmyId == nextArmyId &&
                   previousKingdomId == nextKingdomId &&
                   previousTargetCityId == nextTargetCityId;
        }

        public static bool PendingRequestTimedOut(double startedAt,
            double currentTime, bool hasAssignedBoat)
        {
            return TransportWaitTimedOut(startedAt, currentTime,
                hasAssignedBoat);
        }

        public static bool TransportWaitTimedOut(double startedAt,
            double currentTime, bool hasAssignedBoat)
        {
            double started = NormalizeTime(startedAt);
            double current = NormalizeTime(currentTime);
            if (current < started) return false;
            double timeout = hasAssignedBoat
                ? AssignedTimeoutSeconds
                : PendingTimeoutSeconds;
            return current - started >= timeout;
        }

        private static double NormalizeTime(double pValue)
        {
            return double.IsNaN(pValue) || double.IsInfinity(pValue)
                ? 0d
                : System.Math.Max(0d, pValue);
        }
    }
}
