using System;
using System.Collections.Generic;
using AncientWarfare3.api.multiplayer;
#if !AW3_RULES_TESTS
using AncientWarfare3.core.lineage;
#endif

namespace AncientWarfare3.core.multiplayer
{
    internal interface IAW3MultiplayerStrategicStateStore
    {
        bool IsMainThread { get; }
        IReadOnlyList<AW3MultiplayerArmyProjection> CaptureArmies();
        IReadOnlyList<AW3MultiplayerActorProjection> CaptureActors();
        bool HasArmy(long pArmyId);
        bool HasActor(long pActorId);
        void ApplyArmy(AW3MultiplayerArmyProjection pArmy);
        void ApplyActor(AW3MultiplayerActorProjection pActor);
        void CompleteArmySnapshot(
            IReadOnlyList<AW3MultiplayerArmyProjection> pArmies);
        void RebuildMilitaryReadModels();
    }

    internal static class AW3MultiplayerStrategicStateCoordinator
    {
        internal static AW3MultiplayerStrategicSnapshot Capture(long pTick,
            IAW3MultiplayerStrategicStateStore pStore)
        {
            if (pTick < 0)
                throw new ArgumentOutOfRangeException(nameof(pTick));
            if (pStore == null) throw new ArgumentNullException(nameof(pStore));
            if (!pStore.IsMainThread)
                throw new AW3MultiplayerStrategicCaptureException(
                    AW3MultiplayerStrategicError.WrongThread,
                    "Strategic capture requires the WorldBox main thread.");
            try
            {
                return new AW3MultiplayerStrategicSnapshot(pTick,
                    pStore.CaptureArmies(), pStore.CaptureActors());
            }
            catch (AW3MultiplayerStrategicCaptureException)
            {
                throw;
            }
            catch (Exception error)
            {
                throw new AW3MultiplayerStrategicCaptureException(
                    AW3MultiplayerStrategicError.CaptureFailed,
                    error.Message, error);
            }
        }

        internal static AW3MultiplayerStrategicApplyResult Apply(
            AW3MultiplayerStrategicSnapshot pSnapshot,
            IAW3MultiplayerStrategicStateStore pStore)
        {
            if (pStore == null)
                return AW3MultiplayerStrategicApplyResult.Failure(
                    AW3MultiplayerStrategicError.InvalidSnapshot,
                    "Strategic state store is required.");
            if (!pStore.IsMainThread)
                return AW3MultiplayerStrategicApplyResult.Failure(
                    AW3MultiplayerStrategicError.WrongThread,
                    "Strategic apply requires the WorldBox main thread.");
            if (pSnapshot == null)
                return AW3MultiplayerStrategicApplyResult.Failure(
                    AW3MultiplayerStrategicError.InvalidSnapshot,
                    "Strategic snapshot is required.");

            for (var index = 0; index < pSnapshot.Armies.Count; index++)
            {
                long id = pSnapshot.Armies[index].ArmyId;
                if (!pStore.HasArmy(id))
                    return AW3MultiplayerStrategicApplyResult.Failure(
                        AW3MultiplayerStrategicError.MissingArmy,
                        "Snapshot army identity does not exist.", id);
            }
            for (var index = 0; index < pSnapshot.Actors.Count; index++)
            {
                long id = pSnapshot.Actors[index].ActorId;
                if (!pStore.HasActor(id))
                    return AW3MultiplayerStrategicApplyResult.Failure(
                        AW3MultiplayerStrategicError.MissingActor,
                        "Snapshot actor identity does not exist.", id);
            }

            try
            {
                using (AW3MultiplayerReplicaScope.EnterApply())
                {
                    for (var index = 0;
                         index < pSnapshot.Armies.Count; index++)
                        pStore.ApplyArmy(pSnapshot.Armies[index]);
                    for (var index = 0;
                         index < pSnapshot.Actors.Count; index++)
                        pStore.ApplyActor(pSnapshot.Actors[index]);
                    pStore.CompleteArmySnapshot(pSnapshot.Armies);
                    pStore.RebuildMilitaryReadModels();
                }
                return AW3MultiplayerStrategicApplyResult.Success(
                    pSnapshot.Armies.Count, pSnapshot.Actors.Count);
            }
            catch (Exception error)
            {
                return AW3MultiplayerStrategicApplyResult.Failure(
                    AW3MultiplayerStrategicError.ApplyFailed,
                    error.Message);
            }
        }
    }

#if !AW3_RULES_TESTS
    internal sealed class AW3MultiplayerStrategicWorldStore :
        IAW3MultiplayerStrategicStateStore
    {
        public bool IsMainThread => ThreadHelper.isMainThread();

        public IReadOnlyList<AW3MultiplayerArmyProjection> CaptureArmies()
        {
            var result = new List<AW3MultiplayerArmyProjection>();
            if (World.world?.armies == null) return result;
            foreach (Army army in World.world.armies)
            {
                if (army?.data == null || !army.isAlive()) continue;
                Actor captain = null;
                try { captain = army.getCaptain(); }
                catch { }
                string orderId = captain?.ai?.task?.id ?? string.Empty;
                WorldTile target = captain?.tile_target;
                AW3MultiplayerStrategicTargetKind targetKind =
                    target?.data == null
                        ? AW3MultiplayerStrategicTargetKind.None
                        : AW3MultiplayerStrategicTargetKind.Tile;
                ArmyRtsState operationalState = ArmyRtsState.Idle;
                ArmyRtsRole rtsRole = ArmyRtsRole.Reserve;
                ArmyRtsPosture posture = ArmyRtsPosture.Automatic;
                long warId = -1L;
                long frontId = -1L;
                long targetId = -1L;
                int supply = 100;
                int organization = 100;
                bool playerOrder = false;
                if (ArmyRtsControllerService.TryGetProjection(army,
                        out ArmyRtsStrategicProjection projection))
                {
                    operationalState = projection.State;
                    rtsRole = projection.Role;
                    posture = projection.Posture;
                    warId = projection.WarId;
                    frontId = projection.FrontId;
                    targetId = projection.TargetCityId;
                    supply = projection.Supply;
                    organization = projection.Organization;
                    playerOrder = projection.PlayerOrder;
                    if (targetId >= 0L)
                        targetKind =
                            AW3MultiplayerStrategicTargetKind.City;
                }
                result.Add(new AW3MultiplayerArmyProjection(army.id,
                    AWArmyService.GetRole(army),
                    AWArmyService.GetAnchorCityId(army), orderId,
                    targetKind, targetId, target?.x ?? -1,
                    target?.y ?? -1,
                    operationalState.ToString().ToLowerInvariant(),
                    posture.ToString().ToLowerInvariant(), warId, frontId,
                    supply, organization, playerOrder,
                    rtsRole.ToString().ToLowerInvariant()));
            }
            return result;
        }

        public IReadOnlyList<AW3MultiplayerActorProjection> CaptureActors()
        {
            var result = new List<AW3MultiplayerActorProjection>();
            List<Actor> units = World.world?.units?.units_only_alive;
            if (units == null) return result;
            for (var index = 0; index < units.Count; index++)
            {
                Actor actor = units[index];
                if (actor?.data == null || !actor.isAlive()) continue;
                bool isGeneral = GeneralService.IsActiveGeneralFast(actor);
                if (actor.army?.data == null && !isGeneral) continue;
                actor.data.get(LineageKeys.GENERAL_MERIT,
                    out int merit, 0);
                result.Add(new AW3MultiplayerActorProjection(actor.data.id,
                    isGeneral, Math.Max(0, merit)));
            }
            return result;
        }

        public bool HasArmy(long pArmyId)
        {
            if (pArmyId < 0 || World.world?.armies == null) return false;
            try { return World.world.armies.get(pArmyId)?.data != null; }
            catch { return false; }
        }

        public bool HasActor(long pActorId)
        {
            if (pActorId < 0 || World.world?.units == null) return false;
            try { return World.world.units.get(pActorId)?.data != null; }
            catch { return false; }
        }

        public void ApplyArmy(AW3MultiplayerArmyProjection pArmy)
        {
            Army army = World.world.armies.get(pArmy.ArmyId);
            if (army?.data == null)
                throw new InvalidOperationException(
                    "Snapshot army identity no longer exists.");
            army.data.set(LineageKeys.AW_ARMY_ROLE, pArmy.RoleId);
            army.data.set(LineageKeys.AW_ARMY_CITY_ID,
                pArmy.AnchorCityId);
            ArmyRtsControllerService.InstallReplicaProjection(army,
                pArmy.OperationalStateId, pArmy.RtsRoleId,
                pArmy.PostureId, pArmy.WarId, pArmy.FrontId,
                pArmy.TargetKind ==
                    AW3MultiplayerStrategicTargetKind.City
                    ? pArmy.TargetId
                    : -1L,
                pArmy.Supply, pArmy.Organization, pArmy.PlayerOrder);
        }

        public void ApplyActor(AW3MultiplayerActorProjection pActor)
        {
            Actor actor = World.world.units.get(pActor.ActorId);
            if (actor?.data == null)
                throw new InvalidOperationException(
                    "Snapshot actor identity no longer exists.");
            actor.data.set(LineageKeys.GENERAL_ACTIVE, pActor.IsGeneral);
            actor.data.set(LineageKeys.GENERAL_MERIT,
                pActor.GeneralMerit);
        }

        public void CompleteArmySnapshot(
            IReadOnlyList<AW3MultiplayerArmyProjection> pArmies)
        {
            var retained = new long[pArmies.Count];
            for (var index = 0; index < pArmies.Count; index++)
                retained[index] = pArmies[index].ArmyId;
            ArmyRtsControllerService.RetainReplicaProjections(retained);
        }

        public void RebuildMilitaryReadModels()
        {
            AWArmyService.RepairSpecialArmiesAfterLoad();
            KingdomMilitaryReadinessService.RebuildRuntime();
            MilitaryEmergencyService.RebuildRuntime();
        }
    }
#endif
}
